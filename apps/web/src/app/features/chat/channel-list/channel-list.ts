import { Component, input, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { MemberProfileStore } from '../../../core/services/member-profile.store';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { MessageStore } from '../../../core/services/message.store';
import { PinStore } from '../../../core/services/pin.store';
import { SavedStore } from '../../../core/services/saved.store';
import { FollowedThreadsStore } from '../../../core/services/followed-threads.store';
import { InboxStore } from '../../../core/services/inbox.store';
import { NavShortcuts } from './nav-shortcuts';
import { NotificationPreferencesStore } from '../../../core/services/notification-preferences.store';
import { ChannelMuteAction } from '../../../shared/models/chat.models';
import { SidebarNav, Skeleton } from '../../../shared/ui';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-channel-list',
  standalone: true,
  imports: [SidebarNav, Skeleton, NavShortcuts],
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
          <vc-nav-shortcuts [navCompact]="navCompact()" />
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
          [e2eeAvailable]="e2eeAvailable()"
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
  private readonly inbox = inject(InboxStore);
  readonly notificationPrefs = inject(NotificationPreferencesStore);
  private readonly messages = inject(MessageStore);
  private readonly pins = inject(PinStore);
  private readonly api = inject(ApiService);
  readonly e2eeAvailable = signal(false);

  constructor() {
    void this.api.e2eeCapabilities().then(
      () => this.e2eeAvailable.set(true),
      () => this.e2eeAvailable.set(false),
    );
  }

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

  async onSelect(channelId: string): Promise<void> {
    this.saved.closePanel();
    this.followedThreads.closePanel();
    this.inbox.closePanel();
    this.channels.selectChannel(channelId);
    await this.messages.loadChannel(channelId);
  }

  onOpenProfile(userId: string): void { this.profiles.open(userId); }

  async onOpenDm(userId: string): Promise<void> {
    this.saved.closePanel();
    this.followedThreads.closePanel();
    this.inbox.closePanel();
    const channel = await this.channels.openDirectMessage(userId);
    if (channel) {
      await this.messages.loadChannel(channel.id);
    }
  }

  async onOpenGroup(userIds: string[]): Promise<void> {
    this.saved.closePanel();
    this.followedThreads.closePanel();
    this.inbox.closePanel();
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
        this.inbox.closePanel();
        await this.messages.loadChannel(channel.id);
      }
    } catch (err) {
      // surface via store hint
      console.error(err);
    }
  }
}
