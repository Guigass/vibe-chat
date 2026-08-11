import { Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';
import { TenantContext } from '../tenant/tenant-context';
import {
  ChatMessage,
  PresenceStatus,
  ReactionSummary,
  TypingState,
} from '../../shared/models/chat.models';
import {
  HUB_KEEP_ALIVE_MS,
  HUB_SERVER_TIMEOUT_MS,
  nextHubRetryDelayMs,
} from './chat-hub-reconnect';
import { withoutSelfTyping } from './typing-filter';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface PresenceChangedEvent {
  userId: string;
  status: PresenceStatus;
  tenantId?: string;
}

interface MessageCreatedPayload {
  messageId?: string;
  id?: string;
  /** Echo of the client-supplied message id for optimistic UI reconciliation. */
  clientMessageId?: string;
  channelId: string;
  conversationId?: string;
  threadId?: string | null;
  parentMessageId?: string | null;
  replyToMessageId?: string | null;
  replyTo?: {
    messageId?: string;
    authorName?: string;
    preview?: string;
    deleted?: boolean;
  } | null;
  forwardedFromMessageId?: string | null;
  forwardedFromChannelId?: string | null;
  forwardedFrom?: {
    messageId?: string;
    channelId?: string;
    channelName?: string;
    authorName?: string;
    createdAt?: string;
    isDirect?: boolean;
  } | null;
  sequence?: number;
  authorId?: string;
  authorName?: string;
  body?: string;
  createdAt?: string;
  mentionedUserIds?: string[];
  mentionKinds?: string[];
  attachments?: Array<{
    id: string;
    fileName: string;
    contentType: string;
    sizeBytes: number;
  }>;
}

interface MessageEditedPayload {
  messageId?: string;
  id?: string;
  channelId: string;
  sequence?: number;
  body?: string;
  editedAt?: string;
}

interface MessageDeletedPayload {
  messageId?: string;
  id?: string;
  channelId: string;
  sequence?: number;
  deletedAt?: string;
}

export interface MessageEditEvent {
  id: string;
  channelId: string;
  body: string;
  editedAt: string;
  seq?: number;
}

export interface MessageDeleteEvent {
  id: string;
  channelId: string;
  deletedAt: string;
  seq?: number;
}

export interface ReactionChangedEvent {
  messageId: string;
  channelId: string;
  emoji: string;
  userId: string;
  added: boolean;
  reactions: ReactionSummary[];
  topUsers?: string[];
}

export interface AttachmentThumbnailReadyEvent {
  attachmentId: string;
  channelId: string;
  thumbnailStatus: string | null;
  width?: number | null;
  height?: number | null;
  pageCount?: number | null;
}

interface ReactionChangedPayload {
  messageId?: string;
  channelId: string;
  emoji?: string;
  userId?: string;
  added?: boolean;
  topUsers?: string[];
  reactions?: Array<{ emoji: string; count: number; userIds?: string[]; me?: boolean }>;
}

interface AttachmentThumbnailReadyPayload {
  attachmentId?: string;
  channelId?: string;
  thumbnailStatus?: string | null;
  width?: number | null;
  height?: number | null;
  pageCount?: number | null;
}

@Injectable({ providedIn: 'root' })
export class ChatHubService {
  private readonly auth = inject(AuthService);
  private readonly tenant = inject(TenantContext);
  private connection: HubConnection | null = null;
  /**
   * Channels this client is subscribed to (SignalR groups). Unlike a single
   * "active channel" tracker, we keep every visited channel joined so unread
   * badges can bump live for channels the user isn't currently viewing (B-088).
   */
  private readonly joinedChannelIds = new Set<string>();
  private heartbeatTimer: ReturnType<typeof setInterval> | null = null;
  private visibilityHandler: (() => void) | null = null;
  private onlineHandler: (() => void) | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private manualRetryCount = 0;
  /** True while the shell wants a live hub (until explicit disconnect). */
  private wantConnected = false;
  private connectInFlight: Promise<void> | null = null;
  private visibilityRetryHandler: (() => void) | null = null;

  private readonly statusSignal = signal<ConnectionStatus>('disconnected');
  private readonly typingSignal = signal<TypingState[]>([]);
  private readonly messageHandlers = new Set<(message: ChatMessage) => void>();
  private readonly editedHandlers = new Set<(event: MessageEditEvent) => void>();
  private readonly deletedHandlers = new Set<(event: MessageDeleteEvent) => void>();
  private readonly reactionHandlers = new Set<(event: ReactionChangedEvent) => void>();
  private readonly presenceHandlers = new Set<(event: PresenceChangedEvent) => void>();
  private readonly reconnectedHandlers = new Set<() => void | Promise<void>>();
  private readonly thumbnailReadyHandlers = new Set<(event: AttachmentThumbnailReadyEvent) => void>();

  readonly status = this.statusSignal.asReadonly();
  readonly typingUsers = this.typingSignal.asReadonly();

  async connect(): Promise<void> {
    if (this.auth.isOfflineDemo()) return;
    this.wantConnected = true;
    this.ensureNetworkListeners();

    if (this.connection?.state === HubConnectionState.Connected) return;
    if (
      this.connection?.state === HubConnectionState.Connecting ||
      this.connection?.state === HubConnectionState.Reconnecting
    ) {
      return;
    }
    if (this.connectInFlight) {
      await this.connectInFlight;
      return;
    }

    this.connectInFlight = this.startConnection();
    try {
      await this.connectInFlight;
    } finally {
      this.connectInFlight = null;
    }
  }

  private async startConnection(): Promise<void> {
    this.clearManualRetry();
    this.statusSignal.set('connecting');

    if (!this.connection) {
      this.connection = this.buildConnection();
    }

    try {
      await this.connection.start();
      this.manualRetryCount = 0;
      this.statusSignal.set('connected');
      await this.heartbeat();
      await this.rejoinAllChannels();
      this.startPresenceLoop();
    } catch {
      this.statusSignal.set('disconnected');
      this.scheduleManualRetry();
    }
  }

  private buildConnection(): HubConnection {
    const devUser = this.auth.devUser();
    const hubUrl = devUser
      ? `${environment.hubUrl}?devUser=${encodeURIComponent(devUser)}`
      : environment.hubUrl;

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: async () => (await this.auth.getAccessToken()) ?? '',
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => nextHubRetryDelayMs(ctx.previousRetryCount),
      })
      .configureLogging(environment.production ? LogLevel.Warning : LogLevel.Information)
      .build();

    connection.keepAliveIntervalInMilliseconds = HUB_KEEP_ALIVE_MS;
    connection.serverTimeoutInMilliseconds = HUB_SERVER_TIMEOUT_MS;

    connection.onreconnecting(() => this.statusSignal.set('reconnecting'));
    connection.onreconnected(async () => {
      this.manualRetryCount = 0;
      this.statusSignal.set('connected');
      try {
        await this.heartbeat();
        await this.rejoinAllChannels();
        await this.notifyReconnected();
      } catch {
        // banner already reflects connection; next user action can retry join
      }
    });
    connection.onclose(() => {
      this.stopPresenceLoop();
      this.statusSignal.set('disconnected');
      // Automatic reconnect only arms after a successful start(); cover the rest.
      if (this.wantConnected) {
        this.scheduleManualRetry();
      }
    });

    this.bindHubHandlers(connection);
    return connection;
  }

  private bindHubHandlers(connection: HubConnection): void {
    connection.on('MessageCreated', (raw: MessageCreatedPayload | string) => {
      const payload = this.coercePayload<MessageCreatedPayload>(raw);
      if (!payload) return;
      const message = this.mapPayload(payload);
      if (!message) return;
      for (const handler of this.messageHandlers) {
        handler(message);
      }
    });

    connection.on('MessageEdited', (raw: MessageEditedPayload | string) => {
      const payload = this.coercePayload<MessageEditedPayload>(raw);
      if (!payload) return;
      const id = payload.messageId ?? payload.id;
      if (!id || !payload.channelId || !payload.body) return;
      const event: MessageEditEvent = {
        id: String(id),
        channelId: String(payload.channelId),
        body: payload.body,
        editedAt: payload.editedAt ?? new Date().toISOString(),
        seq: payload.sequence,
      };
      for (const handler of this.editedHandlers) {
        handler(event);
      }
    });

    connection.on('MessageDeleted', (raw: MessageDeletedPayload | string) => {
      const payload = this.coercePayload<MessageDeletedPayload>(raw);
      if (!payload) return;
      const id = payload.messageId ?? payload.id;
      if (!id || !payload.channelId) return;
      const event: MessageDeleteEvent = {
        id: String(id),
        channelId: String(payload.channelId),
        deletedAt: payload.deletedAt ?? new Date().toISOString(),
        seq: payload.sequence,
      };
      for (const handler of this.deletedHandlers) {
        handler(event);
      }
    });

    connection.on('ReactionChanged', (raw: ReactionChangedPayload | string) => {
      const payload = this.coercePayload<ReactionChangedPayload>(raw);
      if (!payload?.messageId || !payload.channelId || !payload.emoji) return;
      const me = this.auth.profile()?.id;
      const event: ReactionChangedEvent = {
        messageId: String(payload.messageId),
        channelId: String(payload.channelId),
        emoji: payload.emoji,
        userId: String(payload.userId ?? ''),
        added: !!payload.added,
        topUsers: payload.topUsers?.map(String),
        reactions: (payload.reactions ?? []).map((r) => {
          const userIds = (r.userIds ?? []).map(String);
          return {
            emoji: r.emoji,
            count: r.count,
            me: me ? userIds.includes(me) : !!r.me,
          };
        }),
      };
      for (const handler of this.reactionHandlers) {
        handler(event);
      }
    });

    connection.on('AttachmentThumbnailReady', (raw: AttachmentThumbnailReadyPayload | string) => {
      const payload = this.coercePayload<AttachmentThumbnailReadyPayload>(raw);
      if (!payload?.attachmentId || !payload.channelId) return;
      const event: AttachmentThumbnailReadyEvent = {
        attachmentId: String(payload.attachmentId),
        channelId: String(payload.channelId),
        thumbnailStatus: payload.thumbnailStatus ?? null,
        width: payload.width ?? null,
        height: payload.height ?? null,
        pageCount: payload.pageCount ?? null,
      };
      for (const handler of this.thumbnailReadyHandlers) {
        handler(event);
      }
    });

    connection.on('Typing', (raw: {
      channelId: string;
      userId: string;
      displayName: string;
    } | string) => {
      const payload = this.coercePayload<{
        channelId: string;
        userId: string;
        displayName: string;
      }>(raw);
      if (!payload?.channelId) return;
      const typing: TypingState = {
        channelId: String(payload.channelId),
        userId: String(payload.userId),
        displayName: payload.displayName,
      };
      // Hub uses OthersInGroup; still ignore self if echo arrives (B-071).
      const me = this.auth.profile()?.id;
      if (me && typing.userId === me) return;
      this.typingSignal.update((list) => {
        const filtered = list.filter(
          (t) => !(t.channelId === typing.channelId && t.userId === typing.userId),
        );
        return withoutSelfTyping([...filtered, typing], me);
      });
      window.setTimeout(() => {
        this.typingSignal.update((list) =>
          list.filter(
            (t) => !(t.channelId === typing.channelId && t.userId === typing.userId),
          ),
        );
      }, 3000);
    });

    connection.on('PresenceChanged', (raw: {
      tenantId?: string;
      userId: string;
      status: string;
    } | string) => {
      const payload = this.coercePayload<{
        tenantId?: string;
        userId: string;
        status: string;
      }>(raw);
      if (!payload?.userId) return;
      const status = (payload.status || 'offline').toLowerCase();
      const event: PresenceChangedEvent = {
        userId: String(payload.userId),
        tenantId: payload.tenantId ? String(payload.tenantId) : undefined,
        status: status === 'online' || status === 'away' ? status : 'offline',
      };
      for (const handler of this.presenceHandlers) {
        handler(event);
      }
    });
  }

  private scheduleManualRetry(): void {
    if (!this.wantConnected || this.retryTimer) return;
    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      // wait for window 'online' listener
      return;
    }
    const delay = nextHubRetryDelayMs(this.manualRetryCount);
    this.manualRetryCount += 1;
    this.statusSignal.set('reconnecting');
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      // Drop dead connection so the next start rebuilds cleanly.
      if (this.connection?.state === HubConnectionState.Disconnected) {
        this.connection = null;
      }
      void this.connect();
    }, delay);
  }

  private clearManualRetry(): void {
    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
  }

  private ensureNetworkListeners(): void {
    if (typeof window === 'undefined') return;
    if (!this.onlineHandler) {
      this.onlineHandler = () => {
        if (this.wantConnected && this.statusSignal() !== 'connected') {
          this.manualRetryCount = 0;
          this.clearManualRetry();
          void this.connect();
        }
      };
      window.addEventListener('online', this.onlineHandler);
    }
    if (!this.visibilityRetryHandler && typeof document !== 'undefined') {
      this.visibilityRetryHandler = () => {
        if (
          document.visibilityState === 'visible' &&
          this.wantConnected &&
          this.statusSignal() !== 'connected'
        ) {
          this.manualRetryCount = 0;
          this.clearManualRetry();
          void this.connect();
        }
      };
      document.addEventListener('visibilitychange', this.visibilityRetryHandler);
    }
  }

  private removeNetworkListeners(): void {
    if (typeof window !== 'undefined' && this.onlineHandler) {
      window.removeEventListener('online', this.onlineHandler);
      this.onlineHandler = null;
    }
    if (typeof document !== 'undefined' && this.visibilityRetryHandler) {
      document.removeEventListener('visibilitychange', this.visibilityRetryHandler);
      this.visibilityRetryHandler = null;
    }
  }

  async disconnect(): Promise<void> {
    this.wantConnected = false;
    this.clearManualRetry();
    this.removeNetworkListeners();
    this.stopPresenceLoop();
    if (!this.connection) {
      this.statusSignal.set('disconnected');
      return;
    }
    await this.connection.stop();
    this.connection = null;
    this.statusSignal.set('disconnected');
  }

  /** Join (and stay joined to) a channel's SignalR group; safe to call repeatedly. */
  async joinChannel(channelId: string): Promise<void> {
    this.joinedChannelIds.add(channelId);
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    const tenantId = this.tenant.snapshot().tenantId;
    if (!tenantId) return;
    await this.connection.invoke('JoinChannel', tenantId, channelId);
  }

  /** Join every given channel not already joined (e.g. all channels/DMs in the workspace). */
  async joinChannels(channelIds: readonly string[]): Promise<void> {
    const pending = channelIds.filter((id) => !this.joinedChannelIds.has(id));
    await Promise.all(pending.map((id) => this.joinChannel(id)));
  }

  async leaveChannel(channelId: string): Promise<void> {
    this.joinedChannelIds.delete(channelId);
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    const tenantId = this.tenant.snapshot().tenantId;
    if (!tenantId) return;
    await this.connection.invoke('LeaveChannel', tenantId, channelId);
  }

  private async rejoinAllChannels(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    const tenantId = this.tenant.snapshot().tenantId;
    if (!tenantId) return;
    for (const channelId of this.joinedChannelIds) {
      try {
        await this.connection.invoke('JoinChannel', tenantId, channelId);
      } catch {
        // next reconnect/heartbeat can retry
      }
    }
  }

  async sendTyping(channelId: string): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    const tenantId = this.tenant.snapshot().tenantId;
    const name = this.auth.profile()?.name ?? 'Usuário';
    if (!tenantId) return;
    await this.connection.invoke('SendTyping', tenantId, channelId, name);
  }

  async heartbeat(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    const tenantId = this.tenant.snapshot().tenantId;
    if (!tenantId) return;
    await this.connection.invoke('Heartbeat', tenantId);
  }

  async setAway(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    const tenantId = this.tenant.snapshot().tenantId;
    if (!tenantId) return;
    await this.connection.invoke('SetAway', tenantId);
  }

  onMessage(handler: (message: ChatMessage) => void): () => void {
    this.messageHandlers.add(handler);
    return () => this.messageHandlers.delete(handler);
  }

  onMessageEdited(handler: (event: MessageEditEvent) => void): () => void {
    this.editedHandlers.add(handler);
    return () => this.editedHandlers.delete(handler);
  }

  onMessageDeleted(handler: (event: MessageDeleteEvent) => void): () => void {
    this.deletedHandlers.add(handler);
    return () => this.deletedHandlers.delete(handler);
  }

  onReactionChanged(handler: (event: ReactionChangedEvent) => void): () => void {
    this.reactionHandlers.add(handler);
    return () => this.reactionHandlers.delete(handler);
  }

  onAttachmentThumbnailReady(handler: (event: AttachmentThumbnailReadyEvent) => void): () => void {
    this.thumbnailReadyHandlers.add(handler);
    return () => this.thumbnailReadyHandlers.delete(handler);
  }

  onPresenceChanged(handler: (event: PresenceChangedEvent) => void): () => void {
    this.presenceHandlers.add(handler);
    return () => this.presenceHandlers.delete(handler);
  }

  /** Fired after automatic reconnect + re-JoinChannel (B-070 gap-fill hook). */
  onReconnected(handler: () => void | Promise<void>): () => void {
    this.reconnectedHandlers.add(handler);
    return () => this.reconnectedHandlers.delete(handler);
  }

  private async notifyReconnected(): Promise<void> {
    for (const handler of this.reconnectedHandlers) {
      try {
        await handler();
      } catch {
        // individual stores handle their own errors
      }
    }
  }

  private coercePayload<T extends object>(payload: T | string | null | undefined): T | null {
    if (payload == null) return null;
    if (typeof payload === 'string') {
      try {
        return JSON.parse(payload) as T;
      } catch {
        return null;
      }
    }
    return payload;
  }

  private startPresenceLoop(): void {
    this.stopPresenceLoop();
    this.heartbeatTimer = setInterval(() => {
      if (document.visibilityState === 'visible') {
        void this.heartbeat();
      }
    }, 20000);

    this.visibilityHandler = () => {
      if (document.visibilityState === 'hidden') {
        void this.setAway();
      } else {
        void this.heartbeat();
      }
    };
    document.addEventListener('visibilitychange', this.visibilityHandler);
  }

  private stopPresenceLoop(): void {
    if (this.heartbeatTimer) {
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = null;
    }
    if (this.visibilityHandler) {
      document.removeEventListener('visibilitychange', this.visibilityHandler);
      this.visibilityHandler = null;
    }
  }

  private mapPayload(payload: MessageCreatedPayload): ChatMessage | null {
    const id = payload.messageId ?? payload.id;
    if (!id || !payload.channelId) return null;
    const me = this.auth.profile()?.id;
    const conversationId = String(payload.conversationId || payload.channelId);
    const channelId = String(payload.channelId);
    const mentionedUserIds = (payload.mentionedUserIds ?? []).map(String);
    const mentionsMe = !!me && mentionedUserIds.includes(me);
    const clientMessageId = payload.clientMessageId
      ? String(payload.clientMessageId)
      : undefined;
    return {
      id: String(id),
      clientMessageId,
      conversationId,
      channelId,
      authorUserId: String(payload.authorId ?? ''),
      authorName: payload.authorName || String(payload.authorId ?? ''),
      body: payload.body ?? '',
      createdAt: payload.createdAt ?? new Date().toISOString(),
      seq: payload.sequence,
      status: 'persisted',
      mine: !!me && me === String(payload.authorId ?? ''),
      mentionsMe,
      threadId: payload.threadId ? String(payload.threadId) : null,
      parentMessageId: payload.parentMessageId ? String(payload.parentMessageId) : null,
      replyToMessageId: payload.replyToMessageId ? String(payload.replyToMessageId) : null,
      replyTo: payload.replyTo?.messageId
        ? {
            messageId: String(payload.replyTo.messageId),
            authorName: payload.replyTo.authorName ?? '',
            preview: payload.replyTo.preview ?? '',
            deleted: !!payload.replyTo.deleted,
          }
        : null,
      forwardedFromMessageId: payload.forwardedFromMessageId
        ? String(payload.forwardedFromMessageId)
        : null,
      forwardedFromChannelId: payload.forwardedFromChannelId
        ? String(payload.forwardedFromChannelId)
        : null,
      forwardedFrom: payload.forwardedFrom?.messageId
        ? {
            messageId: String(payload.forwardedFrom.messageId),
            channelId: String(payload.forwardedFrom.channelId ?? ''),
            channelName: payload.forwardedFrom.channelName ?? '',
            authorName: payload.forwardedFrom.authorName ?? '',
            createdAt: payload.forwardedFrom.createdAt ?? new Date().toISOString(),
            isDirect: !!payload.forwardedFrom.isDirect,
          }
        : null,
      attachments: (payload.attachments ?? []).map((a) => ({
        id: String(a.id),
        fileName: a.fileName,
        contentType: a.contentType,
        sizeBytes: a.sizeBytes,
        status: 'Ready',
      })),
    };
  }
}
