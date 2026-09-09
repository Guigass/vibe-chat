import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { ThreadStore } from './thread.store';
import { FollowedThreadItem } from '../../shared/models/chat.models';

@Injectable({ providedIn: 'root' })
export class FollowedThreadsStore {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  private readonly threads = inject(ThreadStore);

  private readonly itemsSignal = signal<FollowedThreadItem[]>([]);
  private readonly unreadTotalSignal = signal(0);
  private readonly loadingSignal = signal(false);
  private readonly panelOpenSignal = signal(false);

  readonly panelOpen = this.panelOpenSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly items = this.itemsSignal.asReadonly();
  readonly unreadTotal = this.unreadTotalSignal.asReadonly();

  openPanel(): void {
    this.panelOpenSignal.set(true);
    void this.reload();
  }

  closePanel(): void {
    this.panelOpenSignal.set(false);
  }

  togglePanel(): void {
    if (this.panelOpenSignal()) {
      this.closePanel();
    } else {
      this.openPanel();
    }
  }

  async loadForWorkspace(workspaceId: string | null | undefined): Promise<void> {
    if (!workspaceId || this.channels.isDemo()) {
      this.itemsSignal.set([]);
      this.unreadTotalSignal.set(0);
      return;
    }

    this.loadingSignal.set(true);
    try {
      const page = await this.api.getFollowedThreads(workspaceId);
      this.itemsSignal.set(page.items);
      this.unreadTotalSignal.set(page.unreadTotal);
    } catch {
      this.itemsSignal.set([]);
      this.unreadTotalSignal.set(0);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async reload(): Promise<void> {
    await this.loadForWorkspace(this.channels.activeWorkspace()?.id);
  }

  async jumpToFollowed(item: FollowedThreadItem): Promise<void> {
    this.closePanel();
    this.channels.selectChannel(item.channelId);
    await this.messages.loadChannel(item.channelId);
    await this.threads.openFromMessage(item.channelId, item.parentMessageId);
  }
}
