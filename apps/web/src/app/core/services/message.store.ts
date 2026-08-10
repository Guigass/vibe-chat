import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ChatHubService } from './chat-hub.service';
import { AuthService } from '../auth/auth.service';
import { ChannelStore } from './channel.store';
import { ThreadStore } from './thread.store';
import { ChatMessage } from '../../shared/models/chat.models';
import {
  bumpChannelParentForThreadReply,
  findMessageByCorrelators,
  gapFillAfterSeq,
  hasSeqGap,
  idsEqual,
  markReplyQuotesDeleted,
  maxSeqForChannel,
  mergeMessagesById,
  replyPreviewText,
  upsertRemoteMessage,
} from './message-sync';

@Injectable({ providedIn: 'root' })
export class MessageStore {
  private readonly api = inject(ApiService);
  private readonly hub = inject(ChatHubService);
  private readonly auth = inject(AuthService);
  private readonly channels = inject(ChannelStore);
  private readonly threads = inject(ThreadStore);

  private readonly messagesSignal = signal<ChatMessage[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly sendingSignal = signal(false);
  private readonly replyTargetSignal = signal<ChatMessage | null>(null);
  private readonly highlightMessageIdSignal = signal<string | null>(null);
  private highlightTimer: ReturnType<typeof setTimeout> | null = null;
  private gapFillInFlight = new Set<string>();
  private readCursorTimer: ReturnType<typeof setTimeout> | null = null;
  private pendingReadCursor: { channelId: string; seq: number } | null = null;
  private unsubCreated: (() => void) | null = null;
  private unsubEdited: (() => void) | null = null;
  private unsubDeleted: (() => void) | null = null;
  private unsubReactions: (() => void) | null = null;
  private unsubReconnected: (() => void) | null = null;

  readonly messages = this.messagesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly sending = this.sendingSignal.asReadonly();
  readonly replyTarget = this.replyTargetSignal.asReadonly();
  readonly highlightMessageId = this.highlightMessageIdSignal.asReadonly();
  private readonly viewingLatestSignal = signal(true);

  readonly forActiveChannel = computed(() => {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return [];
    return this.messagesSignal()
      .filter(
        (m) =>
          idsEqual(m.channelId, channelId) &&
          (!m.threadId ||
            idsEqual(m.conversationId, channelId) ||
            idsEqual(m.conversationId, m.channelId)),
      )
      .sort((a, b) => (a.seq ?? 0) - (b.seq ?? 0) || a.createdAt.localeCompare(b.createdAt));
  });

  setViewingLatest(value: boolean): void {
    this.viewingLatestSignal.set(value);
  }

  markViewedLatest(): void {
    this.viewingLatestSignal.set(true);
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    const maxSeq = maxSeqForChannel(this.messagesSignal(), channelId);
    if (maxSeq > 0) this.schedulePersistReadCursor(channelId, maxSeq);
  }

  constructor() {
    this.unsubCreated = this.hub.onMessage((message) => this.ingestRemote(message));
    this.unsubEdited = this.hub.onMessageEdited((patch) => this.applyEdit(patch));
    this.unsubDeleted = this.hub.onMessageDeleted((patch) => this.applyDelete(patch));
    this.unsubReactions = this.hub.onReactionChanged((event) =>
      this.applyReactions(event.messageId, event.reactions),
    );
    this.unsubReconnected = this.hub.onReconnected(() => {
      void this.gapFillActiveChannel();
      void this.threads.gapFillActive();
    });
  }

  setReplyTarget(message: ChatMessage | null): void {
    if (!message || message.deletedAt) {
      this.replyTargetSignal.set(null);
      return;
    }
    this.replyTargetSignal.set(message);
  }

  clearReplyTarget(): void {
    this.replyTargetSignal.set(null);
  }

  jumpToMessage(messageId: string): void {
    if (!messageId) return;
    const el = document.querySelector(`[data-message-id="${messageId}"]`);
    if (el instanceof HTMLElement) {
      el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
    if (this.highlightTimer) clearTimeout(this.highlightTimer);
    this.highlightMessageIdSignal.set(messageId);
    this.highlightTimer = setTimeout(() => {
      this.highlightMessageIdSignal.set(null);
      this.highlightTimer = null;
    }, 2000);
  }

  async loadChannel(channelId: string): Promise<void> {
    this.loadingSignal.set(true);
    try {
      await this.hub.joinChannel(channelId);
      if (this.channels.isDemo()) {
        this.messagesSignal.set(this.demoMessages(channelId));
      } else {
        const messages = await this.api.getMessages(channelId);
        this.messagesSignal.update((current) => {
          const others = current.filter((m) => m.channelId !== channelId);
          return [...others, ...messages.map((m) => this.normalize(m))];
        });
        const maxSeq = maxSeqForChannel(this.messagesSignal(), channelId);
        if (maxSeq > 0) {
          void this.persistReadCursor(channelId, maxSeq);
        }
      }
    } catch {
      this.messagesSignal.update((current) => {
        const others = current.filter((m) => m.channelId !== channelId);
        return [...others, ...this.demoMessages(channelId)];
      });
    } finally {
      this.loadingSignal.set(false);
    }
  }

  /** History reconcile after SignalR reconnect or detected seq gap (B-070). */
  async gapFillChannel(channelId: string): Promise<void> {
    if (!channelId || this.channels.isDemo() || this.auth.isOfflineDemo()) return;
    if (this.gapFillInFlight.has(channelId)) return;
    this.gapFillInFlight.add(channelId);
    try {
      const localMax = maxSeqForChannel(this.messagesSignal(), channelId);
      const after = gapFillAfterSeq(localMax);
      const messages = await this.api.getMessages(channelId, { after, take: 100 });
      if (!messages.length) return;
      this.messagesSignal.update((current) =>
        mergeMessagesById(
          current,
          messages.map((m) => this.normalize(m)),
        ),
      );
    } catch {
      // best-effort; live events or next reconnect can retry
    } finally {
      this.gapFillInFlight.delete(channelId);
    }
  }

  private async gapFillActiveChannel(): Promise<void> {
    const channelId = this.channels.activeChannel()?.id;
    if (channelId) {
      await this.gapFillChannel(channelId);
    }
  }

  async send(body: string, attachmentIds: string[] = []): Promise<boolean> {
    const channel = this.channels.activeChannel();
    const profile = this.auth.profile();
    const text = body.trim();
    const hasAttachments = attachmentIds.length > 0;
    if (!channel || (!text && !hasAttachments)) return false;

    const replyTarget = this.replyTargetSignal();
    const replyToMessageId = replyTarget?.id ?? null;
    const replyTo =
      replyTarget && !replyTarget.deletedAt
        ? {
            messageId: replyTarget.id,
            authorName: replyTarget.authorName,
            preview: replyPreviewText(replyTarget.body),
            deleted: false,
          }
        : null;

    const clientMessageId = crypto.randomUUID();
    const idempotencyKey = crypto.randomUUID();
    const optimistic: ChatMessage = {
      id: clientMessageId,
      clientMessageId,
      conversationId: channel.id,
      channelId: channel.id,
      authorUserId: profile?.id ?? 'me',
      authorName: profile?.name ?? 'Você',
      body: text,
      createdAt: new Date().toISOString(),
      status: 'sending',
      mine: true,
      replyToMessageId,
      replyTo,
      attachments: hasAttachments
        ? attachmentIds.map((id) => ({
            id,
            fileName: 'anexo',
            contentType: 'application/octet-stream',
            sizeBytes: 0,
            status: 'PendingUpload',
          }))
        : [],
    };

    this.messagesSignal.update((list) => [...list, optimistic]);
    this.replyTargetSignal.set(null);
    this.sendingSignal.set(true);

    try {
      if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
        await new Promise((r) => setTimeout(r, 450));
        this.patchByClientId(clientMessageId, {
          status: 'sent',
          id: crypto.randomUUID(),
          attachments: hasAttachments
            ? attachmentIds.map((id) => ({
                id,
                fileName: 'anexo',
                contentType: 'application/octet-stream',
                sizeBytes: 0,
                status: 'Ready',
              }))
            : [],
        });
        return true;
      }

      const persisted = await this.api.sendMessage({
        channelId: channel.id,
        body: optimistic.body,
        clientMessageId,
        idempotencyKey,
        attachmentIds,
        replyToMessageId: replyToMessageId ?? undefined,
      });

      this.patchByClientId(clientMessageId, {
        ...this.normalize(persisted),
        status: 'persisted',
        mine: true,
        clientMessageId,
      });
      return true;
    } catch {
      this.patchByClientId(clientMessageId, { status: 'failed' });
      if (replyTarget) this.replyTargetSignal.set(replyTarget);
      return false;
    } finally {
      this.sendingSignal.set(false);
    }
  }

  async edit(messageId: string, body: string): Promise<void> {
    const channel = this.channels.activeChannel();
    if (!channel || !body.trim()) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.messagesSignal.update((list) =>
        list.map((m) =>
          m.id === messageId
            ? { ...m, body: body.trim(), editedAt: new Date().toISOString() }
            : m,
        ),
      );
      return;
    }

    const updated = await this.api.editMessage(channel.id, messageId, body.trim());
    this.applyEdit({
      id: updated.id,
      channelId: updated.channelId,
      body: updated.body,
      editedAt: updated.editedAt ?? new Date().toISOString(),
      seq: updated.seq,
    });
  }

  async remove(messageId: string): Promise<void> {
    const channel = this.channels.activeChannel();
    if (!channel) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.applyDelete({
        id: messageId,
        channelId: channel.id,
        deletedAt: new Date().toISOString(),
      });
      return;
    }

    await this.api.deleteMessage(channel.id, messageId);
    this.applyDelete({
      id: messageId,
      channelId: channel.id,
      deletedAt: new Date().toISOString(),
    });
  }

  async toggleReaction(messageId: string, emoji: string): Promise<void> {
    const channel = this.channels.activeChannel();
    if (!channel || !emoji) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.messagesSignal.update((list) =>
        list.map((m) => {
          if (m.id !== messageId) return m;
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
        }),
      );
      return;
    }

    const result = await this.api.toggleReaction(channel.id, messageId, emoji);
    this.applyReactions(result.messageId, result.reactions);
  }

  /** Merge hub/HTTP messages into the local timeline (also used after forward fan-out). */
  ingestRemote(message: ChatMessage): void {
    const normalized = this.normalize(message);
    const isThreadReply =
      !!normalized.threadId &&
      normalized.conversationId !== normalized.channelId &&
      idsEqual(normalized.conversationId, normalized.threadId);

    if (isThreadReply) {
      this.messagesSignal.update((list) =>
        bumpChannelParentForThreadReply(list, {
          threadId: normalized.threadId!,
          parentMessageId: normalized.parentMessageId,
          replyToMessageId: normalized.replyToMessageId,
        }),
      );
      this.threads.bumpReplyCount(normalized.threadId!);
      return;
    }

    if (hasSeqGap(this.messagesSignal(), normalized.channelId, normalized.seq)) {
      void this.gapFillChannel(normalized.channelId);
    }

    const mine = normalized.authorUserId === this.auth.profile()?.id;
    const remote: ChatMessage = { ...normalized, mine, status: 'persisted' };
    const existing = findMessageByCorrelators(this.messagesSignal(), remote);
    const isActive = idsEqual(normalized.channelId, this.channels.activeChannelId());

    if (existing) {
      // Optimistic / HTTP ack already present — merge, never append a second bubble.
      if (existing.clientMessageId) {
        this.patchByClientId(existing.clientMessageId, {
          ...remote,
          clientMessageId: existing.clientMessageId,
          mine: true,
        });
      } else {
        this.messagesSignal.update((list) => upsertRemoteMessage(list, remote));
      }
      if (isActive && this.viewingLatestSignal() && (normalized.seq ?? 0) > 0) {
        this.schedulePersistReadCursor(normalized.channelId, normalized.seq!);
      }
      return;
    }

    this.messagesSignal.update((list) => upsertRemoteMessage(list, remote));

    if (isActive && this.viewingLatestSignal() && (normalized.seq ?? 0) > 0) {
      this.schedulePersistReadCursor(normalized.channelId, normalized.seq!);
      return;
    }

    if (!mine) {
      if (normalized.mentionsMe) {
        this.channels.bumpMention(normalized.channelId);
      } else {
        this.channels.bumpUnread(normalized.channelId);
      }
    }
  }

  /** BUG-002: persist read cursor so unread badges survive F5 (B-094 alívio). */
  private async persistReadCursor(channelId: string, seq: number): Promise<void> {
    if (!channelId || seq <= 0) return;
    if (this.channels.isDemo() || this.auth.isOfflineDemo()) return;
    try {
      await this.api.upsertReadCursor(channelId, seq);
    } catch {
      // best-effort; next load/ingest can retry
    }
  }

  private schedulePersistReadCursor(channelId: string, seq: number): void {
    if (!channelId || seq <= 0) return;
    if (this.channels.isDemo() || this.auth.isOfflineDemo()) return;

    const pending = this.pendingReadCursor;
    if (pending && pending.channelId === channelId) {
      this.pendingReadCursor = { channelId, seq: Math.max(pending.seq, seq) };
    } else {
      this.pendingReadCursor = { channelId, seq };
    }

    if (this.readCursorTimer) clearTimeout(this.readCursorTimer);
    this.readCursorTimer = setTimeout(() => {
      const next = this.pendingReadCursor;
      this.pendingReadCursor = null;
      this.readCursorTimer = null;
      if (next) void this.persistReadCursor(next.channelId, next.seq);
    }, 500);
  }

  markThreadOpened(messageId: string, threadId: string): void {
    this.messagesSignal.update((list) =>
      list.map((m) =>
        idsEqual(m.id, messageId) ? { ...m, threadId, replyCount: m.replyCount ?? 0 } : m,
      ),
    );
  }

  private applyEdit(patch: {
    id: string;
    channelId: string;
    body: string;
    editedAt: string;
    seq?: number;
  }): void {
    this.messagesSignal.update((list) =>
      list.map((m) =>
        m.id === patch.id
          ? {
              ...m,
              body: patch.body,
              editedAt: patch.editedAt,
              seq: patch.seq ?? m.seq,
              deletedAt: null,
            }
          : m,
      ),
    );
  }

  private applyDelete(patch: { id: string; channelId: string; deletedAt: string; seq?: number }): void {
    this.messagesSignal.update((list) => {
      const deleted = list.map((m) =>
        idsEqual(m.id, patch.id)
          ? {
              ...m,
              body: '',
              deletedAt: patch.deletedAt,
              seq: patch.seq ?? m.seq,
            }
          : m,
      );
      return markReplyQuotesDeleted(deleted, patch.id);
    });
  }

  private applyReactions(
    messageId: string,
    reactions: Array<{ emoji: string; count: number; me: boolean }>,
  ): void {
    this.messagesSignal.update((list) =>
      list.map((m) => (m.id === messageId ? { ...m, reactions: [...reactions] } : m)),
    );
  }

  private patchByClientId(clientMessageId: string, patch: Partial<ChatMessage>): void {
    this.messagesSignal.update((list) =>
      list.map((m) => (idsEqual(m.clientMessageId, clientMessageId) ? { ...m, ...patch } : m)),
    );
  }

  private normalize(message: ChatMessage): ChatMessage {
    const me = this.auth.profile()?.id;
    const mentionsMe =
      message.mentionsMe ??
      (!!me && !!message.body && message.body.includes(`<@${me}>`));
    return {
      ...message,
      channelId: message.channelId || message.conversationId,
      conversationId: message.conversationId || message.channelId,
      status: message.status ?? 'persisted',
      mine: message.mine ?? message.authorUserId === me,
      mentionsMe,
      authorName: message.authorName || 'Membro',
      body: message.deletedAt ? '' : message.body,
      replyCount: message.replyCount ?? 0,
      reactions: message.reactions ?? [],
      replyTo: message.replyTo ?? null,
    };
  }

  private demoMessages(channelId: string): ChatMessage[] {
    const now = Date.now();
    return [
      {
        id: `${channelId}-1`,
        conversationId: channelId,
        channelId,
        authorUserId: 'u-alice',
        authorName: 'Alice Mendes',
        body: 'Maré baixa no outbox — boa janela para deploy.',
        createdAt: new Date(now - 1000 * 60 * 42).toISOString(),
        status: 'persisted',
        mine: false,
        seq: 1,
      },
      {
        id: `${channelId}-2`,
        conversationId: channelId,
        channelId,
        authorUserId: 'u-bob',
        authorName: 'Bob Costa',
        body: 'SignalR reconectou limpo. Mantemos o banner só em reconnecting.',
        createdAt: new Date(now - 1000 * 60 * 18).toISOString(),
        status: 'persisted',
        mine: false,
        seq: 2,
      },
    ];
  }
}
