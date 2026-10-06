import { Injectable, inject, signal } from '@angular/core';
import { MemberPublicProfile, ProfileApiService } from '../api/profile-api.service';
import { AuthService } from '../auth/auth.service';
import { ChannelStore } from './channel.store';

@Injectable({ providedIn: 'root' })
export class MemberProfileStore {
  private readonly api = inject(ProfileApiService);
  private readonly auth = inject(AuthService);
  private readonly channels = inject(ChannelStore);

  readonly target = signal<MemberPublicProfile | null>(null);
  readonly openUserId = signal<string | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly saved = signal(false);

  open(userId: string | null | undefined): void {
    if (!userId) return;
    this.saved.set(false);
    this.error.set(null);
    this.openUserId.set(userId);
    void this.load(userId);
  }

  close(): void {
    this.openUserId.set(null);
    this.target.set(null);
    this.error.set(null);
    this.saved.set(false);
  }

  isMine(): boolean {
    const id = this.auth.profile()?.id;
    return !!id && id === this.openUserId();
  }

  async save(input: {
    displayName: string;
    jobTitle: string;
    about: string;
    highlightMessage: string;
  }): Promise<boolean> {
    this.loading.set(true);
    this.error.set(null);
    this.saved.set(false);
    try {
      const card = await this.api.updateMemberProfile(input, crypto.randomUUID());
      this.target.set(card);
      this.saved.set(true);
      await this.channels.reloadMembers();
      return true;
    } catch (err) {
      this.error.set(readError(err));
      return false;
    } finally {
      this.loading.set(false);
    }
  }

  async upload(file: File): Promise<boolean> {
    return this.mutate(() => this.api.uploadAvatar(file));
  }

  async removeAvatar(): Promise<boolean> {
    return this.mutate(() => this.api.deleteAvatar());
  }

  async message(): Promise<void> {
    const id = this.openUserId();
    if (!id || this.isMine()) return;
    await this.channels.openDirectMessage(id);
    this.close();
  }

  private async load(userId: string): Promise<void> {
    const workspace = this.channels.activeWorkspace();
    if (!workspace) return;
    this.loading.set(true);
    try {
      const card = await this.api.getMemberProfile(workspace.id, userId);
      if (this.openUserId() === userId) this.target.set(card);
    } catch (err) {
      this.error.set(readError(err));
    } finally {
      this.loading.set(false);
    }
  }

  private async mutate(run: () => Promise<MemberPublicProfile>): Promise<boolean> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const card = await run();
      this.target.set(card);
      await this.channels.reloadMembers();
      return true;
    } catch (err) {
      this.error.set(readError(err));
      return false;
    } finally {
      this.loading.set(false);
    }
  }
}

function readError(err: unknown): string {
  const message = err instanceof Error ? err.message : '';
  try {
    const parsed = JSON.parse(message) as { error?: string };
    return parsed.error || 'save';
  } catch {
    return 'save';
  }
}
