import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { ChatMessage, ChatThread } from '../../shared/models/chat.models';
import {
  findMessageByCorrelators,
  gapFillAfterSeq,
  idsEqual,
  markReplyQuotesDeleted,
  mergeMessagesById,
  replyPreviewText,
  upsertRemoteMessage,
} from './message-sync';

@Injectable({ providedIn: 'root' })
export class ThreadStore {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly hub = inject(ChatHubService);
  private readonly channels = inject(ChannelStore);

  private readonly activeSignal = signal<ChatThread | null>(null);
  private readonly messagesSignal = signal<ChatMessage[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly sendingSignal = signal(false);
  private readonly openSignal = signal(false);
  private readonly replyTargetSignal = signal<ChatMessage | null>(null);
  private readonly editingMessageSignal = signal<ChatMessage | null>(null);
  private gapFillInFlight = false;

  readonly active = this.activeSignal.asReadonly();
  readonly messages = this.messagesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly sending = this.sendingSignal.asReadonly();
  readonly open = this.openSignal.asReadonly();
  readonly replyTarget = this.replyTargetSignal.asReadonly();
  readonly editingMessage = this.editingMessageSignal.asReadonly();
  readonly sortedMessages = computed(() =>
    [...this.messagesSignal()].sort(
      (a, b) => (a.seq ?? 0) - (b.seq ?? 0) || a.createdAt.localeCompare(b.createdAt),
    ),
  );

  constructor() {
    this.hub.onMessage((message) => this.ingestRemote(message));
    this.hub.onMessageEdited((patch) => this.applyEdit(patch));
    this.hub.onMessageDeleted((patch) => this.applyDelete(patch.id));
    this.hub.onReactionChanged((event) => this.applyReactions(event.messageId, event.reactions));
    this.hub.onAttachmentThumbnailReady((event) => this.applyThumbnailReady(event));
    this.hub.onLinkPreviewReady((event) => this.applyLinkPreviewReady(event));
  }

  async openFromMessage(channelId: string, messageId: string): Promise<void> {
    this.openSignal.set(true);
    this.loadingSignal.set(true);
    this.replyTargetSignal.set(null);
    this.editingMessageSignal.set(null);
    try {
      if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
        const demo = this.demoThread(channelId, messageId);
        this.activeSignal.set(demo);
        this.messagesSignal.set([]);
        return;
      }

      const thread = await this.api.openThread(channelId, messageId);
      const detailed = await this.api.getThread(thread.id);
      const replies = await this.api.getThreadMessages(thread.id);
      this.activeSignal.set(detailed);
      this.messagesSignal.set(replies.map((m) => this.normalize(m)));
    } catch {
      this.activeSignal.set(null);
      this.messagesSignal.set([]);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  close(): void {
    this.openSignal.set(false);
    this.activeSignal.set(null);
    this.messagesSignal.set([]);
    this.replyTargetSignal.set(null);
    this.editingMessageSignal.set(null);
  }

  setReplyTarget(message: ChatMessage | null): void {
    if (!message || message.deletedAt) {
      this.replyTargetSignal.set(null);
      return;
    }
    this.editingMessageSignal.set(null);
    this.replyTargetSignal.set(message);
  }

  clearReplyTarget(): void {
    this.replyTargetSignal.set(null);
  }

  startEdit(message: ChatMessage | null): void {
    if (
      !message ||
      message.deletedAt ||
      !message.mine ||
      message.status !== 'persisted'
    ) {
      this.editingMessageSignal.set(null);
      return;
    }
    this.replyTargetSignal.set(null);
    this.editingMessageSignal.set(message);
  }

  clearEdit(): void {
    this.editingMessageSignal.set(null);
  }

  lastOwnPersistedMessage(): ChatMessage | null {
    const parent = this.activeSignal()?.parentMessage;
    const candidates = [
      ...(parent ? [parent] : []),
      ...this.sortedMessages(),
    ];
    for (let i = candidates.length - 1; i >= 0; i--) {
      const m = candidates[i];
      if (m.mine && !m.deletedAt && m.status === 'persisted') return m;
    }
    return null;
  }

  async edit(messageId: string, body: string): Promise<void> {
    const thread = this.activeSignal();
    const text = body.trim();
    if (!thread || !text) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.applyEdit({
        id: messageId,
        channelId: thread.channelId,
        body: text,
        editedAt: new Date().toISOString(),
      });
      this.editingMessageSignal.set(null);
      return;
    }

    const updated = await this.api.editMessage(thread.channelId, messageId, text);
    this.applyEdit({
      id: updated.id,
      channelId: updated.channelId,
      body: updated.body,
      editedAt: updated.editedAt ?? new Date().toISOString(),
      seq: updated.seq,
    });
    this.editingMessageSignal.set(null);
  }

  bumpReplyCount(threadId: string): void {
    const active = this.activeSignal();
    if (active && idsEqual(active.id, threadId)) {
      this.activeSignal.set({ ...active, replyCount: (active.replyCount ?? 0) + 1 });
    }
  }

  /** History reconcile after SignalR reconnect while a thread panel is open (BUG-009). */
  async gapFillActive(): Promise<void> {
    const thread = this.activeSignal();
    if (!thread || !this.openSignal() || this.channels.isDemo() || this.auth.isOfflineDemo()) {
      return;
    }
    if (this.gapFillInFlight) return;
    this.gapFillInFlight = true;
    try {
      let maxSeq = 0;
      for (const message of this.messagesSignal()) {
        const seq = message.seq ?? 0;
        if (seq > maxSeq) maxSeq = seq;
      }
      const after = gapFillAfterSeq(maxSeq);
      const messages = await this.api.getThreadMessages(thread.id, 100);
      const incoming = after > 0 ? messages.filter((m) => (m.seq ?? 0) > after) : messages;
      const mergeSource = incoming.length ? incoming : messages;
      if (!mergeSource.length) return;
      this.messagesSignal.update((current) =>
        mergeMessagesById(
          current,
          mergeSource.map((m) => this.normalize(m)),
        ),
      );
      const detailed = await this.api.getThread(thread.id);
      this.activeSignal.set(detailed);
    } catch {
      // best-effort
    } finally {
      this.gapFillInFlight = false;
    }
  }

  async toggleReaction(messageId: string, emoji: string): Promise<void> {
    const thread = this.activeSignal();
    if (!thread || !emoji) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.applyReactionsLocal(messageId, emoji);
      return;
    }

    const result = await this.api.toggleReaction(thread.channelId, messageId, emoji);
    this.applyReactions(result.messageId, result.reactions);
  }

  async send(body: string): Promise<boolean> {
    const thread = this.activeSignal();
    const profile = this.auth.profile();
    const text = body.trim();
    if (!thread || !text) return false;

    const replyTarget = this.replyTargetSignal();
    const replyToMessageId = replyTarget?.id ?? thread.parentMessageId;
    const replyTo =
      replyTarget && !replyTarget.deletedAt
        ? {
            messageId: replyTarget.id,
            authorName: replyTarget.authorName,
            preview: replyPreviewText(replyTarget.body),
            deleted: false,
          }
        : replyTarget
          ? null
          : thread.parentMessage && !thread.parentMessage.deletedAt
            ? {
                messageId: thread.parentMessageId,
                authorName: thread.parentMessage.authorName,
                preview: replyPreviewText(thread.parentMessage.body),
                deleted: false,
              }
            : {
                messageId: thread.parentMessageId,
                authorName: '',
                preview: '',
                deleted: false,
              };

    const clientMessageId = crypto.randomUUID();
    const idempotencyKey = crypto.randomUUID();
    const optimistic: ChatMessage = {
      id: clientMessageId,
      clientMessageId,
      conversationId: thread.id,
      channelId: thread.channelId,
      authorUserId: profile?.id ?? 'me',
      authorName: profile?.name ?? 'Você',
      body: text,
      createdAt: new Date().toISOString(),
      status: 'sending',
      mine: true,
      threadId: thread.id,
      replyToMessageId,
      replyTo,
      parentMessageId: thread.parentMessageId,
    };

    this.messagesSignal.update((list) => [...list, optimistic]);
    this.replyTargetSignal.set(null);
    this.sendingSignal.set(true);

    try {
      if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
        await new Promise((r) => setTimeout(r, 350));
        this.patchByClientId(clientMessageId, {
          status: 'sent',
          id: crypto.randomUUID(),
          seq: this.messagesSignal().length,
        });
        this.bumpReplyCount(thread.id);
        return true;
      }

      const persisted = await this.api.sendThreadMessage({
        threadId: thread.id,
        body: text,
        clientMessageId,
        idempotencyKey,
        replyToMessageId,
      });

      this.patchByClientId(clientMessageId, {
        ...this.normalize(persisted),
        status: 'persisted',
        mine: true,
        clientMessageId,
      });
      // replyCount is bumped when the outbox/hub event arrives (or by MessageStore)
      return true;
    } catch {
      this.patchByClientId(clientMessageId, { status: 'failed' });
      return false;
    } finally {
      this.sendingSignal.set(false);
    }
  }

  private ingestRemote(message: ChatMessage): void {
    const active = this.activeSignal();
    if (!active || !message.threadId || !idsEqual(message.threadId, active.id)) return;
    if (idsEqual(message.conversationId, message.channelId)) return;

    const normalized = this.normalize(message);
    const mine = normalized.authorUserId === this.auth.profile()?.id;
    const remote: ChatMessage = { ...normalized, mine, status: 'persisted' };
    const existing = findMessageByCorrelators(this.messagesSignal(), remote);

    if (existing?.clientMessageId) {
      this.patchByClientId(existing.clientMessageId, {
        ...remote,
        clientMessageId: existing.clientMessageId,
        mine: true,
      });
      return;
    }

    this.messagesSignal.update((list) => upsertRemoteMessage(list, remote));
  }

  private applyDelete(messageId: string): void {
    this.messagesSignal.update((list) => {
      const deleted = list.map((m) =>
        idsEqual(m.id, messageId)
          ? { ...m, body: '', deletedAt: m.deletedAt ?? new Date().toISOString() }
          : m,
      );
      return markReplyQuotesDeleted(deleted, messageId);
    });
    const active = this.activeSignal();
    if (active?.parentMessage && idsEqual(active.parentMessage.id, messageId)) {
      this.activeSignal.set({
        ...active,
        parentMessage: {
          ...active.parentMessage,
          body: '',
          deletedAt: active.parentMessage.deletedAt ?? new Date().toISOString(),
        },
      });
    }
    const editing = this.editingMessageSignal();
    if (editing && idsEqual(editing.id, messageId)) {
      this.editingMessageSignal.set(null);
    }
  }

  private applyEdit(patch: {
    id: string;
    channelId: string;
    body: string;
    editedAt: string;
    seq?: number;
  }): void {
    const patchList = (list: ChatMessage[]): ChatMessage[] =>
      list.map((m) =>
        idsEqual(m.id, patch.id)
          ? {
              ...m,
              body: patch.body,
              editedAt: patch.editedAt,
              seq: patch.seq ?? m.seq,
              deletedAt: null,
            }
          : m,
      );

    this.messagesSignal.update(patchList);
    const active = this.activeSignal();
    if (active?.parentMessage && idsEqual(active.parentMessage.id, patch.id)) {
      this.activeSignal.set({
        ...active,
        parentMessage: {
          ...active.parentMessage,
          body: patch.body,
          editedAt: patch.editedAt,
          seq: patch.seq ?? active.parentMessage.seq,
          deletedAt: null,
        },
      });
    }
  }

  private patchByClientId(clientMessageId: string, patch: Partial<ChatMessage>): void {
    this.messagesSignal.update((list) =>
      list.map((m) => (idsEqual(m.clientMessageId, clientMessageId) ? { ...m, ...patch } : m)),
    );
  }

  private applyReactions(
    messageId: string,
    reactions: Array<{ emoji: string; count: number; me: boolean }>,
  ): void {
    this.messagesSignal.update((list) =>
      list.map((m) => (idsEqual(m.id, messageId) ? { ...m, reactions: [...reactions] } : m)),
    );
    const active = this.activeSignal();
    if (active?.parentMessage && idsEqual(active.parentMessage.id, messageId)) {
      this.activeSignal.set({
        ...active,
        parentMessage: { ...active.parentMessage, reactions: [...reactions] },
      });
    }
  }

  private applyThumbnailReady(event: {
    attachmentId: string;
    channelId: string;
    thumbnailStatus: string | null;
    width?: number | null;
    height?: number | null;
    pageCount?: number | null;
  }): void {
    const patchAttachments = (list: ChatMessage[]): ChatMessage[] =>
      list.map((m) => {
        if (!idsEqual(m.channelId, event.channelId) || !m.attachments?.length) {
          return m;
        }
        let changed = false;
        const attachments = m.attachments.map((a) => {
          if (!idsEqual(a.id, event.attachmentId)) return a;
          changed = true;
          return {
            ...a,
            thumbnailStatus: event.thumbnailStatus,
            width: event.width ?? a.width,
            height: event.height ?? a.height,
            pageCount: event.pageCount ?? a.pageCount,
          };
        });
        return changed ? { ...m, attachments } : m;
      });

    this.messagesSignal.update(patchAttachments);
    const active = this.activeSignal();
    if (active?.parentMessage) {
      const [parent] = patchAttachments([active.parentMessage]);
      if (parent !== active.parentMessage) {
        this.activeSignal.set({ ...active, parentMessage: parent });
      }
    }
  }

  applyLinkPreviewCleared(messageId: string): void {
    const clear = (list: ChatMessage[]): ChatMessage[] =>
      list.map((m) => (idsEqual(m.id, messageId) ? { ...m, linkPreview: null } : m));

    this.messagesSignal.update(clear);
    const active = this.activeSignal();
    if (active?.parentMessage && idsEqual(active.parentMessage.id, messageId)) {
      this.activeSignal.set({
        ...active,
        parentMessage: { ...active.parentMessage, linkPreview: null },
      });
    }
  }

  private applyLinkPreviewReady(event: {
    channelId: string;
    messageId: string;
    linkPreviewId: string;
    url: string;
    title?: string | null;
    description?: string | null;
    siteName?: string | null;
    hasImage: boolean;
    status: string;
  }): void {
    const patch = (list: ChatMessage[]): ChatMessage[] =>
      list.map((m) => {
        if (!idsEqual(m.id, event.messageId) || !idsEqual(m.channelId, event.channelId)) {
          return m;
        }
        return {
          ...m,
          linkPreview: {
            id: event.linkPreviewId,
            url: event.url,
            title: event.title ?? null,
            description: event.description ?? null,
            siteName: event.siteName ?? null,
            hasImage: event.hasImage,
            status: event.status,
          },
        };
      });

    this.messagesSignal.update(patch);
    const active = this.activeSignal();
    if (active?.parentMessage) {
      const [parent] = patch([active.parentMessage]);
      if (parent !== active.parentMessage) {
        this.activeSignal.set({ ...active, parentMessage: parent });
      }
    }
  }

  private applyReactionsLocal(messageId: string, emoji: string): void {
    const toggle = (list: ChatMessage[]): ChatMessage[] =>
      list.map((m) => {
        if (!idsEqual(m.id, messageId)) return m;
        const current = [...(m.reactions ?? [])];
        const idx = current.findIndex((r) => r.emoji === emoji);
        if (idx >= 0) {
          const item = current[idx];
          if (item.me) {
            if (item.count <= 1) current.splice(idx, 1);
            else current[idx] = { ...item, count: item.count - 1, me: false };
          } else {
            current[idx] = { ...item, count: item.count + 1, me: true };
          }
        } else {
          current.push({ emoji, count: 1, me: true });
        }
        return { ...m, reactions: current };
      });

    this.messagesSignal.update(toggle);
    const active = this.activeSignal();
    if (active?.parentMessage && idsEqual(active.parentMessage.id, messageId)) {
      const [updated] = toggle([active.parentMessage]);
      this.activeSignal.set({ ...active, parentMessage: updated });
    }
  }

  private normalize(message: ChatMessage): ChatMessage {
    return {
      ...message,
      channelId: message.channelId || message.conversationId,
      status: message.status ?? 'persisted',
      mine: message.mine ?? message.authorUserId === this.auth.profile()?.id,
      authorName: message.authorName || 'Membro',
      body: message.deletedAt ? '' : message.body,
      reactions: message.reactions ?? [],
      replyTo: message.replyTo ?? null,
    };
  }

  private demoThread(channelId: string, messageId: string): ChatThread {
    return {
      id: `thread-${messageId}`,
      channelId,
      parentMessageId: messageId,
      createdBy: this.auth.profile()?.id ?? 'me',
      createdAt: new Date().toISOString(),
      replyCount: 0,
      parentMessage: {
        id: messageId,
        conversationId: channelId,
        channelId,
        authorUserId: 'u-alice',
        authorName: 'Alice Mendes',
        body: 'Mensagem de origem da thread (demo).',
        createdAt: new Date().toISOString(),
        status: 'persisted',
        mine: false,
        threadId: `thread-${messageId}`,
        replyCount: 0,
      },
    };
  }
}
