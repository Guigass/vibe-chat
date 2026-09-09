import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ChannelStore } from './channel.store';
import { ThreadStore } from './thread.store';
import { ChatHubService } from './chat-hub.service';
import { ui } from '../i18n/strings';
import { FollowedThreadItem } from '../../shared/models/chat.models';

/** B-102 — "Threads seguidas": follow/unfollow, the workspace list, and its unread badge. */
@Injectable({ providedIn: 'root' })
export class FollowedThreadsStore {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly threads = inject(ThreadStore);
  private readonly hub = inject(ChatHubService);

  private readonly itemsSignal = signal<FollowedThreadItem[]>([]);
  private readonly followedIdsSignal = signal<Set<string>>(new Set());
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly panelOpenSignal = signal(false);
  private readonly toastShownSignal = signal<Set<string>>(new Set());

  readonly panelOpen = this.panelOpenSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly items = this.itemsSignal.asReadonly();
  readonly unreadTotal = computed(() =>
    this.itemsSignal().reduce((sum, item) => sum + item.unreadCount, 0),
  );

  constructor() {
    // Cheap client-side patch instead of a dedicated realtime event: a new reply to a thread
    // the caller already has loaded just bumps its unread badge and moves it to the top.
    this.hub.onMessage((message) => {
      if (!message.threadId) return;
      this.itemsSignal.update((items) => {
        const idx = items.findIndex((i) => i.threadId === message.threadId);
        if (idx < 0) return items;
        const bumped: FollowedThreadItem = {
          ...items[idx],
          unreadCount: message.mine ? items[idx].unreadCount : items[idx].unreadCount + 1,
          lastActivityAt: message.createdAt,
        };
        return [bumped, ...items.slice(0, idx), ...items.slice(idx + 1)];
      });
    });
  }

  isFollowing(threadId: string): boolean {
    return this.followedIdsSignal().has(threadId);
  }

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
      this.followedIdsSignal.set(new Set());
      return;
    }

    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const page = await this.api.getFollowedThreads(workspaceId, { limit: 100 });
      this.itemsSignal.set(page.items);
      this.followedIdsSignal.set(new Set(page.items.map((i) => i.threadId)));
    } catch {
      this.errorSignal.set(ui.threadFollowLoadError);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async reload(): Promise<void> {
    await this.loadForWorkspace(this.channels.activeWorkspace()?.id);
  }

  async follow(threadId: string): Promise<boolean> {
    if (this.channels.isDemo()) {
      this.followedIdsSignal.update((current) => new Set(current).add(threadId));
      this.threads.setFollowing(threadId, true, 'Manual');
      return true;
    }
    try {
      await this.api.followThread(threadId);
      this.followedIdsSignal.update((current) => new Set(current).add(threadId));
      this.threads.setFollowing(threadId, true, 'Manual');
      await this.reload();
      return true;
    } catch {
      this.errorSignal.set(ui.threadFollowActionError);
      return false;
    }
  }

  async unfollow(threadId: string): Promise<boolean> {
    if (this.channels.isDemo()) {
      this.followedIdsSignal.update((current) => {
        const next = new Set(current);
        next.delete(threadId);
        return next;
      });
      this.threads.setFollowing(threadId, false);
      this.itemsSignal.update((items) => items.filter((i) => i.threadId !== threadId));
      return true;
    }
    try {
      await this.api.unfollowThread(threadId);
      this.followedIdsSignal.update((current) => {
        const next = new Set(current);
        next.delete(threadId);
        return next;
      });
      this.threads.setFollowing(threadId, false);
      this.itemsSignal.update((items) => items.filter((i) => i.threadId !== threadId));
      return true;
    } catch {
      this.errorSignal.set(ui.threadFollowActionError);
      return false;
    }
  }

  /** Called once when the thread panel opens on a thread the caller already follows. */
  async markRead(threadId: string, lastReadSequence: number): Promise<void> {
    if (this.channels.isDemo()) {
      this.itemsSignal.update((items) =>
        items.map((i) => (i.threadId === threadId ? { ...i, unreadCount: 0 } : i)),
      );
      return;
    }
    try {
      await this.api.markThreadRead(threadId, lastReadSequence);
      this.itemsSignal.update((items) =>
        items.map((i) => (i.threadId === threadId ? { ...i, unreadCount: 0 } : i)),
      );
    } catch {
      // best-effort — the badge just stays stale until the next reload
    }
  }

  async jumpToThread(item: FollowedThreadItem): Promise<void> {
    await this.channels.selectChannel(item.channelId);
    await this.threads.openById(item.threadId);
    this.closePanel();
  }

  /** First-time auto-follow toast (spec: "você está seguindo esta thread", with undo) — once per thread per session. */
  shouldAnnounceAutoFollow(threadId: string): boolean {
    if (this.toastShownSignal().has(threadId)) return false;
    this.toastShownSignal.update((current) => new Set(current).add(threadId));
    return true;
  }
}
