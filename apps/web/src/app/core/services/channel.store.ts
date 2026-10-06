import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import {
  Channel,
  ContactSection,
  PendingAnnouncement,
  PresenceStatus,
  Space,
  Workspace,
  WorkspaceMember,
} from '../../shared/models/chat.models';
import { ui } from '../i18n/strings';
import { idsEqual } from './message-sync';
import {
  defaultMessagingPolicy,
  type MessagingPolicy,
} from '../../shared/messaging/messaging-policy';
import {
  demoChannels,
  demoContactSections,
  demoMembers,
  demoPresence,
  demoSpaces,
  demoWorkspace,
} from './channel-demo';
import {
  bumpChannelMention,
  bumpChannelUnread,
  forgetPendingAnnouncement,
  refreshChannelUnreads,
  refreshPendingAnnouncementInbox,
  syncOneChannelUnread,
} from './channel-unread';
import {
  PersonalGroupDeps,
  contactDepartmentLabel,
  createPersonalGroup,
  deletePersonalGroup,
  renamePersonalGroup,
  savePersonalMembers,
} from './channel-contacts';
import {
  ChannelDirectoryDeps,
  addGroupDmParticipantsDirectory,
  createChannelDirectory,
  createSpaceDirectory,
  groupChannelsBySpace,
  leaveGroupDmDirectory,
  openDirectMessageDirectory,
  reloadWorkspaceMembers,
  openGroupDmDirectory,
  renameGroupDmDirectory,
  selectWorkspaceDirectory,
  upsertChannelList,
} from './channel-directory';
@Injectable({ providedIn: 'root' })
export class ChannelStore {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly hub = inject(ChatHubService);

  private readonly workspacesSignal = signal<Workspace[]>([]);
  private readonly spacesSignal = signal<Space[]>([]);
  private readonly channelsSignal = signal<Channel[]>([]);
  private readonly membersSignal = signal<WorkspaceMember[]>([]);
  private readonly contactSectionsSignal = signal<ContactSection[]>([]);
  private readonly contactErrorSignal = signal<string | null>(null);
  private readonly presenceSignal = signal<Record<string, PresenceStatus>>({});
  private readonly activeWorkspaceId = signal<string | null>(null);
  private readonly activeChannelIdSignal = signal<string | null>(null);
  private readonly openedUnreadCountSignal = signal(0);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly usingDemo = signal(false);
  private readonly composerPrefillSignal = signal<string | null>(null);
  private readonly messagingPolicySignal = signal<MessagingPolicy>(defaultMessagingPolicy);
  private readonly pendingAnnouncementsSignal = signal<PendingAnnouncement[]>([]);

  readonly workspaces = this.workspacesSignal.asReadonly();
  readonly spaces = this.spacesSignal.asReadonly();
  readonly channels = this.channelsSignal.asReadonly();
  readonly members = this.membersSignal.asReadonly();
  readonly contactSections = this.contactSectionsSignal.asReadonly();
  readonly contactError = this.contactErrorSignal.asReadonly();
  readonly presence = this.presenceSignal.asReadonly();
  /** Stable id signal — prefer this over `activeChannel()?.id` in effects that must not re-run on channel list refresh. */
  readonly activeChannelId = this.activeChannelIdSignal.asReadonly();
  /** Unread count snapshotted when the channel was opened (B-088 local divider until B-094). */
  readonly openedUnreadCount = this.openedUnreadCountSignal.asReadonly();
  readonly messagingPolicy = this.messagingPolicySignal.asReadonly();
  readonly activeWorkspace = computed(() => this.workspacesSignal().find((w) => w.id === this.activeWorkspaceId()) ?? null);
  readonly activeChannel = computed(() => this.channelsSignal().find((c) => idsEqual(c.id, this.activeChannelIdSignal())) ?? null);
  readonly publicChannels = computed(() => this.channelsSignal().filter((c) => !c.isDirect));
  readonly directChannels = computed(() => this.channelsSignal().filter((c) => !!c.isDirect));
  readonly spaceGroups = computed(() => groupChannelsBySpace(this.spacesSignal(), this.publicChannels()));
  readonly peerCandidates = computed(() => {
    const me = this.auth.profile()?.id;
    return this.membersSignal().filter((m) => !idsEqual(m.userId, me));
  });
  readonly canCreateChannel = computed(() => {
    if (this.usingDemo()) return true;
    const role = this.activeWorkspace()?.role;
    return !!role && ['PlatformOwner', 'WorkspaceOwner', 'Admin', 'Moderator', 'Member'].includes(role);
  });
  readonly canPublishAnnouncement = computed(() => {
    const role = this.activeWorkspace()?.role;
    return !!role && ['PlatformOwner', 'WorkspaceOwner', 'Admin', 'Moderator'].includes(role);
  });
  readonly pendingAnnouncements = this.pendingAnnouncementsSignal.asReadonly();
  readonly isGuest = computed(() => this.activeWorkspace()?.role === 'Guest');
  readonly canInviteGuest = computed(() => {
    const role = this.activeWorkspace()?.role;
    return !!role && ['PlatformOwner', 'WorkspaceOwner', 'Admin'].includes(role);
  });
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly isDemo = this.usingDemo.asReadonly();
  readonly composerPrefill = this.composerPrefillSignal.asReadonly();

  constructor() {
    this.hub.onReadCursorChanged((event) => {
      const me = this.auth.profile()?.id;
      if (!me || event.userId !== me) return;
      void this.syncChannelUnread(event.channelId);
    });
    this.hub.onReconnected(() => {
      void this.refreshPendingAnnouncements();
    });
  }

  /** Ephemeral draft injection from AI suggest-reply (B-045); composer consumes once. */
  prefillComposer(text: string): void {
    this.composerPrefillSignal.set(text);
  }

  consumeComposerPrefill(): string | null {
    const text = this.composerPrefillSignal();
    if (text !== null) {
      this.composerPrefillSignal.set(null);
    }
    return text;
  }

  async load(): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    if (this.auth.isOfflineDemo()) {
      this.seedDemo();
      this.usingDemo.set(true);
      this.loadingSignal.set(false);
      return;
    }

    try {
      const workspaces = await this.api.getWorkspaces();
      this.workspacesSignal.set(workspaces);
      this.usingDemo.set(false);
      const first = workspaces[0];
      if (first) {
        await this.selectWorkspace(first.id);
      }
      await this.refreshUnreads();
    } catch {
      this.workspacesSignal.set([]);
      this.spacesSignal.set([]);
      this.channelsSignal.set([]);
      this.membersSignal.set([]);
      this.contactSectionsSignal.set([]);
      this.presenceSignal.set({});
      this.activeWorkspaceId.set(null);
      this.activeChannelIdSignal.set(null);
      this.usingDemo.set(false);
      this.errorSignal.set(ui.errorLoadWorkspace);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  refreshUnreads(): Promise<void> {
    return refreshChannelUnreads(this.unreadDeps());
  }

  refreshPendingAnnouncements(): Promise<void> {
    return refreshPendingAnnouncementInbox({
      api: this.api,
      isDemo: () => this.usingDemo(),
      workspaceId: () => this.activeWorkspace()?.id,
      setPending: (rows) => this.pendingAnnouncementsSignal.set(rows),
    });
  }

  forgetPendingAnnouncement(messageId: string): void {
    this.pendingAnnouncementsSignal.update((list) => forgetPendingAnnouncement(list, messageId));
  }

  syncChannelUnread(channelId: string): Promise<void> {
    return syncOneChannelUnread(this.unreadDeps(), channelId);
  }

  setOpenedUnreadCount(count: number): void {
    this.openedUnreadCountSignal.set(Math.max(0, count));
  }

  selectWorkspace(workspaceId: string): Promise<void> {
    return selectWorkspaceDirectory(this.directoryDeps(), workspaceId);
  }

  selectChannel(channelId: string): void {
    this.setActiveChannel(channelId);
  }

  patchChannel(channelId: string, patch: Partial<Channel>): void {
    this.channelsSignal.update((list) =>
      list.map((c) => (c.id === channelId ? { ...c, ...patch } : c)),
    );
  }

  setPresence(userId: string, status: PresenceStatus): void {
    this.presenceSignal.update((current) => ({ ...current, [userId]: status }));
  }

  presenceOf(userId: string | undefined | null): PresenceStatus {
    if (!userId) return 'offline';
    return this.presenceSignal()[userId] ?? 'offline';
  }

  createSpace(name: string): Promise<Space | null> {
    return createSpaceDirectory(this.directoryDeps(), name);
  }

  createChannel(input: {
    name: string;
    type?: string;
    spaceId?: string | null;
    newSpaceName?: string;
  }): Promise<Channel | null> {
    return createChannelDirectory(this.directoryDeps(), input);
  }

  openDirectMessage(userId: string): Promise<Channel | null> {
    return openDirectMessageDirectory(this.directoryDeps(), userId);
  }

  reloadMembers(): Promise<void> { return reloadWorkspaceMembers(this.directoryDeps()); }

  openGroupDm(userIds: string[], name?: string): Promise<Channel | null> {
    return openGroupDmDirectory(this.directoryDeps(), userIds, name);
  }

  addGroupDmParticipants(channelId: string, userIds: string[]): Promise<Channel | null> {
    return addGroupDmParticipantsDirectory(this.directoryDeps(), channelId, userIds);
  }

  leaveGroupDm(channelId: string): Promise<void> {
    return leaveGroupDmDirectory(this.directoryDeps(), channelId);
  }

  renameGroupDm(channelId: string, name: string): Promise<Channel | null> {
    return renameGroupDmDirectory(this.directoryDeps(), channelId, name);
  }

  bumpUnread(channelId: string): void {
    this.channelsSignal.update((list) => bumpChannelUnread(list, channelId));
  }

  bumpMention(channelId: string): void {
    this.channelsSignal.update((list) => bumpChannelMention(list, channelId));
  }

  mentionLabels(): Record<string, string> {
    return Object.fromEntries(this.membersSignal().map((m) => [m.userId, m.displayName]));
  }

  contactLabel(userId: string): string | null {
    return contactDepartmentLabel(this.contactSectionsSignal(), userId);
  }

  createPersonalGroup(name: string): Promise<void> {
    return createPersonalGroup(this.personalDeps(), name);
  }

  renamePersonalGroup(groupId: string, name: string): Promise<void> {
    return renamePersonalGroup(this.personalDeps(), groupId, name);
  }

  deletePersonalGroup(groupId: string): Promise<void> {
    return deletePersonalGroup(this.personalDeps(), groupId);
  }

  savePersonalMembers(groupId: string, userIds: string[]): Promise<void> {
    return savePersonalMembers(this.personalDeps(), groupId, userIds);
  }

  private setActiveChannel(channelId: string | null): void {
    if (!channelId) {
      this.activeChannelIdSignal.set(null);
      this.openedUnreadCountSignal.set(0);
      return;
    }
    if (!idsEqual(channelId, this.activeChannelIdSignal())) {
      const current = this.channelsSignal().find((c) => idsEqual(c.id, channelId));
      this.openedUnreadCountSignal.set(current?.unreadCount ?? 0);
    }
    this.activeChannelIdSignal.set(channelId);
    void this.refreshMessagingPolicy(channelId);
  }

  private async refreshMessagingPolicy(channelId: string): Promise<void> {
    if (this.usingDemo()) {
      this.messagingPolicySignal.set(defaultMessagingPolicy);
      return;
    }
    try {
      this.messagingPolicySignal.set(await this.api.getMessagingPolicy(channelId));
    } catch {
      this.messagingPolicySignal.set(defaultMessagingPolicy);
    }
  }

  /** Keep every channel/DM joined so unread badges can bump live (B-088). */
  private joinAllChannels(): void {
    if (this.usingDemo()) return;
    void this.hub.joinChannels(this.channelsSignal().map((c) => c.id));
  }

  private upsertChannel(channel: Channel): void {
    this.channelsSignal.update((list) => upsertChannelList(list, channel));
  }

  private seedDemo(): void {
    const workspace = demoWorkspace();
    this.workspacesSignal.set([workspace]);
    this.activeWorkspaceId.set(workspace.id);
    this.spacesSignal.set(demoSpaces(workspace.id));
    this.channelsSignal.set(demoChannels(workspace.id));
    this.membersSignal.set(demoMembers());
    this.contactSectionsSignal.set(demoContactSections(this.peerCandidates()));
    this.presenceSignal.set(demoPresence());
    this.selectChannel('ch-general');
  }

  private unreadDeps() {
    return {
      api: this.api,
      isDemo: () => this.usingDemo(),
      workspaceId: () => this.activeWorkspace()?.id,
      channels: () => this.channelsSignal(),
      updateChannels: (fn: (list: Channel[]) => Channel[]) => this.channelsSignal.update(fn),
      patchChannel: (channelId: string, patch: Partial<Channel>) => this.patchChannel(channelId, patch),
      activeChannelId: () => this.activeChannelIdSignal(),
      setOpenedUnreadCount: (count: number) => this.openedUnreadCountSignal.set(count),
      setPending: (rows: PendingAnnouncement[]) => this.pendingAnnouncementsSignal.set(rows),
      refreshPending: () => this.refreshPendingAnnouncements(),
    };
  }

  private directoryDeps(): ChannelDirectoryDeps {
    return {
      api: this.api,
      isDemo: () => this.usingDemo(),
      isOfflineDemo: () => this.auth.isOfflineDemo(),
      activeWorkspace: () => this.activeWorkspace(),
      workspaces: () => this.workspacesSignal(),
      spaces: () => this.spacesSignal(),
      channels: () => this.channelsSignal(),
      members: () => this.membersSignal(),
      peerCandidates: () => this.peerCandidates(),
      profileId: () => this.auth.profile()?.id,
      setActiveWorkspaceId: (id) => this.activeWorkspaceId.set(id),
      setSpaces: (spaces) => this.spacesSignal.set(spaces),
      setChannels: (channels) => this.channelsSignal.set(channels),
      updateChannels: (fn) => this.channelsSignal.update(fn),
      updateSpaces: (fn) => this.spacesSignal.update(fn),
      setMembers: (members) => this.membersSignal.set(members),
      setContactSections: (sections) => this.contactSectionsSignal.set(sections),
      setPresence: (presence) => this.presenceSignal.set(presence),
      setError: (message) => this.errorSignal.set(message),
      selectChannel: (channelId) => this.selectChannel(channelId),
      setActiveChannel: (channelId) => this.setActiveChannel(channelId),
      joinAllChannels: () => this.joinAllChannels(),
      joinChannel: (channelId) => void this.hub.joinChannel(channelId),
      upsertChannel: (channel) => this.upsertChannel(channel),
    };
  }

  private personalDeps(): PersonalGroupDeps {
    return {
      api: this.api,
      workspaceId: () => this.activeWorkspaceId(),
      isDemo: () => this.usingDemo(),
      isGuest: () => this.isGuest(),
      profileId: () => this.auth.profile()?.id,
      setError: (message) => this.contactErrorSignal.set(message),
      setMembers: (members) => this.membersSignal.set(members),
      setSections: (sections) => this.contactSectionsSignal.set(sections),
    };
  }
}
