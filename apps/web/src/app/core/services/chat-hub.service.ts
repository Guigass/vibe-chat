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
import { HUB_KEEP_ALIVE_MS, HUB_SERVER_TIMEOUT_MS, nextHubRetryDelayMs } from './chat-hub-reconnect';
import { bindChatHubHandlers } from './chat-hub-handlers';
import { HubPresenceLoop } from './chat-hub-presence';
import { HubRetrySession } from './chat-hub-session';
import { ui } from '../i18n/strings';
import {
  AnnouncementAcknowledgedEvent,
  AttachmentThumbnailReadyEvent,
  ConnectionStatus,
  HubUserStatusEvent,
  LinkPreviewReadyEvent,
  MessageDeleteEvent,
  MessageEditEvent,
  PinChangedEvent,
  PollChangedEvent,
  PresenceChangedEvent,
  ReactionChangedEvent,
  ReadCursorChangedEvent,
  ScheduleNoticeEvent,
} from './chat-hub.types';

export {
  AWAY_GRACE_MS,
  type AnnouncementAcknowledgedEvent,
  type AttachmentThumbnailReadyEvent,
  type ConnectionStatus,
  type HubUserStatusEvent,
  type LinkPreviewReadyEvent,
  type MessageDeleteEvent,
  type MessageEditEvent,
  type PinChangedEvent,
  type PollChangedEvent,
  type PresenceChangedEvent,
  type ReactionChangedEvent,
  type ReadCursorChangedEvent,
  type ScheduleNoticeEvent,
} from './chat-hub.types';

@Injectable({ providedIn: 'root' })
export class ChatHubService {
  private readonly auth = inject(AuthService);
  private readonly tenant = inject(TenantContext);
  private readonly presenceLoop = new HubPresenceLoop();
  private readonly retry = new HubRetrySession();
  private connection: HubConnection | null = null;
  /**
   * Channels this client is subscribed to (SignalR groups). Unlike a single
   * "active channel" tracker, we keep every visited channel joined so unread
   * badges can bump live for channels the user isn't currently viewing (B-088).
   */
  private readonly joinedChannelIds = new Set<string>();
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
  private readonly pinHandlers = new Set<(event: PinChangedEvent) => void>();
  private readonly pollHandlers = new Set<(event: PollChangedEvent) => void>();
  private readonly announcementHandlers = new Set<(event: AnnouncementAcknowledgedEvent) => void>();
  private readonly scheduleNoticeHandlers = new Set<(event: ScheduleNoticeEvent) => void>();
  private readonly presenceHandlers = new Set<(event: PresenceChangedEvent) => void>();
  private readonly userStatusHandlers = new Set<(event: HubUserStatusEvent) => void>();
  private readonly reconnectedHandlers = new Set<() => void | Promise<void>>();
  private readonly thumbnailReadyHandlers = new Set<(event: AttachmentThumbnailReadyEvent) => void>();
  private readonly linkPreviewReadyHandlers = new Set<(event: LinkPreviewReadyEvent) => void>();
  private readonly readCursorHandlers = new Set<(event: ReadCursorChangedEvent) => void>();

  readonly status = this.statusSignal.asReadonly();
  readonly typingUsers = this.typingSignal.asReadonly();

  async connect(): Promise<void> {
    if (this.auth.isOfflineDemo()) return;
    this.wantConnected = true;
    this.retry.ensureNetworkListeners(this.retryHooks());

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
    this.retry.clear();
    this.statusSignal.set('connecting');

    if (!this.connection) {
      this.connection = this.buildConnection();
    }

    try {
      await this.connection.start();
      this.retry.resetCount();
      this.statusSignal.set('connected');
      await this.heartbeat();
      await this.rejoinAllChannels();
      this.startPresenceLoop();
    } catch {
      this.statusSignal.set('disconnected');
      this.retry.schedule(this.retryHooks());
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
      this.retry.resetCount();
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
        this.retry.schedule(this.retryHooks());
      }
    });

    bindChatHubHandlers(connection, {
      profileId: () => this.auth.profile()?.id,
      typing: this.typingSignal,
      messageHandlers: this.messageHandlers,
      editedHandlers: this.editedHandlers,
      deletedHandlers: this.deletedHandlers,
      reactionHandlers: this.reactionHandlers,
      pinHandlers: this.pinHandlers,
      pollHandlers: this.pollHandlers,
      announcementHandlers: this.announcementHandlers,
      scheduleNoticeHandlers: this.scheduleNoticeHandlers,
      presenceHandlers: this.presenceHandlers,
      userStatusHandlers: this.userStatusHandlers,
      thumbnailReadyHandlers: this.thumbnailReadyHandlers,
      linkPreviewReadyHandlers: this.linkPreviewReadyHandlers,
      readCursorHandlers: this.readCursorHandlers,
    });
    return connection;
  }

  private retryHooks() {
    return {
      wantConnected: () => this.wantConnected,
      status: () => this.statusSignal(),
      setStatus: (status: ConnectionStatus) => this.statusSignal.set(status),
      connection: () => this.connection,
      dropConnection: () => {
        this.connection = null;
      },
      connect: () => this.connect(),
    };
  }

  async disconnect(): Promise<void> {
    this.wantConnected = false;
    this.retry.clear();
    this.retry.removeNetworkListeners();
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
    const name = this.auth.profile()?.name ?? ui.userFallback;
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
    return this.listen(this.messageHandlers, handler);
  }

  onMessageEdited(handler: (event: MessageEditEvent) => void): () => void {
    return this.listen(this.editedHandlers, handler);
  }

  onMessageDeleted(handler: (event: MessageDeleteEvent) => void): () => void {
    return this.listen(this.deletedHandlers, handler);
  }

  onReactionChanged(handler: (event: ReactionChangedEvent) => void): () => void {
    return this.listen(this.reactionHandlers, handler);
  }

  onPinChanged(handler: (event: PinChangedEvent) => void): () => void {
    return this.listen(this.pinHandlers, handler);
  }

  onScheduleNotice(handler: (event: ScheduleNoticeEvent) => void): () => void {
    return this.listen(this.scheduleNoticeHandlers, handler);
  }

  onPollChanged(handler: (event: PollChangedEvent) => void): () => void {
    return this.listen(this.pollHandlers, handler);
  }

  onAnnouncementAcknowledged(handler: (event: AnnouncementAcknowledgedEvent) => void): () => void {
    return this.listen(this.announcementHandlers, handler);
  }

  onAttachmentThumbnailReady(handler: (event: AttachmentThumbnailReadyEvent) => void): () => void {
    return this.listen(this.thumbnailReadyHandlers, handler);
  }

  onLinkPreviewReady(handler: (event: LinkPreviewReadyEvent) => void): () => void {
    return this.listen(this.linkPreviewReadyHandlers, handler);
  }

  onReadCursorChanged(handler: (event: ReadCursorChangedEvent) => void): () => void {
    return this.listen(this.readCursorHandlers, handler);
  }

  onPresenceChanged(handler: (event: PresenceChangedEvent) => void): () => void {
    return this.listen(this.presenceHandlers, handler);
  }

  onUserStatusChanged(handler: (event: HubUserStatusEvent) => void): () => void {
    return this.listen(this.userStatusHandlers, handler);
  }

  /** Fired after automatic reconnect + re-JoinChannel (B-070 gap-fill hook). */
  onReconnected(handler: () => void | Promise<void>): () => void {
    return this.listen(this.reconnectedHandlers, handler);
  }

  private listen<T>(set: Set<(event: T) => void>, handler: (event: T) => void): () => void {
    set.add(handler);
    return () => {
      set.delete(handler);
    };
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

  private startPresenceLoop(): void {
    this.presenceLoop.start({
      heartbeat: () => this.heartbeat(),
      setAway: () => this.setAway(),
    });
  }

  private stopPresenceLoop(): void {
    this.presenceLoop.stop();
  }
}
