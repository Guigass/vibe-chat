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

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface PresenceChangedEvent {
  userId: string;
  status: PresenceStatus;
  tenantId?: string;
}

interface MessageCreatedPayload {
  messageId?: string;
  id?: string;
  channelId: string;
  conversationId?: string;
  threadId?: string | null;
  replyToMessageId?: string | null;
  sequence?: number;
  authorId?: string;
  authorName?: string;
  body?: string;
  createdAt?: string;
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
}

interface ReactionChangedPayload {
  messageId?: string;
  channelId: string;
  emoji?: string;
  userId?: string;
  added?: boolean;
  reactions?: Array<{ emoji: string; count: number; userIds?: string[]; me?: boolean }>;
}

@Injectable({ providedIn: 'root' })
export class ChatHubService {
  private readonly auth = inject(AuthService);
  private readonly tenant = inject(TenantContext);
  private connection: HubConnection | null = null;
  private joinedChannelId: string | null = null;
  private heartbeatTimer: ReturnType<typeof setInterval> | null = null;
  private visibilityHandler: (() => void) | null = null;

  private readonly statusSignal = signal<ConnectionStatus>('disconnected');
  private readonly typingSignal = signal<TypingState[]>([]);
  private readonly messageHandlers = new Set<(message: ChatMessage) => void>();
  private readonly editedHandlers = new Set<(event: MessageEditEvent) => void>();
  private readonly deletedHandlers = new Set<(event: MessageDeleteEvent) => void>();
  private readonly reactionHandlers = new Set<(event: ReactionChangedEvent) => void>();
  private readonly presenceHandlers = new Set<(event: PresenceChangedEvent) => void>();

  readonly status = this.statusSignal.asReadonly();
  readonly typingUsers = this.typingSignal.asReadonly();

  async connect(): Promise<void> {
    if (this.auth.isOfflineDemo()) return;
    if (this.connection?.state === HubConnectionState.Connected) return;

    this.statusSignal.set('connecting');
    const devUser = this.auth.devUser();
    const hubUrl = devUser
      ? `${environment.hubUrl}?devUser=${encodeURIComponent(devUser)}`
      : environment.hubUrl;

    this.connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: async () => (await this.auth.getAccessToken()) ?? '',
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) =>
          Math.min(10000, (ctx.previousRetryCount + 1) * 1000),
      })
      .configureLogging(environment.production ? LogLevel.Warning : LogLevel.Information)
      .build();

    this.connection.onreconnecting(() => this.statusSignal.set('reconnecting'));
    this.connection.onreconnected(async () => {
      this.statusSignal.set('connected');
      try {
        await this.heartbeat();
        if (this.joinedChannelId) {
          await this.joinChannel(this.joinedChannelId);
        }
      } catch {
        // banner already reflects connection; next user action can retry join
      }
    });
    this.connection.onclose(() => this.statusSignal.set('disconnected'));

    this.connection.on('MessageCreated', (payload: MessageCreatedPayload) => {
      const message = this.mapPayload(payload);
      if (!message) return;
      for (const handler of this.messageHandlers) {
        handler(message);
      }
    });

    this.connection.on('MessageEdited', (payload: MessageEditedPayload) => {
      const id = payload.messageId ?? payload.id;
      if (!id || !payload.channelId || !payload.body) return;
      const event: MessageEditEvent = {
        id,
        channelId: payload.channelId,
        body: payload.body,
        editedAt: payload.editedAt ?? new Date().toISOString(),
        seq: payload.sequence,
      };
      for (const handler of this.editedHandlers) {
        handler(event);
      }
    });

    this.connection.on('MessageDeleted', (payload: MessageDeletedPayload) => {
      const id = payload.messageId ?? payload.id;
      if (!id || !payload.channelId) return;
      const event: MessageDeleteEvent = {
        id,
        channelId: payload.channelId,
        deletedAt: payload.deletedAt ?? new Date().toISOString(),
        seq: payload.sequence,
      };
      for (const handler of this.deletedHandlers) {
        handler(event);
      }
    });

    this.connection.on('ReactionChanged', (payload: ReactionChangedPayload) => {
      if (!payload.messageId || !payload.channelId || !payload.emoji) return;
      const me = this.auth.profile()?.id;
      const event: ReactionChangedEvent = {
        messageId: payload.messageId,
        channelId: payload.channelId,
        emoji: payload.emoji,
        userId: String(payload.userId ?? ''),
        added: !!payload.added,
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

    this.connection.on('Typing', (payload: {
      channelId: string;
      userId: string;
      displayName: string;
    }) => {
      const typing: TypingState = {
        channelId: payload.channelId,
        userId: String(payload.userId),
        displayName: payload.displayName,
      };
      this.typingSignal.update((list) => {
        const filtered = list.filter(
          (t) => !(t.channelId === typing.channelId && t.userId === typing.userId),
        );
        return [...filtered, typing];
      });
      window.setTimeout(() => {
        this.typingSignal.update((list) =>
          list.filter(
            (t) => !(t.channelId === typing.channelId && t.userId === typing.userId),
          ),
        );
      }, 3000);
    });

    this.connection.on('PresenceChanged', (payload: {
      tenantId?: string;
      userId: string;
      status: string;
    }) => {
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

    try {
      await this.connection.start();
      this.statusSignal.set('connected');
      await this.heartbeat();
      this.startPresenceLoop();
    } catch {
      this.statusSignal.set('disconnected');
    }
  }

  async disconnect(): Promise<void> {
    this.stopPresenceLoop();
    if (!this.connection) return;
    await this.connection.stop();
    this.connection = null;
    this.statusSignal.set('disconnected');
  }

  async joinChannel(channelId: string): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      this.joinedChannelId = channelId;
      return;
    }
    const tenantId = this.tenant.snapshot().tenantId;
    if (!tenantId) return;

    if (this.joinedChannelId && this.joinedChannelId !== channelId) {
      try {
        await this.connection.invoke('LeaveChannel', tenantId, this.joinedChannelId);
      } catch {
        // ignore leave failures; join still proceeds
      }
    }

    this.joinedChannelId = channelId;
    await this.connection.invoke('JoinChannel', tenantId, channelId);
  }

  async leaveChannel(channelId: string): Promise<void> {
    if (this.joinedChannelId === channelId) {
      this.joinedChannelId = null;
    }
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    const tenantId = this.tenant.snapshot().tenantId;
    if (!tenantId) return;
    await this.connection.invoke('LeaveChannel', tenantId, channelId);
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

  onPresenceChanged(handler: (event: PresenceChangedEvent) => void): () => void {
    this.presenceHandlers.add(handler);
    return () => this.presenceHandlers.delete(handler);
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
    const conversationId = payload.conversationId || payload.channelId;
    return {
      id,
      conversationId,
      channelId: payload.channelId,
      authorUserId: String(payload.authorId ?? ''),
      authorName: payload.authorName || String(payload.authorId ?? ''),
      body: payload.body ?? '',
      createdAt: payload.createdAt ?? new Date().toISOString(),
      seq: payload.sequence,
      status: 'persisted',
      mine: !!me && me === String(payload.authorId ?? ''),
      threadId: payload.threadId ?? null,
      replyToMessageId: payload.replyToMessageId ?? null,
      attachments: (payload.attachments ?? []).map((a) => ({
        id: a.id,
        fileName: a.fileName,
        contentType: a.contentType,
        sizeBytes: a.sizeBytes,
        status: 'Ready',
      })),
    };
  }
}
