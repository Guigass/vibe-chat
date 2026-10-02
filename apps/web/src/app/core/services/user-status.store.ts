import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import {
  MemberAvailability,
  UserStatusChangedEvent,
  UserStatusStateName,
  isStatusActive,
  statusAccessibleLabel,
  statusEmoji,
  statusLine,
  withPresence,
} from '../../shared/status/user-status';
import { PresenceStatus } from '../../shared/models/chat.models';
import { ChannelStore } from './channel.store';
import { ChatHubService } from './chat-hub.service';

@Injectable({ providedIn: 'root' })
export class UserStatusStore {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly hub = inject(ChatHubService);
  private readonly auth = inject(AuthService);

  private readonly byUser = signal<Record<string, MemberAvailability>>({});
  private readonly nowMs = signal(Date.now());
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly reportedSignal = signal(false);
  private started = false;
  private tick: ReturnType<typeof setInterval> | null = null;
  private readonly unsubs: Array<() => void> = [];

  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly reported = this.reportedSignal.asReadonly();
  readonly clock = this.nowMs.asReadonly();

  readonly mine = computed(() => {
    const id = this.auth.profile()?.id;
    return id ? this.byUser()[id] ?? null : null;
  });

  start(): void {
    if (this.started) return;
    this.started = true;
    this.tick = setInterval(() => this.nowMs.set(Date.now()), 30_000);
    const hub = this.hub as Partial<ChatHubService>;
    const unsubStatus = hub.onUserStatusChanged?.((event) => this.applyEvent(event));
    const unsubPresence = hub.onPresenceChanged?.((event) => this.applyPresence(event.userId, event.status));
    const unsubReconnect = hub.onReconnected?.(() => this.refresh());
    if (unsubStatus) this.unsubs.push(unsubStatus);
    if (unsubPresence) this.unsubs.push(unsubPresence);
    if (unsubReconnect) this.unsubs.push(unsubReconnect);
  }

  entryOf(userId: string | null | undefined): MemberAvailability | null {
    if (!userId) return null;
    const entry = this.byUser()[userId];
    if (!entry) return null;
    if (entry.status && !isStatusActive(entry.status, this.nowMs())) {
      return { ...entry, status: null };
    }
    return entry;
  }

  lineOf(userId: string | null | undefined): string {
    return statusLine(this.entryOf(userId), this.nowMs());
  }

  emojiOf(userId: string | null | undefined): string | null {
    return statusEmoji(this.entryOf(userId), this.nowMs());
  }

  labelOf(userId: string | null | undefined): string {
    return statusAccessibleLabel(this.entryOf(userId), this.nowMs());
  }

  async refresh(): Promise<void> {
    const workspace = this.channels.activeWorkspace();
    if (!workspace || this.channels.isGuest() || this.channels.isDemo()) {
      return;
    }

    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const rows = await this.api.getWorkspaceAvailability(workspace.id);
      const next: Record<string, MemberAvailability> = {};
      for (const row of rows) {
        next[row.userId] = row;
      }
      this.byUser.set(next);
      this.nowMs.set(Date.now());
    } catch {
      this.errorSignal.set('load');
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async save(input: {
    state: UserStatusStateName;
    emoji: string;
    text: string;
    clearAtEndOfDay: boolean;
    expiresAt: string | null;
  }): Promise<boolean> {
    this.errorSignal.set(null);
    this.loadingSignal.set(true);
    try {
      const mine = await this.api.setMyStatus(input);
      this.mergeMine(mine);
      return true;
    } catch (err) {
      this.errorSignal.set(readError(err));
      return false;
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async clearMine(): Promise<boolean> {
    this.errorSignal.set(null);
    this.loadingSignal.set(true);
    try {
      const mine = await this.api.clearMyStatus();
      this.mergeMine(mine);
      return true;
    } catch (err) {
      this.errorSignal.set(readError(err));
      return false;
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async report(userId: string): Promise<boolean> {
    const workspace = this.channels.activeWorkspace();
    if (!workspace) return false;
    this.errorSignal.set(null);
    try {
      await this.api.reportUserStatus(workspace.id, userId);
      this.reportedSignal.set(true);
      return true;
    } catch (err) {
      this.errorSignal.set(readError(err));
      return false;
    }
  }

  async clearMember(userId: string): Promise<boolean> {
    const workspace = this.channels.activeWorkspace();
    if (!workspace) return false;
    this.errorSignal.set(null);
    try {
      await this.api.clearMemberStatus(workspace.id, userId);
      this.byUser.update((current) => {
        const existing = current[userId];
        if (!existing) return current;
        return { ...current, [userId]: { ...existing, status: null, availability: existing.presence === 'online' ? 'available' : existing.presence === 'away' ? 'away' : 'offline' } };
      });
      await this.refresh();
      return true;
    } catch (err) {
      this.errorSignal.set(readError(err));
      return false;
    }
  }

  dismissReport(): void {
    this.reportedSignal.set(false);
  }

  applyEvent(event: UserStatusChangedEvent): void {
    this.byUser.update((current) => ({
      ...current,
      [event.userId]: {
        userId: event.userId,
        presence: event.presence,
        availability: event.availability,
        status: event.status,
      },
    }));
    this.nowMs.set(Date.now());
  }

  applyPresence(userId: string, presence: PresenceStatus): void {
    this.byUser.update((current) => {
      const existing = current[userId];
      if (!existing) return current;
      return { ...current, [userId]: withPresence(existing, presence, this.nowMs()) };
    });
  }

  private mergeMine(response: { presence: PresenceStatus; availability: MemberAvailability['availability']; status: MemberAvailability['status'] }): void {
    const id = this.auth.profile()?.id;
    if (!id) return;
    this.byUser.update((current) => ({
      ...current,
      [id]: {
        userId: id,
        presence: response.presence,
        availability: response.availability,
        status: response.status,
      },
    }));
  }
}

function readError(err: unknown): string {
  const message = err instanceof Error ? err.message : '';
  if (message.startsWith('{')) {
    try {
      const parsed = JSON.parse(message) as { error?: string };
      if (parsed.error) return parsed.error;
    } catch {
      return 'save';
    }
  }
  return 'save';
}
