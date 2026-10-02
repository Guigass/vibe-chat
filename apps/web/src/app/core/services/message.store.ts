import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ChatHubService } from './chat-hub.service';
import { AuthService } from '../auth/auth.service';
import { ChannelStore } from './channel.store';
import { ThreadStore } from './thread.store';
import { PushNotificationService } from './push-notification.service';
import { ChatMessage, PollSummary } from '../../shared/models/chat.models';
import { idsEqual, maxSeqForChannel } from './message-sync';
import {
  ChannelPaginationState,
  MessageScrollRequest,
  applyPinnedIdSet,
  applySavedIdSet,
  channelTimeline,
  defaultPagination,
  markThreadOpenedOnMessages,
  setPinnedFlag,
  setSavedFlag,
} from './message-patches';
import { MessageRuntime } from './message-runtime';
import {
  acknowledgeChannelAnnouncement,
  closeChannelAnnouncement,
  closeChannelPoll,
  createChannelPoll,
  editChannelMessage,
  removeChannelLinkPreview,
  removeChannelMessage,
  sendChannelMessage,
  toggleChannelReaction,
  voteChannelPoll,
} from './message-commands';

export type { ChannelPaginationState, MessageScrollRequest } from './message-patches';

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
  private readonly viewingLatestSignal = signal(true);
  private readonly runtime = new MessageRuntime(this.runtimeHost());

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
  readonly forActiveChannel = computed(() => {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return [];
    return channelTimeline(this.messagesSignal(), channelId);
  });

  constructor() {
    this.runtime.bind();
  }

  setViewingLatest(value: boolean): void {
    this.viewingLatestSignal.set(value);
  }

  markViewedLatest(): void {
    this.viewingLatestSignal.set(true);
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    const maxSeq = maxSeqForChannel(this.messagesSignal(), channelId);
    if (maxSeq > 0) this.runtime.scheduleRead(channelId, maxSeq);
  }

  /** B-099: Shift+Esc marks the open channel read at the latest known seq. */
  async markActiveChannelRead(): Promise<void> {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    this.channels.patchChannel(channelId, { unreadCount: 0, mentionCount: 0 });
    const maxSeq = maxSeqForChannel(this.messagesSignal(), channelId);
    if (maxSeq <= 0) return;
    this.runtime.cancelPendingRead();
    await this.runtime.persistRead(channelId, maxSeq);
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
      message.status !== 'persisted' ||
      !this.runtime.allowsEdit(message)
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
    this.runtime.requestScroll(channelId, messageId);
    this.runtime.highlight(messageId);
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

  jumpToSequence(channelId: string, seq: number, messageId: string): Promise<'ok' | 'deleted' | 'missing'> {
    return this.runtime.timeline.jumpToSequence(channelId, seq, messageId);
  }

  loadOlderMessages(channelId?: string): Promise<boolean> {
    return this.runtime.timeline.loadOlderMessages(channelId);
  }

  retryLoadOlder(): void {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    this.paginationSignal.update((map) => ({
      ...map,
      [channelId]: { ...(map[channelId] ?? defaultPagination()), loadError: null },
    }));
    void this.loadOlderMessages(channelId);
  }

  loadChannel(channelId: string): Promise<void> {
    return this.runtime.timeline.loadChannel(channelId);
  }

  /** History reconcile after SignalR reconnect or detected seq gap (B-070). */
  gapFillChannel(channelId: string): Promise<void> {
    return this.runtime.timeline.gapFillChannel(channelId);
  }

  send(
    body: string,
    attachmentIds: string[] = [],
    announcement?: { requiresAcknowledgement: boolean; acknowledgeBy?: string | null },
  ): Promise<boolean> {
    return sendChannelMessage(this.runtime.commands(), body, attachmentIds, announcement);
  }

  createPoll(input: {
    question: string;
    options: string[];
    allowMultiple: boolean;
    anonymous: boolean;
    closesAt?: string | null;
  }): Promise<boolean> {
    return createChannelPoll(this.runtime.commands(), input);
  }

  votePoll(poll: PollSummary, optionId: string): Promise<void> {
    return voteChannelPoll(this.runtime.commands(), poll, optionId);
  }

  closePoll(pollId: string, messageId: string): Promise<void> {
    return closeChannelPoll(this.runtime.commands(), pollId, messageId);
  }

  acknowledgeAnnouncement(message: ChatMessage): Promise<boolean> {
    return acknowledgeChannelAnnouncement(this.runtime.commands(), message);
  }

  closeAnnouncement(message: ChatMessage): Promise<boolean> {
    return closeChannelAnnouncement(this.runtime.commands(), message);
  }

  loadAnnouncementReport(message: ChatMessage) {
    return this.api.getAnnouncementReport(message.channelId, message.id);
  }

  edit(messageId: string, body: string): Promise<void> {
    return editChannelMessage(this.runtime.commands(), messageId, body);
  }

  remove(messageId: string): Promise<void> {
    return removeChannelMessage(this.runtime.commands(), messageId);
  }

  removeLinkPreview(messageId: string): Promise<void> {
    return removeChannelLinkPreview(this.runtime.commands(), messageId);
  }

  toggleReaction(messageId: string, emoji: string): Promise<void> {
    return toggleChannelReaction(this.runtime.commands(), messageId, emoji);
  }

  /** Merge hub/HTTP messages into the local timeline (also used after forward fan-out). */
  ingestRemote(message: ChatMessage): void {
    this.runtime.ingest(message);
  }

  async markMessageUnread(message: ChatMessage): Promise<void> {
    const seq = message.seq ?? 0;
    if (!message.channelId || seq <= 0) return;
    if (this.channels.isDemo() || this.auth.isOfflineDemo()) return;
    const targetSeq = Math.max(0, seq - 1);
    try {
      await this.api.upsertReadCursor(message.channelId, targetSeq, { allowRetrograde: true });
      await this.channels.syncChannelUnread(message.channelId);
    } catch {
      // best-effort
    }
  }

  markThreadOpened(messageId: string, threadId: string): void {
    this.messagesSignal.update((list) => markThreadOpenedOnMessages(list, messageId, threadId));
  }

  setPinned(channelId: string, messageId: string, pinned: boolean): void {
    this.messagesSignal.update((current) => setPinnedFlag(current, channelId, messageId, pinned));
  }

  applyPinnedFlags(channelId: string, messageIds: readonly string[]): void {
    this.messagesSignal.update((current) => applyPinnedIdSet(current, channelId, messageIds));
  }

  setSaved(messageId: string, saved: boolean): void {
    this.messagesSignal.update((current) => setSavedFlag(current, messageId, saved));
  }

  applySavedFlags(messageIds: readonly string[]): void {
    this.messagesSignal.update((current) => applySavedIdSet(current, messageIds));
  }

  private runtimeHost() {
    return {
      api: this.api,
      hub: this.hub,
      auth: this.auth,
      channels: this.channels,
      threads: this.threads,
      push: this.push,
      messages: this.messagesSignal,
      loading: this.loadingSignal,
      sending: this.sendingSignal,
      replyTarget: this.replyTargetSignal,
      editing: this.editingMessageSignal,
      highlightId: this.highlightMessageIdSignal,
      scrollRequest: this.scrollRequestSignal,
      pagination: this.paginationSignal,
      viewingLatest: this.viewingLatestSignal,
      setViewingLatest: (value: boolean) => this.setViewingLatest(value),
      cancelScroll: (requestId?: number) => this.cancelScrollRequest(requestId),
    };
  }
}
