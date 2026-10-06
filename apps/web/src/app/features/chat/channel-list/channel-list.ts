import { Component, input, inject } from '@angular/core';
import { ChannelStore } from '../../../core/services/channel.store';
import { MemberProfileStore } from '../../../core/services/member-profile.store';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { MessageStore } from '../../../core/services/message.store';
import { PinStore } from '../../../core/services/pin.store';
import { SavedStore } from '../../../core/services/saved.store';
import { FollowedThreadsStore } from '../../../core/services/followed-threads.store';
import { NotificationPreferencesStore } from '../../../core/services/notification-preferences.store';
import { ChannelMuteAction } from '../../../shared/models/chat.models';
import { Badge, SidebarNav, Skeleton, VcTooltip } from '../../../shared/ui';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-channel-list',
  standalone: true,
  imports: [SidebarNav, Skeleton, Badge, VcTooltip],
  template: `
    <div class="channel-list">
      @if (channels.loading()) {
        <div class="channel-list__loading">
          <vc-skeleton height="2rem" />
          <vc-skeleton height="2rem" />
          <vc-skeleton height="2rem" />
        </div>
      } @else {
        @if (!channels.isGuest()) {
        <div class="channel-list__shortcuts" [class.channel-list__shortcuts--compact]="navCompact()">
          <button
            type="button"
            class="channel-list__saved"
            [class.channel-list__saved--compact]="navCompact()"
            [class.channel-list__saved--active]="saved.panelOpen()"
            data-testid="saved-nav"
            [attr.aria-label]="navCompact() ? ui.savedMessages : null"
            [vcTooltip]="navCompact() ? ui.savedMessages : null"
            [tooltipDisabled]="!navCompact()"
            position="right"
            (click)="openSaved()"
          >
            <span class="channel-list__saved-icon" aria-hidden="true">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                <path d="M6 4.5A1.5 1.5 0 0 1 7.5 3h9A1.5 1.5 0 0 1 18 4.5v15.2a.8.8 0 0 1-1.25.66L12 16.6l-4.75 3.76A.8.8 0 0 1 6 19.7V4.5Z" />
              </svg>
            </span>
            @if (!navCompact()) {
              <span class="channel-list__saved-copy">
                <span class="channel-list__saved-label">{{ ui.channelSaved }}</span>
                <span class="channel-list__saved-hint">{{ ui.channelPersonalView }}</span>
              </span>
            }
            @if (saved.pendingCount() > 0) {
              @if (navCompact()) {
                <span class="channel-list__saved-dot" aria-hidden="true"></span>
              } @else {
                <vc-badge tone="accent">{{ saved.pendingCount() }}</vc-badge>
              }
            }
          </button>

          <button
            type="button"
            class="channel-list__saved channel-list__followed"
            [class.channel-list__saved--compact]="navCompact()"
            [class.channel-list__saved--active]="followedThreads.panelOpen()"
            data-testid="followed-threads-nav"
            [attr.aria-label]="navCompact() ? ui.channelFollowedThreads : null"
            [vcTooltip]="navCompact() ? ui.channelFollowedThreads : null"
            [tooltipDisabled]="!navCompact()"
            position="right"
            (click)="openFollowedThreads()"
          >
            <span class="channel-list__saved-icon" aria-hidden="true">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                <path d="M4 5.5h11M4 12h16M4 18.5h11" />
              </svg>
            </span>
            @if (!navCompact()) {
              <span class="channel-list__saved-copy">
                <span class="channel-list__saved-label">{{ ui.channelFollowedThreads }}</span>
                <span class="channel-list__saved-hint">{{ ui.channelPersonalView }}</span>
              </span>
            }
            @if (followedThreads.unreadTotal() > 0) {
              @if (navCompact()) {
                <span class="channel-list__saved-dot" aria-hidden="true"></span>
              } @else {
                <vc-badge tone="accent">{{ followedThreads.unreadTotal() }}</vc-badge>
              }
            }
          </button>
        </div>
        }

        @if (channels.pendingAnnouncements().length) {
          <section class="channel-list__inbox" [attr.aria-label]="ui.inboxAnnouncements">
            @if (!navCompact()) {
              <p class="channel-list__inbox-label">{{ ui.inboxAnnouncements }}</p>
            }
            <ul>
              @for (item of channels.pendingAnnouncements(); track item.messageId) {
                <li>
                  <button type="button" class="channel-list__inbox-item" (click)="onSelect(item.channelId)">
                    <span>{{ item.channelName }}</span>
                    @if (!navCompact()) {
                      <span class="channel-list__inbox-preview">{{ item.bodyPreview }}</span>
                    }
                  </button>
                </li>
              }
            </ul>
          </section>
        }

        <vc-sidebar-nav
          [groups]="channels.spaceGroups()"
          [spaces]="channels.spaces()"
          [directs]="channels.directChannels()"
          [members]="channels.peerCandidates()"
          [contactSections]="channels.contactSections()"
          [directoryMembers]="channels.members()"
          [contactError]="channels.contactError()"
          [presence]="channels.presence()"
          [activeId]="channels.activeChannelId() ?? null"
          [draftIds]="drafts.draftConversationIds()"
          [mutedIds]="notificationPrefs.mutedChannelIds()"
          [overrideIds]="notificationPrefs.channelsWithOverride()"
          [followAllThreadsIds]="notificationPrefs.followAllThreadsChannelIds()"
          [canCreate]="channels.canCreateChannel()"
          [canPublishAnnouncement]="channels.canPublishAnnouncement()"
          [compact]="navCompact()"
          (select)="onSelect($event)"
          (openDm)="onOpenDm($event)"
          (openProfile)="onOpenProfile($event)"
          (openGroup)="onOpenGroup($event)"
          (createChannel)="onCreate($event)"
          (createPersonalGroup)="onCreatePersonal($event)"
          (renamePersonalGroup)="onRenamePersonal($event)"
          (deletePersonalGroup)="onDeletePersonal($event)"
          (savePersonalMembers)="onSavePersonal($event)"
          (channelMuteAction)="onChannelMuteAction($event)"
        />
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-height: 0;
      overflow: hidden;
    }
    .channel-list {
      height: 100%;
      min-height: 0;
      overflow: auto;
      overscroll-behavior: contain;
    }
    .channel-list__loading {
      display: grid;
      gap: 0.5rem;
      padding: 0 var(--vc-space-4);
    }
    .channel-list__inbox {
      display: grid;
      gap: 0.25rem;
      padding: 0 var(--vc-space-3) var(--vc-space-2);
    }
    .channel-list__inbox-label {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.75rem;
    }
    .channel-list__inbox ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.25rem;
    }
    .channel-list__inbox-item {
      width: 100%;
      display: grid;
      gap: 0.1rem;
      text-align: start;
      min-height: 2.75rem;
      padding: 0.35rem 0.55rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: transparent;
      color: inherit;
      font: inherit;
      cursor: pointer;
    }
    .channel-list__inbox-item:focus-visible {
      outline: 2px solid var(--vc-brand);
      outline-offset: 2px;
    }
    .channel-list__inbox-preview {
      color: var(--vc-ink-muted);
      font-size: 0.75rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .channel-list__shortcuts {
      padding: 0 var(--vc-space-3) var(--vc-space-2);
    }
    .channel-list__shortcuts--compact {
      padding-inline: var(--vc-space-2);
    }
    .channel-list__saved {
      width: 100%;
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: var(--vc-space-2);
      min-height: calc(var(--vc-density-row) + 0.25rem);
      padding: 0.4rem 0.65rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: transparent;
      color: var(--vc-ink);
      text-align: left;
      cursor: pointer;
      position: relative;
      font: inherit;
      transition:
        background var(--vc-dur-fast) var(--vc-ease-out),
        border-color var(--vc-dur-fast) var(--vc-ease-out),
        color var(--vc-dur-fast) var(--vc-ease-out);
    }
    .channel-list__saved:hover {
      border-color: color-mix(in srgb, var(--vc-brand) 45%, var(--vc-border));
      background: color-mix(in srgb, var(--vc-brand) 8%, transparent);
    }
    .channel-list__saved--active {
      border-color: color-mix(in srgb, var(--vc-brand) 55%, var(--vc-border));
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
    .channel-list__saved-icon {
      display: inline-grid;
      place-items: center;
      width: 1.35rem;
      height: 1.35rem;
      color: var(--vc-brand);
    }
    .channel-list__saved--active .channel-list__saved-icon svg path {
      fill: var(--vc-brand);
      stroke: var(--vc-brand);
    }
    .channel-list__saved-copy {
      display: grid;
      min-width: 0;
      gap: 0.05rem;
    }
    .channel-list__saved-label {
      font-weight: 600;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .channel-list__saved-hint {
      font-size: 0.7rem;
      color: var(--vc-ink-subtle);
      font-weight: 400;
    }
    .channel-list__saved--compact {
      grid-template-columns: 1fr;
      width: 2.25rem;
      min-height: 2.25rem;
      margin-inline: auto;
      padding: 0;
      justify-items: center;
      border-color: transparent;
      background: transparent;
    }
    .channel-list__saved--compact:hover,
    .channel-list__saved--compact.channel-list__saved--active {
      border-color: transparent;
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
    .channel-list__saved-dot {
      position: absolute;
      top: 0.2rem;
      right: 0.2rem;
      width: 0.45rem;
      height: 0.45rem;
      border-radius: 50%;
      background: var(--vc-brand);
      box-shadow: 0 0 0 2px var(--vc-surface-elevated);
    }
  `,
})
export class ChannelList {
  readonly ui = ui;
  readonly navCompact = input(false);
  readonly channels = inject(ChannelStore);
  private readonly profiles = inject(MemberProfileStore);
  readonly drafts = inject(DraftStoreService);
  readonly saved = inject(SavedStore);
  readonly followedThreads = inject(FollowedThreadsStore);
  readonly notificationPrefs = inject(NotificationPreferencesStore);
  private readonly messages = inject(MessageStore);
  private readonly pins = inject(PinStore);

  async onChannelMuteAction(event: { channelId: string; action: ChannelMuteAction }): Promise<void> {
    const { channelId, action } = event;
    if (action.kind === 'default') {
      await this.notificationPrefs.unmuteChannel(channelId);
    } else if (action.kind === 'all') {
      await this.notificationPrefs.muteChannel(channelId, 'All');
    } else if (action.kind === 'follow-all-threads') {
      await this.notificationPrefs.setFollowAllThreads(channelId, action.enabled);
    } else {
      await this.notificationPrefs.muteChannel(channelId, 'None', action.duration);
    }
  }

  openSaved(): void {
    this.pins.closePanel();
    this.followedThreads.closePanel();
    this.saved.openPanel();
  }

  openFollowedThreads(): void {
    this.pins.closePanel();
    this.saved.closePanel();
    this.followedThreads.openPanel();
  }

  async onSelect(channelId: string): Promise<void> {
    this.saved.closePanel();
    this.followedThreads.closePanel();
    this.channels.selectChannel(channelId);
    await this.messages.loadChannel(channelId);
  }

  onOpenProfile(userId: string): void { this.profiles.open(userId); }

  async onOpenDm(userId: string): Promise<void> {
    this.saved.closePanel();
    this.followedThreads.closePanel();
    const channel = await this.channels.openDirectMessage(userId);
    if (channel) {
      await this.messages.loadChannel(channel.id);
    }
  }

  async onOpenGroup(userIds: string[]): Promise<void> {
    this.saved.closePanel();
    this.followedThreads.closePanel();
    const channel = await this.channels.openGroupDm(userIds);
    if (channel) {
      await this.messages.loadChannel(channel.id);
    }
  }

  async onCreatePersonal(name: string): Promise<void> {
    await this.channels.createPersonalGroup(name);
  }

  async onRenamePersonal(event: { groupId: string; name: string }): Promise<void> {
    await this.channels.renamePersonalGroup(event.groupId, event.name);
  }

  async onDeletePersonal(groupId: string): Promise<void> {
    await this.channels.deletePersonalGroup(groupId);
  }

  async onSavePersonal(event: { groupId: string; userIds: string[] }): Promise<void> {
    await this.channels.savePersonalMembers(event.groupId, event.userIds);
  }

  async onCreate(input: {
    name: string;
    type: string;
    spaceId?: string | null;
    newSpaceName?: string;
  }): Promise<void> {
    try {
      const channel = await this.channels.createChannel(input);
      if (channel) {
        this.saved.closePanel();
        this.followedThreads.closePanel();
        await this.messages.loadChannel(channel.id);
      }
    } catch (err) {
      // surface via store hint
      console.error(err);
    }
  }
}
