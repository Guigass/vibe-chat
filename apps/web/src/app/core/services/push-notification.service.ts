import { Injectable, inject, signal } from '@angular/core';
import { SwPush } from '@angular/service-worker';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { ChatMessage, PushDevice } from '../../shared/models/chat.models';
import { formatMentionPlainText } from '../../shared/markdown/mention-tokens';

export const PUSH_DISMISSED_KEY = 'vc.push.dismissed';
export const PUSH_SENT_KEY = 'vc.push.sent';

const GUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export type PushPermissionHint = 'default' | 'granted' | 'denied' | 'unsupported';

export interface InAppNotice {
  channelId: string;
  messageId: string;
  seq?: number;
  title: string;
  body: string;
}

@Injectable({ providedIn: 'root' })
export class PushNotificationService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly hub = inject(ChatHubService);
  private readonly channels = inject(ChannelStore);
  private readonly swPush = inject(SwPush, { optional: true });

  readonly bannerOpen = signal(false);
  readonly permissionDenied = signal(false);
  readonly devicesOpen = signal(false);
  readonly devices = signal<PushDevice[]>([]);
  readonly notice = signal<InAppNotice | null>(null);
  readonly busy = signal(false);

  constructor() {
    this.hub.onMessage((message) => this.onRemoteMessage(message));
  }

  recordSuccessfulSend(): void {
    if (typeof localStorage === 'undefined') return;
    if (localStorage.getItem(PUSH_SENT_KEY) === '1') return;
    localStorage.setItem(PUSH_SENT_KEY, '1');
    void this.maybeShowBanner();
  }

  dismissBanner(): void {
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(PUSH_DISMISSED_KEY, '1');
    }
    this.bannerOpen.set(false);
  }

  async enablePush(): Promise<void> {
    this.busy.set(true);
    try {
      const status = await this.registerCurrentDevice();
      if (status === 'denied') {
        this.permissionDenied.set(true);
        return;
      }
      this.bannerOpen.set(false);
      this.permissionDenied.set(false);
    } finally {
      this.busy.set(false);
    }
  }

  async toggleDevices(): Promise<void> {
    const next = !this.devicesOpen();
    this.devicesOpen.set(next);
    if (next) {
      await this.refreshDevices();
    }
  }

  async removeDevice(id: string): Promise<void> {
    await this.api.deletePushSubscription(id);
    this.devices.update((list) => list.filter((item) => item.id !== id));
  }

  dismissNotice(): void {
    this.notice.set(null);
  }

  private async maybeShowBanner(): Promise<void> {
    if (typeof localStorage === 'undefined') return;
    if (localStorage.getItem(PUSH_DISMISSED_KEY) === '1') return;
    if (this.auth.isOfflineDemo() || this.channels.isDemo()) return;
    if (this.notificationPermission() !== 'default') return;
    if (!this.swPush?.isEnabled) return;

    try {
      const key = await this.api.getPushPublicKey();
      if (!key.enabled || !key.publicKey) return;
      this.bannerOpen.set(true);
    } catch {
      // Kill switch / network — never surface as a hard error.
    }
  }

  private async registerCurrentDevice(): Promise<PushPermissionHint> {
    if (!this.swPush?.isEnabled || typeof Notification === 'undefined') {
      return 'unsupported';
    }

    const key = await this.api.getPushPublicKey();
    if (!key.enabled || !key.publicKey) {
      return 'unsupported';
    }

    const permission = await Notification.requestPermission();
    if (permission !== 'granted') {
      return permission === 'denied' ? 'denied' : 'default';
    }

    const subscription = await this.swPush.requestSubscription({ serverPublicKey: key.publicKey });
    const json = subscription.toJSON();
    const endpoint = json.endpoint;
    const p256dh = json.keys?.['p256dh'];
    const auth = json.keys?.['auth'];
    if (!endpoint || !p256dh || !auth) {
      return 'unsupported';
    }

    await this.api.registerPushSubscription({
      endpoint,
      p256dh,
      auth,
      userAgent: typeof navigator === 'undefined' ? undefined : navigator.userAgent,
    });
    return 'granted';
  }

  private async refreshDevices(): Promise<void> {
    try {
      this.devices.set(await this.api.listPushSubscriptions());
    } catch {
      this.devices.set([]);
    }
  }

  private onRemoteMessage(message: ChatMessage): void {
    if (message.mine || typeof document === 'undefined' || !document.hasFocus()) return;
    const activeId = this.channels.activeChannelId();
    if (!activeId || activeId === message.channelId) return;

    const channel = this.channels.channels().find((item) => item.id === message.channelId);
    const isDirect = !!channel?.isDirect || channel?.type === 'Direct';
    if (!isDirect && !message.mentionsMe) return;

    this.notice.set({
      channelId: message.channelId,
      messageId: message.id,
      seq: message.seq,
      title: this.noticeTitle(message.authorName, channel, isDirect),
      body: this.noticeBody(message.body),
    });
  }

  private noticeTitle(
    authorName: string | undefined,
    channel: { name?: string; peerDisplayName?: string | null } | undefined,
    isDirect: boolean,
  ): string {
    const author = this.humanName(authorName) ?? 'Alguém';
    if (isDirect) {
      return author === 'Alguém'
        ? this.humanName(channel?.peerDisplayName) ?? 'Mensagem direta'
        : author;
    }

    const channelName = this.humanName(channel?.name?.replace(/^#/, ''));
    return `${author} · #${channelName ?? 'canal'}`;
  }

  private noticeBody(body: string): string {
    const labels = this.channels.mentionLabels();
    return formatMentionPlainText(body, labels).trim().slice(0, 80);
  }

  private humanName(value?: string | null): string | null {
    const text = value?.trim() ?? '';
    if (!text || /^dm:/i.test(text) || GUID_RE.test(text)) {
      return null;
    }
    return text;
  }

  private notificationPermission(): PushPermissionHint {
    if (typeof Notification === 'undefined') return 'unsupported';
    return Notification.permission;
  }
}
