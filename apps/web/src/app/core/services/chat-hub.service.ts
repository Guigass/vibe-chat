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
import { ChatMessage, TypingState } from '../../shared/models/chat.models';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

interface MessageCreatedPayload {
  messageId?: string;
  id?: string;
  channelId: string;
  sequence?: number;
  authorId?: string;
  authorName?: string;
  body?: string;
  createdAt?: string;
}

@Injectable({ providedIn: 'root' })
export class ChatHubService {
  private readonly auth = inject(AuthService);
  private readonly tenant = inject(TenantContext);
  private connection: HubConnection | null = null;

  private readonly statusSignal = signal<ConnectionStatus>('disconnected');
  private readonly typingSignal = signal<TypingState[]>([]);
  private readonly messageHandlers = new Set<(message: ChatMessage) => void>();

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
    this.connection.onreconnected(() => this.statusSignal.set('connected'));
    this.connection.onclose(() => this.statusSignal.set('disconnected'));

    this.connection.on('MessageCreated', (payload: MessageCreatedPayload) => {
      const message = this.mapPayload(payload);
      if (!message) return;
      for (const handler of this.messageHandlers) {
        handler(message);
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

    try {
      await this.connection.start();
      this.statusSignal.set('connected');
    } catch {
      this.statusSignal.set('disconnected');
    }
  }

  async disconnect(): Promise<void> {
    if (!this.connection) return;
    await this.connection.stop();
    this.connection = null;
    this.statusSignal.set('disconnected');
  }

  async joinChannel(channelId: string): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    const tenantId = this.tenant.snapshot().tenantId;
    if (!tenantId) return;
    await this.connection.invoke('JoinChannel', tenantId, channelId);
  }

  async leaveChannel(channelId: string): Promise<void> {
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

  onMessage(handler: (message: ChatMessage) => void): () => void {
    this.messageHandlers.add(handler);
    return () => this.messageHandlers.delete(handler);
  }

  private mapPayload(payload: MessageCreatedPayload): ChatMessage | null {
    const id = payload.messageId ?? payload.id;
    if (!id || !payload.channelId) return null;
    const me = this.auth.profile()?.id;
    return {
      id,
      conversationId: payload.channelId,
      channelId: payload.channelId,
      authorUserId: String(payload.authorId ?? ''),
      authorName: payload.authorName || String(payload.authorId ?? ''),
      body: payload.body ?? '',
      createdAt: payload.createdAt ?? new Date().toISOString(),
      seq: payload.sequence,
      status: 'persisted',
      mine: !!me && me === String(payload.authorId ?? ''),
    };
  }
}
