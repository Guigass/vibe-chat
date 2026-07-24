import { Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';
import { ChatMessage, TypingState } from '../../shared/models/chat.models';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

@Injectable({ providedIn: 'root' })
export class ChatHubService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;

  private readonly statusSignal = signal<ConnectionStatus>('disconnected');
  private readonly typingSignal = signal<TypingState[]>([]);
  private readonly messageHandlers = new Set<(message: ChatMessage) => void>();

  readonly status = this.statusSignal.asReadonly();
  readonly typingUsers = this.typingSignal.asReadonly();

  async connect(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return;

    this.statusSignal.set('connecting');
    this.connection = new HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: async () => (await this.auth.getAccessToken()) ?? '',
      })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(environment.production ? LogLevel.Warning : LogLevel.Information)
      .build();

    this.connection.onreconnecting(() => this.statusSignal.set('reconnecting'));
    this.connection.onreconnected(() => this.statusSignal.set('connected'));
    this.connection.onclose(() => this.statusSignal.set('disconnected'));

    this.connection.on('message.created', (payload: ChatMessage) => {
      for (const handler of this.messageHandlers) {
        handler(payload);
      }
    });

    this.connection.on('typing.started', (payload: TypingState) => {
      this.typingSignal.update((list) => {
        const filtered = list.filter(
          (t) => !(t.channelId === payload.channelId && t.userId === payload.userId),
        );
        return [...filtered, payload];
      });
      window.setTimeout(() => {
        this.typingSignal.update((list) =>
          list.filter(
            (t) => !(t.channelId === payload.channelId && t.userId === payload.userId),
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
    await this.connection.invoke('JoinChannel', channelId);
  }

  async leaveChannel(channelId: string): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    await this.connection.invoke('LeaveChannel', channelId);
  }

  async sendTyping(channelId: string): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    await this.connection.invoke('Typing', channelId);
  }

  onMessage(handler: (message: ChatMessage) => void): () => void {
    this.messageHandlers.add(handler);
    return () => this.messageHandlers.delete(handler);
  }
}
