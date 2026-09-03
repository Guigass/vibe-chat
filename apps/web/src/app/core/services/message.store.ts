import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ChatHubService } from './chat-hub.service';
import { AuthService } from '../auth/auth.service';
import { ChannelStore } from './channel.store';
import { ThreadStore } from './thread.store';
import { PushNotificationService } from './push-notification.service';
import { ChatMessage, PollSummary } from '../../shared/models/chat.models';
import {
  bumpChannelParentForThreadReply,
  findMessageByCorrelators,
  gapFillAfterSeq,
  hasSeqGap,
  idsEqual,
  markReplyQuotesDeleted,
  maxSeqForChannel,
  mergeMessagesById,
  minSeqForChannel,
  replyPreviewText,
  upsertRemoteMessage,
} from './message-sync';

export interface ChannelPaginationState {
  hasMoreBefore: boolean;
  hasMoreAfter: boolean;
  loadingOlder: boolean;
  loadError: string | null;
}

export interface MessageScrollRequest {
  requestId: number;
  channelId: string;
  messageId: string;
}

const defaultPagination = (): ChannelPaginationState => ({
  hasMoreBefore: false,
  hasMoreAfter: false,
  loadingOlder: false,
  loadError: null,
});

@Injectable({ providedIn: 'root' })
export class MessageStore {
  private readonly api = inject(ApiService);
  private readonly hub = inject(ChatHubService);
  private readonly auth = inject(AuthService);
  private readonly channels = inject(ChannelStore);
  private readonly threads = inject(ThreadStore);
  private readonly push = inject(PushNotificationService, { optional: true });

  private readonly messagesSignal = signal<ChatMessage[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly sendingSignal = signal(false);
  private readonly replyTargetSignal = signal<ChatMessage | null>(null);
  private readonly editingMessageSignal = signal<ChatMessage | null>(null);
  private readonly highlightMessageIdSignal = signal<string | null>(null);
  private readonly scrollRequestSignal = signal<MessageScrollRequest | null>(null);
  private readonly paginationSignal = signal<Record<string, ChannelPaginationState>>({});
  private scrollRequestVersion = 0;
  private highlightTimer: ReturnType<typeof setTimeout> | null = null;
  private gapFillInFlight = new Set<string>();
  private readCursorTimer: ReturnType<typeof setTimeout> | null = null;
  private pendingReadCursor: { channelId: string; seq: number } | null = null;
  private unsubCreated: (() => void) | null = null;
  private unsubEdited: (() => void) | null = null;
  private unsubDeleted: (() => void) | null = null;
  private unsubReactions: (() => void) | null = null;
  private unsubThumbnail: (() => void) | null = null;
  private unsubLinkPreview: (() => void) | null = null;
  private unsubPoll: (() => void) | null = null;
  private unsubReconnected: (() => void) | null = null;

  readonly messages = this.messagesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly sending = this.sendingSignal.asReadonly();
  readonly replyTarget = this.replyTargetSignal.asReadonly();
  readonly editingMessage = this.editingMessageSignal.asReadonly();
  readonly highlightMessageId = this.highlightMessageIdSignal.asReadonly();
  readonly scrollRequest = this.scrollRequestSignal.asReadonly();
  readonly paginationForActive = computed(() => {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return defaultPagination();
    return this.paginationSignal()[channelId] ?? defaultPagination();
  });
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

  /** B-099: Shift+Esc marks the open channel read at the latest known seq. */
  async markActiveChannelRead(): Promise<void> {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    this.channels.patchChannel(channelId, { unreadCount: 0, mentionCount: 0 });
    const maxSeq = maxSeqForChannel(this.messagesSignal(), channelId);
    if (maxSeq <= 0) return;
    if (this.readCursorTimer) {
      clearTimeout(this.readCursorTimer);
      this.readCursorTimer = null;
    }
    this.pendingReadCursor = null;
    await this.persistReadCursor(channelId, maxSeq);
  }

  constructor() {
    this.unsubCreated = this.hub.onMessage((message) => this.ingestRemote(message));
    this.unsubEdited = this.hub.onMessageEdited((patch) => this.applyEdit(patch));
    this.unsubDeleted = this.hub.onMessageDeleted((patch) => this.applyDelete(patch));
    this.unsubReactions = this.hub.onReactionChanged((event) =>
      this.applyReactions(event.messageId, event.reactions),
    );
    this.unsubThumbnail = this.hub.onAttachmentThumbnailReady((event) =>
      this.applyThumbnailReady(event),
    );
    this.unsubLinkPreview = this.hub.onLinkPreviewReady((event) =>
      this.applyLinkPreviewReady(event),
    );
    this.unsubPoll = this.hub.onPollChanged((event) => this.applyPoll(event.messageId, event.poll));
    this.unsubReconnected = this.hub.onReconnected(() => {
      void this.gapFillActiveChannel();
      void this.threads.gapFillActive();
    });
    if (typeof window !== 'undefined') {
      window.addEventListener('blur', this.flushPendingReadCursor);
      window.addEventListener('pagehide', this.flushPendingReadCursor);
    }
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

  /** Enter composer edit mode (B-173). Mutual exclusion with reply cite. */
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

  /** Last own persisted message in the active channel timeline (for ↑ shortcut). */
  lastOwnPersistedMessage(): ChatMessage | null {
    const list = this.forActiveChannel();
    for (let i = list.length - 1; i >= 0; i--) {
      const m = list[i];
      if (m.mine && !m.deletedAt && m.status === 'persisted') return m;
    }
    return null;
  }

  jumpToMessage(messageId: string): void {
    if (!messageId) return;
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    this.requestMessageScroll(channelId, messageId);
    this.highlightMessage(messageId);
  }

  acknowledgeScrollRequest(requestId: number): void {
    if (this.scrollRequestSignal()?.requestId === requestId) {
      this.scrollRequestSignal.set(null);
    }
  }

  cancelScrollRequest(requestId?: number): void {
    if (requestId === undefined || this.scrollRequestSignal()?.requestId === requestId) {
      this.scrollRequestSignal.set(null);
    }
  }

  private requestMessageScroll(channelId: string, messageId: string): number {
    const requestId = ++this.scrollRequestVersion;
    this.scrollRequestSignal.set({ requestId, channelId, messageId });
    return requestId;
  }

  private highlightMessage(messageId: string): void {
    if (this.highlightTimer) clearTimeout(this.highlightTimer);
    this.highlightMessageIdSignal.set(messageId);
    this.highlightTimer = setTimeout(() => {
      this.highlightMessageIdSignal.set(null);
      this.highlightTimer = null;
    }, 2000);
  }

  async ensureMessageVisible(messageId: string): Promise<'ok' | 'missing'> {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return 'missing';
    const existing = this.messagesSignal().find((m) => idsEqual(m.id, messageId));
    if (existing) {
      this.jumpToMessage(messageId);
      return 'ok';
    }
    return 'missing';
  }

  async jumpToSequence(
    channelId: string,
    seq: number,
    messageId: string,
  ): Promise<'ok' | 'deleted' | 'missing'> {
    if (!channelId || seq <= 0 || !messageId) return 'missing';
    let requestId = this.requestMessageScroll(channelId, messageId);
    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.highlightMessage(messageId);
      return 'ok';
    }

    this.loadingSignal.set(true);
    try {
      await this.hub.joinChannel(channelId);
      const page = await this.api.getMessages(channelId, { around: seq, take: 50 });
      const target =
        page.messages.find((m) => idsEqual(m.id, messageId)) ??
        page.messages.find((m) => m.seq === seq);
      if (!target) {
        this.cancelScrollRequest(requestId);
        return 'missing';
      }
      if (target.deletedAt) {
        this.cancelScrollRequest(requestId);
        return 'deleted';
      }

      if (!idsEqual(target.id, messageId)) {
        this.cancelScrollRequest(requestId);
        requestId = this.requestMessageScroll(channelId, target.id);
      }

      this.applyChannelPage(channelId, page, { replace: true });
      this.highlightMessage(target.id);
      this.setViewingLatest(false);
      return 'ok';
    } catch {
      this.cancelScrollRequest(requestId);
      return 'missing';
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async loadOlderMessages(channelId?: string): Promise<boolean> {
    const id = channelId ?? this.channels.activeChannelId();
    if (!id || this.channels.isDemo() || this.auth.isOfflineDemo()) return false;

    const state = this.paginationSignal()[id] ?? defaultPagination();
    if (!state.hasMoreBefore || state.loadingOlder) return false;

    const minSeq = minSeqForChannel(this.messagesSignal(), id);
    if (minSeq <= 0) return false;

    this.patchPagination(id, { loadingOlder: true, loadError: null });
    try {
      const page = await this.api.getMessages(id, { before: minSeq, take: 50 });
      if (!page.messages.length) {
        this.patchPagination(id, { hasMoreBefore: false, loadingOlder: false });
        return false;
      }
      this.applyChannelPage(id, page, { prepend: true });
      return true;
    } catch {
      this.patchPagination(id, { loadingOlder: false, loadError: 'retry' });
      return false;
    }
  }

  retryLoadOlder(): void {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    this.patchPagination(channelId, { loadError: null });
    void this.loadOlderMessages(channelId);
  }

  async loadChannel(channelId: string): Promise<void> {
    const hasCached = this.messagesSignal().some((message) => idsEqual(message.channelId, channelId));
    // Keep a cached timeline mounted so channel switches do not destroy
    // `.timeline__list` and drop the sticky-bottom pin (OPS-E2E-B097).
    if (!hasCached) {
      this.loadingSignal.set(true);
    }
    try {
      await this.hub.joinChannel(channelId);
      if (this.channels.isDemo()) {
        this.messagesSignal.set(this.demoMessages(channelId));
        this.patchPagination(channelId, {
          hasMoreBefore: false,
          hasMoreAfter: false,
          loadError: null,
        });
      } else {
        const page = await this.api.getMessages(channelId, { take: 50 });
        this.applyChannelPage(channelId, page, { replace: true });
      }
    } catch {
      this.messagesSignal.update((current) => {
        return current.filter((message) => !idsEqual(message.channelId, channelId));
      });
      this.patchPagination(channelId, { ...defaultPagination(), loadError: 'retry' });
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
      const page = await this.api.getMessages(channelId, { after, take: 100 });
      if (!page.messages.length) return;
      this.messagesSignal.update((current) =>
        mergeMessagesById(
          current,
          page.messages.map((m) => this.normalize(m)),
        ),
      );
      this.patchPagination(channelId, {
        hasMoreAfter: page.hasMoreAfter,
      });
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
      this.push?.recordSuccessfulSend();
      return true;
    } catch {
      this.patchByClientId(clientMessageId, { status: 'failed' });
      if (replyTarget) this.replyTargetSignal.set(replyTarget);
      return false;
    } finally {
      this.sendingSignal.set(false);
    }
  }

  async createPoll(input: {
    question: string;
    options: string[];
    allowMultiple: boolean;
    anonymous: boolean;
    closesAt?: string | null;
  }): Promise<boolean> {
    const channel = this.channels.activeChannel();
    if (!channel || channel.isDirect) return false;
    if (this.channels.isDemo() || this.auth.isOfflineDemo()) return false;

    this.sendingSignal.set(true);
    try {
      const persisted = await this.api.createPoll({
        channelId: channel.id,
        clientMessageId: crypto.randomUUID(),
        idempotencyKey: crypto.randomUUID(),
        question: input.question,
        options: input.options,
        allowMultiple: input.allowMultiple,
        anonymous: input.anonymous,
        closesAt: input.closesAt,
      });
      this.ingestRemote(this.normalize(persisted));
      return true;
    } catch {
      return false;
    } finally {
      this.sendingSignal.set(false);
    }
  }

  async votePoll(poll: PollSummary, optionId: string): Promise<void> {
    if (!poll.canVote || poll.closedAt) return;
    const nextIds = poll.allowMultiple
      ? poll.options.some((option) => option.id === optionId && option.votedByMe)
        ? poll.options.filter((option) => option.votedByMe && option.id !== optionId).map((option) => option.id)
        : [...poll.options.filter((option) => option.votedByMe).map((option) => option.id), optionId]
      : [optionId];

    try {
      const updated =
        poll.allowMultiple && nextIds.length === 0
          ? await this.api.unvotePoll(poll.id)
          : await this.api.votePoll(poll.id, nextIds);
      this.applyPoll(poll.messageId, updated);
    } catch {
      /* keep current card */
    }
  }

  async closePoll(pollId: string, messageId: string): Promise<void> {
    try {
      const updated = await this.api.closePoll(pollId);
      this.applyPoll(messageId, updated);
    } catch {
      /* keep current card */
    }
  }

  private applyPoll(messageId: string, poll: PollSummary): void {
    this.messagesSignal.update((list) =>
      list.map((message) =>
        idsEqual(message.id, messageId) || idsEqual(message.id, poll.messageId)
          ? { ...message, poll }
          : message,
      ),
    );
  }

  async edit(messageId: string, body: string): Promise<void> {
    const channel = this.channels.activeChannel();
    if (!channel || !body.trim()) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.messagesSignal.update((list) =>
        list.map((m) =>
          m.id === messageId ? { ...m, body: body.trim(), editedAt: new Date().toISOString() } : m,
        ),
      );
      this.editingMessageSignal.set(null);
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
    this.editingMessageSignal.set(null);
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

  async removeLinkPreview(messageId: string): Promise<void> {
    const channel = this.channels.activeChannel();
    const existing = this.messagesSignal().find((m) => idsEqual(m.id, messageId));
    const channelId = existing?.channelId ?? channel?.id;
    if (!channelId || !messageId) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.applyLinkPreviewCleared(messageId);
      this.threads.applyLinkPreviewCleared(messageId);
      return;
    }

    await this.api.deleteMessageLinkPreview(channelId, messageId);
    this.applyLinkPreviewCleared(messageId);
    this.threads.applyLinkPreviewCleared(messageId);
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

  /** B-094: persist read cursor when the user reaches the latest visible message. */
  private async persistReadCursor(channelId: string, seq: number): Promise<void> {
    if (!channelId || seq <= 0) return;
    if (this.channels.isDemo() || this.auth.isOfflineDemo()) return;
    try {
      await this.api.upsertReadCursor(channelId, seq);
      await this.channels.syncChannelUnread(channelId);
    } catch {
      // best-effort; next load/ingest can retry
    }
  }

  private readonly flushPendingReadCursor = (): void => {
    const pending = this.pendingReadCursor;
    if (!pending) return;
    if (this.readCursorTimer) {
      clearTimeout(this.readCursorTimer);
      this.readCursorTimer = null;
    }
    this.pendingReadCursor = null;
    void this.persistReadCursor(pending.channelId, pending.seq);
  };

  private schedulePersistReadCursor(channelId: string, seq: number): void {
    if (!channelId || seq <= 0) return;
    if (this.channels.isDemo() || this.auth.isOfflineDemo()) return;
    if (typeof document !== 'undefined' && !document.hasFocus()) return;

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
    }, 1000);
  }

  async markMessageUnread(message: ChatMessage): Promise<void> {
    const seq = message.seq ?? 0;
    if (!message.channelId || seq <= 0) return;
    if (this.channels.isDemo() || this.auth.isOfflineDemo()) return;
    const targetSeq = Math.max(0, seq - 1);
    try {
      await this.api.upsertReadCursor(message.channelId, targetSeq, {
        allowRetrograde: true,
      });
      await this.channels.syncChannelUnread(message.channelId);
    } catch {
      // best-effort
    }
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

  private applyDelete(patch: {
    id: string;
    channelId: string;
    deletedAt: string;
    seq?: number;
  }): void {
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

  private applyThumbnailReady(event: {
    attachmentId: string;
    channelId: string;
    thumbnailStatus: string | null;
    width?: number | null;
    height?: number | null;
    pageCount?: number | null;
  }): void {
    this.messagesSignal.update((list) =>
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
      }),
    );
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
    this.messagesSignal.update((list) =>
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
      }),
    );
  }

  private applyLinkPreviewCleared(messageId: string): void {
    this.messagesSignal.update((list) =>
      list.map((m) => (idsEqual(m.id, messageId) ? { ...m, linkPreview: null } : m)),
    );
  }

  private patchByClientId(clientMessageId: string, patch: Partial<ChatMessage>): void {
    this.messagesSignal.update((list) =>
      list.map((m) => (idsEqual(m.clientMessageId, clientMessageId) ? { ...m, ...patch } : m)),
    );
  }

  private applyChannelPage(
    channelId: string,
    page: { messages: ChatMessage[]; hasMoreBefore: boolean; hasMoreAfter: boolean },
    mode: { replace?: boolean; prepend?: boolean } = {},
  ): void {
    const normalized = page.messages.map((m) => this.normalize(m));
    this.messagesSignal.update((current) => {
      const others = current.filter((m) => m.channelId !== channelId);
      if (mode.replace) {
        return [...others, ...normalized];
      }
      const existing = current.filter((m) => m.channelId === channelId);
      const merged = mergeMessagesById(existing, normalized);
      return [...others, ...merged];
    });
    this.patchPagination(channelId, {
      hasMoreBefore: page.hasMoreBefore,
      hasMoreAfter: page.hasMoreAfter,
      loadingOlder: false,
      loadError: null,
    });
  }

  private patchPagination(channelId: string, patch: Partial<ChannelPaginationState>): void {
    this.paginationSignal.update((map) => ({
      ...map,
      [channelId]: { ...(map[channelId] ?? defaultPagination()), ...patch },
    }));
  }

  private normalize(message: ChatMessage): ChatMessage {
    const me = this.auth.profile()?.id;
    const mentionsMe =
      message.mentionsMe ?? (!!me && !!message.body && message.body.includes(`<@${me}>`));
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
      isPinned: message.isPinned ?? false,
      isSaved: message.isSaved ?? false,
    };
  }

  setPinned(channelId: string, messageId: string, pinned: boolean): void {
    this.messagesSignal.update((current) =>
      current.map((m) =>
        m.channelId === channelId && m.id === messageId ? { ...m, isPinned: pinned } : m,
      ),
    );
  }

  applyPinnedFlags(channelId: string, messageIds: readonly string[]): void {
    const pinned = new Set(messageIds);
    this.messagesSignal.update((current) =>
      current.map((m) => (m.channelId === channelId ? { ...m, isPinned: pinned.has(m.id) } : m)),
    );
  }

  setSaved(messageId: string, saved: boolean): void {
    this.messagesSignal.update((current) =>
      current.map((m) => (m.id === messageId ? { ...m, isSaved: saved } : m)),
    );
  }

  applySavedFlags(messageIds: readonly string[]): void {
    const saved = new Set(messageIds);
    this.messagesSignal.update((current) =>
      current.map((m) => ({ ...m, isSaved: saved.has(m.id) })),
    );
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
