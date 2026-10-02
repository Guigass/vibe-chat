import { WritableSignal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { ThreadStore } from './thread.store';
import { PushNotificationService } from './push-notification.service';
import { editLifecycle, messagingPolicyOf } from '../../shared/messaging/messaging-policy';
import { ChatMessage, PollSummary } from '../../shared/models/chat.models';
import { idsEqual } from './message-sync';
import {
  ChannelPaginationState,
  MessageScrollRequest,
  applyAnnouncementEventToMessages,
  applyLinkPreviewToMessages,
  applyMessageDelete,
  applyMessageEdit,
  applyMessageReactions,
  applyRemotePollToMessages,
  applyThumbnailToMessages,
  clearLinkPreview,
  normalizeChannelMessage,
  patchPaginationMap,
} from './message-patches';
import { ReadCursorScheduler } from './message-read-cursor';
import { ingestChannelMessage } from './message-ingest';
import { MessageTimeline, createMessageTimeline } from './message-timeline';
import { MessageCommandDeps } from './message-commands';

export interface MessageRuntimeHost {
  api: ApiService;
  hub: ChatHubService;
  auth: AuthService;
  channels: ChannelStore;
  threads: ThreadStore;
  push: PushNotificationService | null;
  messages: WritableSignal<ChatMessage[]>;
  loading: WritableSignal<boolean>;
  sending: WritableSignal<boolean>;
  replyTarget: WritableSignal<ChatMessage | null>;
  editing: WritableSignal<ChatMessage | null>;
  highlightId: WritableSignal<string | null>;
  scrollRequest: WritableSignal<MessageScrollRequest | null>;
  pagination: WritableSignal<Record<string, ChannelPaginationState>>;
  viewingLatest: WritableSignal<boolean>;
  setViewingLatest: (value: boolean) => void;
  cancelScroll: (requestId?: number) => void;
}

/** Hub ingest, read cursor, timeline and command wiring for MessageStore. */
export class MessageRuntime {
  readonly timeline: MessageTimeline;
  private scrollRequestVersion = 0;
  private highlightTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly readCursor: ReadCursorScheduler;

  constructor(private readonly host: MessageRuntimeHost) {
    this.readCursor = new ReadCursorScheduler((channelId, seq) => this.persistReadCursor(channelId, seq));
    this.timeline = createMessageTimeline(() => this.timelineDeps());
  }

  bind(): void {
    const { hub } = this.host;
    hub.onMessage((message) => this.ingest(message));
    hub.onMessageEdited((patch) => this.applyEdit(patch));
    hub.onMessageDeleted((patch) => this.applyDelete(patch));
    hub.onReactionChanged((event) => this.applyReactions(event.messageId, event.reactions));
    hub.onAttachmentThumbnailReady((event) => this.applyThumbnailReady(event));
    hub.onLinkPreviewReady((event) => this.applyLinkPreviewReady(event));
    hub.onPollChanged((event) => this.applyRemotePoll(event.messageId, event.poll));
    hub.onAnnouncementAcknowledged((event) => this.applyAnnouncementEvent(event));
    hub.onReconnected(() => {
      void this.timeline.gapFillActive();
      void this.host.threads.gapFillActive();
    });
    if (typeof window !== 'undefined') {
      window.addEventListener('blur', () => this.readCursor.flushPending());
      window.addEventListener('pagehide', () => this.readCursor.flushPending());
    }
  }

  allowsEdit(message: ChatMessage): boolean {
    const policy = messagingPolicyOf(this.host.channels);
    const role = this.host.channels.activeWorkspace?.()?.role;
    return (
      editLifecycle({
        policy,
        role,
        mine: true,
        createdAt: message.createdAt,
        nowMs: Date.now(),
      }) === 'allow'
    );
  }

  requestScroll(channelId: string, messageId: string): number {
    const requestId = ++this.scrollRequestVersion;
    this.host.scrollRequest.set({ requestId, channelId, messageId });
    return requestId;
  }

  highlight(messageId: string): void {
    if (this.highlightTimer) clearTimeout(this.highlightTimer);
    this.host.highlightId.set(messageId);
    this.highlightTimer = setTimeout(() => {
      this.host.highlightId.set(null);
      this.highlightTimer = null;
    }, 2000);
  }

  cancelPendingRead(): void {
    this.readCursor.cancelPending();
  }

  persistRead(channelId: string, seq: number): Promise<void> {
    return this.persistReadCursor(channelId, seq);
  }

  scheduleRead(channelId: string, seq: number): void {
    this.readCursor.schedule(
      channelId,
      seq,
      !this.host.channels.isDemo() && !this.host.auth.isOfflineDemo(),
    );
  }

  ingest(message: ChatMessage): void {
    ingestChannelMessage(
      {
        messages: () => this.host.messages(),
        updateMessages: (fn) => this.host.messages.update(fn),
        normalize: (item) => this.normalize(item),
        profileId: () => this.host.auth.profile()?.id,
        activeChannelId: () => this.host.channels.activeChannelId(),
        viewingLatest: () => this.host.viewingLatest(),
        scheduleReadCursor: (channelId, seq) => this.scheduleRead(channelId, seq),
        gapFillChannel: (channelId) => this.timeline.gapFillChannel(channelId),
        bumpMention: (channelId) => this.host.channels.bumpMention(channelId),
        bumpUnread: (channelId) => this.host.channels.bumpUnread(channelId),
        bumpThreadReply: (threadId) => this.host.threads.bumpReplyCount(threadId),
      },
      message,
    );
  }

  commands(): MessageCommandDeps {
    return {
      api: this.host.api,
      isDemo: () => this.host.channels.isDemo(),
      isOfflineDemo: () => this.host.auth.isOfflineDemo(),
      activeChannel: () => this.host.channels.activeChannel(),
      profile: () => this.host.auth.profile(),
      messages: () => this.host.messages(),
      updateMessages: (fn) => this.host.messages.update(fn),
      replyTarget: () => this.host.replyTarget(),
      setReplyTarget: (message) => this.host.replyTarget.set(message),
      setEditing: (message) => this.host.editing.set(message),
      setSending: (value) => this.host.sending.set(value),
      normalize: (message) => this.normalize(message),
      ingestRemote: (message) => this.ingest(message),
      applyEdit: (patch) => this.applyEdit(patch),
      applyDelete: (patch) => this.applyDelete(patch),
      applyReactions: (messageId, reactions) => this.applyReactions(messageId, reactions),
      clearLinkPreview: (messageId) => this.applyLinkPreviewCleared(messageId),
      clearThreadLinkPreview: (messageId) => this.host.threads.applyLinkPreviewCleared(messageId),
      recordSuccessfulSend: () => this.host.push?.recordSuccessfulSend(),
      forgetPendingAnnouncement: (messageId) => this.host.channels.forgetPendingAnnouncement(messageId),
      refreshPendingAnnouncements: () => void this.host.channels.refreshPendingAnnouncements(),
    };
  }

  private async persistReadCursor(channelId: string, seq: number): Promise<void> {
    if (!channelId || seq <= 0) return;
    if (this.host.channels.isDemo() || this.host.auth.isOfflineDemo()) return;
    try {
      await this.host.api.upsertReadCursor(channelId, seq);
      await this.host.channels.syncChannelUnread(channelId);
    } catch {
      // best-effort; next load/ingest can retry
    }
  }

  private applyEdit(patch: {
    id: string;
    channelId: string;
    body: string;
    editedAt: string;
    seq?: number;
  }): void {
    this.host.messages.update((list) => applyMessageEdit(list, patch));
  }

  private applyDelete(patch: {
    id: string;
    channelId: string;
    deletedAt: string;
    seq?: number;
  }): void {
    this.host.messages.update((list) => applyMessageDelete(list, patch));
  }

  private applyReactions(
    messageId: string,
    reactions: Array<{ emoji: string; count: number; me: boolean }>,
  ): void {
    this.host.messages.update((list) => applyMessageReactions(list, messageId, reactions));
  }

  private applyThumbnailReady(event: {
    attachmentId: string;
    channelId: string;
    thumbnailStatus: string | null;
    width?: number | null;
    height?: number | null;
    pageCount?: number | null;
  }): void {
    this.host.messages.update((list) => applyThumbnailToMessages(list, event));
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
    this.host.messages.update((list) => applyLinkPreviewToMessages(list, event));
  }

  private applyLinkPreviewCleared(messageId: string): void {
    this.host.messages.update((list) => clearLinkPreview(list, messageId));
  }

  private applyRemotePoll(messageId: string, incoming: PollSummary): void {
    this.host.messages.update((list) => applyRemotePollToMessages(list, messageId, incoming));
  }

  private applyAnnouncementEvent(event: {
    messageId: string;
    acknowledgementCount?: number;
    acknowledgedByUserId?: string | null;
    closedAt?: string | null;
  }): void {
    const me = this.host.auth.profile()?.id;
    this.host.messages.update((list) => applyAnnouncementEventToMessages(list, event, me));
    if (me && event.acknowledgedByUserId && idsEqual(event.acknowledgedByUserId, me)) {
      this.host.channels.forgetPendingAnnouncement(event.messageId);
    }
  }

  private normalize(message: ChatMessage): ChatMessage {
    return normalizeChannelMessage(message, this.host.auth.profile()?.id);
  }

  private timelineDeps() {
    return {
      isDemo: () => this.host.channels.isDemo(),
      isOfflineDemo: () => this.host.auth.isOfflineDemo(),
      activeChannelId: () => this.host.channels.activeChannelId(),
      activeChannel: () => this.host.channels.activeChannel(),
      messages: () => this.host.messages(),
      updateMessages: (fn: (list: ChatMessage[]) => ChatMessage[]) => this.host.messages.update(fn),
      setMessages: (list: ChatMessage[]) => this.host.messages.set(list),
      pagination: () => this.host.pagination(),
      patchPagination: (channelId: string, patch: Partial<ChannelPaginationState>) =>
        this.host.pagination.update((map) => patchPaginationMap(map, channelId, patch)),
      setLoading: (value: boolean) => this.host.loading.set(value),
      joinChannel: (channelId: string) => this.host.hub.joinChannel(channelId),
      getMessages: (
        channelId: string,
        options?: { take?: number; after?: number; before?: number; around?: number },
      ) => this.host.api.getMessages(channelId, options),
      normalize: (message: ChatMessage) => this.normalize(message),
      requestScroll: (channelId: string, messageId: string) => this.requestScroll(channelId, messageId),
      cancelScroll: (requestId?: number) => this.host.cancelScroll(requestId),
      highlight: (messageId: string) => this.highlight(messageId),
      setViewingLatest: (value: boolean) => this.host.setViewingLatest(value),
    };
  }
}
