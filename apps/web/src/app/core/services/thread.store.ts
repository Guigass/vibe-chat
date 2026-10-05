import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { editLifecycle, messagingPolicyOf } from '../../shared/messaging/messaging-policy';
import { ChatMessage, ChatThread } from '../../shared/models/chat.models';
import { compareMessagesBySeq, gapFillAfterSeq, idsEqual, mergeMessagesById } from './message-sync';
import {
  ThreadPatchState,
  applyThreadDelete,
  applyThreadEdit,
  applyThreadLinkPreview,
  applyThreadReactions,
  applyThreadThumbnail,
  clearThreadLinkPreview,
  demoThread,
  ingestThreadMessage,
  normalizeThreadMessage,
  toggleThreadReactionLocal,
} from './thread-patches';
import { sendThreadMessage } from './thread-send';

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
  readonly sortedMessages = computed(() => [...this.messagesSignal()].sort(compareMessagesBySeq));

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
        const demo = demoThread(channelId, messageId, this.auth.profile()?.id ?? 'me');
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

  /** B-102 — open a thread the caller already knows the id of (from "Threads seguidas"). */
  async openById(threadId: string): Promise<void> {
    this.openSignal.set(true);
    this.loadingSignal.set(true);
    this.replyTargetSignal.set(null);
    this.editingMessageSignal.set(null);
    try {
      const [detailed, replies] = await Promise.all([
        this.api.getThread(threadId),
        this.api.getThreadMessages(threadId),
      ]);
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
      message.status !== 'persisted' ||
      editLifecycle({
        policy: messagingPolicyOf(this.channels),
        role: this.channels.activeWorkspace?.()?.role,
        mine: true,
        createdAt: message.createdAt,
        nowMs: Date.now(),
      }) !== 'allow'
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
    const candidates = [...(parent ? [parent] : []), ...this.sortedMessages()];
    for (let i = candidates.length - 1; i >= 0; i--) {
      const m = candidates[i];
      if (m.mine && !m.deletedAt && m.status === 'persisted') return m;
    }
    return null;
  }

  edit(messageId: string, body: string): Promise<void> {
    return this.editMessage(messageId, body);
  }

  bumpReplyCount(threadId: string): void {
    const active = this.activeSignal();
    if (active && idsEqual(active.id, threadId)) {
      this.activeSignal.set({ ...active, replyCount: (active.replyCount ?? 0) + 1 });
    }
  }

  /** B-102 — reflects a follow/unfollow (manual or auto) in the open thread panel, if it's this thread. */
  setFollowing(threadId: string, following: boolean, source: ChatThread['followSource'] = null): void {
    const active = this.activeSignal();
    if (active && idsEqual(active.id, threadId)) {
      this.activeSignal.set({ ...active, following, followSource: following ? source : null });
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

  toggleReaction(messageId: string, emoji: string): Promise<void> {
    return this.toggleThreadReaction(messageId, emoji);
  }

  send(body: string): Promise<boolean> {
    return sendThreadMessage(
      {
        api: this.api,
        isDemo: () => this.channels.isDemo(),
        isOfflineDemo: () => this.auth.isOfflineDemo(),
        profile: () => this.auth.profile(),
        state: this.patchState(),
        replyTarget: () => this.replyTargetSignal(),
        setReplyTarget: (message) => this.replyTargetSignal.set(message),
        setSending: (value) => this.sendingSignal.set(value),
        bumpReplyCount: (threadId) => this.bumpReplyCount(threadId),
      },
      body,
    );
  }

  applyLinkPreviewCleared(messageId: string): void {
    clearThreadLinkPreview(this.patchState(), messageId);
  }

  private async editMessage(messageId: string, body: string): Promise<void> {
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

  private async toggleThreadReaction(messageId: string, emoji: string): Promise<void> {
    const thread = this.activeSignal();
    if (!thread || !emoji) return;

    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      toggleThreadReactionLocal(this.patchState(), messageId, emoji);
      return;
    }

    const result = await this.api.toggleReaction(thread.channelId, messageId, emoji);
    this.applyReactions(result.messageId, result.reactions);
  }

  private ingestRemote(message: ChatMessage): void {
    ingestThreadMessage(this.patchState(), message);
  }

  private applyDelete(messageId: string): void {
    applyThreadDelete(this.patchState(), messageId);
  }

  private applyEdit(patch: {
    id: string;
    channelId: string;
    body: string;
    editedAt: string;
    seq?: number;
  }): void {
    applyThreadEdit(this.patchState(), patch);
  }

  private applyReactions(
    messageId: string,
    reactions: Array<{ emoji: string; count: number; me: boolean }>,
  ): void {
    applyThreadReactions(this.patchState(), messageId, reactions);
  }

  private applyThumbnailReady(event: {
    attachmentId: string;
    channelId: string;
    thumbnailStatus: string | null;
    width?: number | null;
    height?: number | null;
    pageCount?: number | null;
  }): void {
    applyThreadThumbnail(this.patchState(), event);
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
    applyThreadLinkPreview(this.patchState(), event);
  }

  private normalize(message: ChatMessage): ChatMessage {
    return normalizeThreadMessage(message, this.auth.profile()?.id);
  }

  private patchState(): ThreadPatchState {
    return {
      messages: this.messagesSignal,
      active: this.activeSignal,
      editing: this.editingMessageSignal,
      profileId: () => this.auth.profile()?.id,
    };
  }
}
