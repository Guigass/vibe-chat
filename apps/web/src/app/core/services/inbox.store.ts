import { Injectable, inject, signal } from '@angular/core';
import { InboxApiService, InboxItem, InboxQuery } from '../api/inbox-api.service';
import { ui } from '../i18n/strings';
import { ChannelStore } from './channel.store';
import { ChatHubService } from './chat-hub.service';
import { FollowedThreadsStore } from './followed-threads.store';
import { onInboxPoke } from './inbox-refresh';
import { MessageStore } from './message.store';
import { NotificationPreferencesStore } from './notification-preferences.store';
import { PinStore } from './pin.store';
import { SavedStore } from './saved.store';
import { ThreadStore } from './thread.store';

@Injectable({ providedIn: 'root' })
export class InboxStore {
  private readonly api = inject(InboxApiService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  private readonly threads = inject(ThreadStore);
  private readonly saved = inject(SavedStore);
  private readonly pins = inject(PinStore);
  private readonly followed = inject(FollowedThreadsStore);
  private readonly notificationPrefs = inject(NotificationPreferencesStore);
  private readonly hub = inject(ChatHubService);

  private readonly itemsSignal = signal<InboxItem[]>([]);
  private readonly totalSignal = signal(0);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly panelOpenSignal = signal(false);
  private readonly typeSignal = signal('');
  private readonly stateSignal = signal<'open' | 'snoozed' | 'all'>('open');
  private readonly cursorSignal = signal<string | null>(null);
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;

  readonly items = this.itemsSignal.asReadonly();
  readonly openCount = this.totalSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly panelOpen = this.panelOpenSignal.asReadonly();
  readonly typeFilter = this.typeSignal.asReadonly();
  readonly stateFilter = this.stateSignal.asReadonly();
  readonly hasMore = this.cursorSignal.asReadonly();

  constructor() {
    this.hub.onMessage(() => this.schedule());
    this.hub.onReadCursorChanged(() => this.schedule());
    this.hub.onAnnouncementAcknowledged(() => this.schedule());
    this.hub.onReconnected(() => this.schedule());
    onInboxPoke(() => this.schedule());
  }

  openPanel(): void {
    this.threads.close();
    this.pins.closePanel();
    this.saved.closePanel();
    this.followed.closePanel();
    this.notificationPrefs.closePanel();
    this.panelOpenSignal.set(true);
    void this.refresh();
  }

  closePanel(): void {
    this.panelOpenSignal.set(false);
  }

  setType(type: string): void {
    this.typeSignal.set(type);
    void this.refresh();
  }

  setState(state: 'open' | 'snoozed' | 'all'): void {
    this.stateSignal.set(state);
    void this.refresh();
  }

  schedule(): void {
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    this.refreshTimer = setTimeout(() => void this.refresh(), 250);
  }

  async refresh(): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId || this.channels.isDemo()) {
      this.itemsSignal.set([]);
      this.totalSignal.set(0);
      this.cursorSignal.set(null);
      return;
    }

    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const page = await this.api.list(workspaceId, this.query());
      this.itemsSignal.set(page.items ?? []);
      this.totalSignal.set(page.counts?.total ?? page.items?.length ?? 0);
      this.cursorSignal.set(page.nextCursor ?? null);
    } catch {
      this.errorSignal.set(ui.inboxError);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async loadMore(): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    const cursor = this.cursorSignal();
    if (!workspaceId || !cursor || this.channels.isDemo()) return;
    try {
      const page = await this.api.list(workspaceId, { ...this.query(), cursor });
      const seen = new Set(this.itemsSignal().map((item) => item.itemKey));
      const next = (page.items ?? []).filter((item) => !seen.has(item.itemKey));
      this.itemsSignal.update((current) => [...current, ...next]);
      this.cursorSignal.set(page.nextCursor ?? null);
    } catch {
      this.errorSignal.set(ui.inboxError);
    }
  }

  async openItem(item: InboxItem): Promise<void> {
    const channelId = item.navigation?.channelId ?? item.channelId;
    if (item.kind === 'thread' && item.threadId) {
      await this.channels.selectChannel(channelId);
      await this.threads.openById(item.threadId);
      this.closePanel();
      return;
    }

    const messageId = item.navigation?.messageId ?? item.messageId;
    const sequence = item.navigation?.sequence ?? item.sequence;
    if (messageId && sequence != null) {
      await this.messages.jumpToSequence(channelId, sequence, messageId);
    } else {
      await this.channels.selectChannel(channelId);
    }
    this.closePanel();
  }

  async markRead(item: InboxItem): Promise<void> {
    await this.mutate(item, (workspaceId) => this.api.markRead(workspaceId, item.itemKey));
  }

  async markUnread(item: InboxItem): Promise<void> {
    await this.mutate(item, (workspaceId) => this.api.markUnread(workspaceId, item.itemKey));
  }

  async snooze(item: InboxItem): Promise<void> {
    const until = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();
    await this.mutate(item, (workspaceId) => this.api.snooze(workspaceId, item.itemKey, until));
  }

  async unsnooze(item: InboxItem): Promise<void> {
    await this.mutate(item, (workspaceId) => this.api.unsnooze(workspaceId, item.itemKey));
  }

  private async mutate(item: InboxItem, action: (workspaceId: string) => Promise<void>): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return;
    try {
      await action(workspaceId);
      await this.refresh();
    } catch {
      this.errorSignal.set(ui.inboxError);
    }
    void item;
  }

  private query(): InboxQuery {
    return {
      type: this.typeSignal() || undefined,
      state: this.stateSignal(),
      limit: 30,
    };
  }
}
