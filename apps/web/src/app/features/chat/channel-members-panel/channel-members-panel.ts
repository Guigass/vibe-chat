import { Component, computed, inject, output, signal } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiService } from '../../../core/api/api.service';
import { translateErrorCode, ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';
import { MessageStore } from '../../../core/services/message.store';
import { idsEqual } from '../../../core/services/message-sync';
import { ChannelRosterMember } from '../../../shared/models/chat.models';
import { Button, IconButton, Input } from '../../../shared/ui';

@Component({
  selector: 'vc-channel-members-panel',
  standalone: true,
  imports: [Button, IconButton, Input],
  template: `
    <section class="members-panel" data-testid="channel-members-panel" [attr.aria-label]="ui.channelMembers">
      <header class="members-panel__header">
        <h2>{{ ui.channelMembers }}</h2>
        <vc-icon-button [label]="ui.closePanel" (click)="close.emit()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      @if (!loading() && !privateChannel() && !canManage()) {
        <p class="members-panel__hint">{{ ui.channelMembersPublicHint }}</p>
      }

      @if (loading() && !members().length) {
        <p class="members-panel__status">{{ ui.loading }}</p>
      } @else if (error() && !members().length) {
        <p class="members-panel__status" role="alert">{{ error() }}</p>
      } @else if (!members().length) {
        <p class="members-panel__status">{{ ui.channelMembersEmpty }}</p>
      } @else {
        <ul class="members-panel__list">
          @for (member of members(); track member.userId) {
            <li class="members-panel__item" data-testid="channel-member-row">
              <div class="members-panel__who">
                @if (member.presence === 'online' || member.presence === 'away') {
                  <span
                    class="members-panel__presence"
                    [class.members-panel__presence--online]="member.presence === 'online'"
                    [attr.aria-label]="member.presence === 'online' ? ui.presenceOnline : ui.presenceAway"
                  ></span>
                }
                <strong>{{ member.displayName }}</strong>
                @if (member.isGuest) {
                  <span class="members-panel__guest">{{ ui.channelMemberGuest }}</span>
                }
              </div>
              @if (!isSelf(member)) {
                <div class="members-panel__actions">
                  <button type="button" data-testid="channel-member-direct" (click)="openDirect(member.userId)">
                    {{ ui.channelMembersDirect }}
                  </button>
                  @if (canManage()) {
                    <button type="button" data-testid="channel-member-remove" (click)="remove(member.userId)">
                      {{ ui.channelMembersRemove }}
                    </button>
                  }
                </div>
              }
            </li>
          }
        </ul>
        @if (nextCursor()) {
          <vc-button variant="ghost" type="button" (click)="loadMore()">{{ ui.searchMore }}</vc-button>
        }
      }

      @if (error() && members().length) {
        <p class="members-panel__status" role="alert">{{ error() }}</p>
      }

      @if (canManage()) {
        <form class="members-panel__add" (submit)="add($event)">
          <vc-input
            controlId="vc-channel-member-search"
            [(value)]="addQuery"
            [placeholder]="ui.channelMembersSearch"
            [ariaLabel]="ui.channelMembersSearch"
          />
          @if (addQuery().trim()) {
            @if (!candidates().length) {
              <p class="members-panel__status">{{ ui.channelMembersSearchEmpty }}</p>
            } @else {
              <ul class="members-panel__list">
                @for (candidate of candidates(); track candidate.userId) {
                  <li class="members-panel__item">
                    <span>{{ candidate.displayName }}</span>
                    <button type="button" data-testid="channel-member-add" (click)="addUser(candidate.userId)">
                      {{ ui.channelMembersAdd }}
                    </button>
                  </li>
                }
              </ul>
            }
          }
        </form>
      }

      @if (privateChannel() && selfIsMember()) {
        <vc-button variant="ghost" type="button" data-testid="channel-member-leave" (click)="leave()">
          {{ ui.channelMembersLeave }}
        </vc-button>
      }
    </section>
  `,
  styles: `
    .members-panel {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
      height: 100%;
      min-height: 0;
      padding: var(--vc-space-4);
      overflow: auto;
    }

    .members-panel__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }

    .members-panel__header h2,
    .members-panel__hint,
    .members-panel__status {
      margin: 0;
    }

    .members-panel__header h2 {
      font-size: var(--vc-text-sm);
      font-weight: 600;
    }

    .members-panel__hint,
    .members-panel__status {
      color: var(--vc-ink-muted);
      font-size: var(--vc-text-sm);
    }

    .members-panel__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
    }

    .members-panel__item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }

    .members-panel__who {
      display: flex;
      align-items: center;
      gap: var(--vc-space-2);
      min-width: 0;
    }

    .members-panel__presence {
      width: 0.5rem;
      height: 0.5rem;
      border-radius: 50%;
      background: var(--vc-ink-muted);
      flex: 0 0 auto;
    }

    .members-panel__presence--online {
      background: var(--vc-brand);
    }

    .members-panel__guest {
      color: var(--vc-ink-muted);
      font-size: var(--vc-text-sm);
    }

    .members-panel__actions {
      display: flex;
      gap: var(--vc-space-2);
      flex: 0 0 auto;
    }

    .members-panel__actions button,
    .members-panel__item > button {
      border: 0;
      background: none;
      color: var(--vc-brand);
      cursor: pointer;
      font: inherit;
      padding: 0;
    }

    .members-panel__add {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
    }
  `,
})
export class ChannelMembersPanel {
  readonly close = output<void>();
  readonly changed = output<void>();
  readonly left = output<void>();

  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  private readonly auth = inject(AuthService);

  readonly ui = ui;
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly members = signal<ChannelRosterMember[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly canManage = signal(false);
  readonly addQuery = signal('');

  readonly privateChannel = computed(() => {
    const channel = this.channels.activeChannel();
    return !!channel && (channel.isPrivate || (channel.type ?? '').toLowerCase() === 'private');
  });

  readonly selfIsMember = computed(() =>
    this.members().some((member) => idsEqual(member.userId, this.auth.profile()?.id)),
  );

  readonly candidates = computed(() => {
    const query = this.addQuery().trim().toLowerCase();
    if (!query) return [];
    const taken = new Set(this.members().map((member) => member.userId.toLowerCase()));
    return this.channels.members()
      .filter((member) => !taken.has(member.userId.toLowerCase()))
      .filter((member) =>
        member.displayName.toLowerCase().includes(query)
        || member.email.toLowerCase().includes(query))
      .slice(0, 8);
  });

  constructor() {
    void this.reload();
  }

  isSelf(member: ChannelRosterMember): boolean {
    return idsEqual(member.userId, this.auth.profile()?.id);
  }

  async loadMore(): Promise<void> {
    const cursor = this.nextCursor();
    if (!cursor) return;
    await this.fetchPage(cursor, true);
  }

  add(event: Event): void {
    event.preventDefault();
  }

  async addUser(userId: string): Promise<void> {
    const target = this.scope();
    if (!target) return;
    this.error.set(null);
    try {
      await this.api.addChannelMember(target.workspaceId, target.channelId, userId);
      this.addQuery.set('');
      await this.reload();
      await this.messages.loadChannel(target.channelId);
      this.changed.emit();
    } catch (err) {
      this.error.set(this.readError(err));
    }
  }

  async remove(userId: string): Promise<void> {
    const target = this.scope();
    if (!target) return;
    this.error.set(null);
    try {
      await this.api.removeChannelMember(target.workspaceId, target.channelId, userId);
      await this.reload();
      await this.messages.loadChannel(target.channelId);
      this.changed.emit();
    } catch (err) {
      this.error.set(this.readError(err));
    }
  }

  async leave(): Promise<void> {
    const target = this.scope();
    if (!target) return;
    this.error.set(null);
    try {
      await this.api.leaveChannel(target.workspaceId, target.channelId);
      this.left.emit();
    } catch (err) {
      this.error.set(this.readError(err));
    }
  }

  async openDirect(userId: string): Promise<void> {
    const channel = await this.channels.openDirectMessage(userId);
    if (channel) {
      await this.messages.loadChannel(channel.id);
    }
    this.close.emit();
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    this.members.set([]);
    this.nextCursor.set(null);
    if (this.channels.isDemo() || this.auth.isOfflineDemo()) {
      this.canManage.set(false);
      this.members.set(this.channels.members().map((member) => ({
        userId: member.userId,
        displayName: member.displayName,
        email: member.email,
        isGuest: false,
        presence: null,
      })));
      this.loading.set(false);
      return;
    }
    await this.fetchPage(undefined, false);
  }

  private async fetchPage(cursor: string | undefined, append: boolean): Promise<void> {
    const target = this.scope();
    if (!target) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.api.getChannelRoster(target.workspaceId, target.channelId, {
        cursor,
        limit: 50,
      });
      this.canManage.set(page.canManage && this.privateChannel());
      this.nextCursor.set(page.nextCursor);
      this.members.update((current) => append ? [...current, ...page.items] : page.items);
    } catch (err) {
      this.error.set(this.readError(err));
    } finally {
      this.loading.set(false);
    }
  }

  private scope(): { workspaceId: string; channelId: string } | null {
    const workspaceId = this.channels.activeWorkspace()?.id;
    const channelId = this.channels.activeChannel()?.id;
    if (!workspaceId || !channelId) return null;
    return { workspaceId, channelId };
  }

  private readError(err: unknown): string {
    if (!(err instanceof Error) || !err.message) return ui.channelMembersError;
    try {
      const body = JSON.parse(err.message) as { error?: string };
      const translated = translateErrorCode(body.error);
      return translated && translated !== body.error ? translated : ui.channelMembersError;
    } catch {
      return ui.channelMembersError;
    }
  }
}
