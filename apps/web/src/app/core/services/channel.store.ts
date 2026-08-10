import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import {
  Channel,
  PresenceStatus,
  Space,
  SpaceGroup,
  Workspace,
  WorkspaceMember,
} from '../../shared/models/chat.models';
import { idsEqual } from './message-sync';

@Injectable({ providedIn: 'root' })
export class ChannelStore {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly hub = inject(ChatHubService);

  private readonly workspacesSignal = signal<Workspace[]>([]);
  private readonly spacesSignal = signal<Space[]>([]);
  private readonly channelsSignal = signal<Channel[]>([]);
  private readonly membersSignal = signal<WorkspaceMember[]>([]);
  private readonly presenceSignal = signal<Record<string, PresenceStatus>>({});
  private readonly activeWorkspaceId = signal<string | null>(null);
  private readonly activeChannelIdSignal = signal<string | null>(null);
  private readonly openedUnreadCountSignal = signal(0);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly usingDemo = signal(false);
  private readonly composerPrefillSignal = signal<string | null>(null);

  readonly workspaces = this.workspacesSignal.asReadonly();
  readonly spaces = this.spacesSignal.asReadonly();
  readonly channels = this.channelsSignal.asReadonly();
  readonly members = this.membersSignal.asReadonly();
  readonly presence = this.presenceSignal.asReadonly();
  /** Stable id signal — prefer this over `activeChannel()?.id` in effects that must not re-run on channel list refresh. */
  readonly activeChannelId = this.activeChannelIdSignal.asReadonly();
  /** Unread count snapshotted when the channel was opened (B-088 local divider until B-094). */
  readonly openedUnreadCount = this.openedUnreadCountSignal.asReadonly();
  readonly activeWorkspace = computed(
    () => this.workspacesSignal().find((w) => w.id === this.activeWorkspaceId()) ?? null,
  );
  readonly activeChannel = computed(
    () => this.channelsSignal().find((c) => idsEqual(c.id, this.activeChannelIdSignal())) ?? null,
  );
  readonly publicChannels = computed(() =>
    this.channelsSignal().filter((c) => !c.isDirect),
  );
  readonly directChannels = computed(() =>
    this.channelsSignal().filter((c) => !!c.isDirect),
  );
  readonly spaceGroups = computed((): SpaceGroup[] => {
    const spaces = [...this.spacesSignal()].sort((a, b) => a.order - b.order || a.name.localeCompare(b.name));
    const channels = this.publicChannels();
    const grouped: SpaceGroup[] = spaces.map((space) => ({
      space,
      channels: channels.filter((c) => c.spaceId === space.id),
    }));
    const ungrouped = channels.filter(
      (c) => !c.spaceId || !spaces.some((s) => s.id === c.spaceId),
    );
    if (ungrouped.length) {
      grouped.push({ space: null, channels: ungrouped });
    }
    return grouped.filter((g) => g.channels.length > 0 || g.space !== null);
  });
  readonly peerCandidates = computed(() => {
    const me = this.auth.profile()?.id;
    return this.membersSignal().filter((m) => m.userId !== me);
  });
  readonly canCreateChannel = computed(() => {
    if (this.usingDemo()) return true;
    const role = this.activeWorkspace()?.role;
    return !!role && ['PlatformOwner', 'WorkspaceOwner', 'Admin', 'Moderator', 'Member'].includes(role);
  });
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly isDemo = this.usingDemo.asReadonly();
  readonly composerPrefill = this.composerPrefillSignal.asReadonly();

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
      this.seedDemo();
      this.usingDemo.set(true);
      this.errorSignal.set('API indisponível — modo demonstração local');
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async refreshUnreads(): Promise<void> {
    if (this.usingDemo()) return;
    const channels = this.channelsSignal();
    const activeId = this.activeChannelIdSignal();
    const updated = await Promise.all(
      channels.map(async (channel) => {
        try {
          const counts = await this.api.getUnreadCount(channel.id);
          return { ...channel, unreadCount: counts.unreadCount, mentionCount: counts.mentionCount };
        } catch {
          return channel;
        }
      }),
    );
    const active = activeId ? updated.find((c) => idsEqual(c.id, activeId)) : undefined;
    if (active && this.openedUnreadCountSignal() === 0 && active.unreadCount > 0) {
      this.openedUnreadCountSignal.set(active.unreadCount);
    }
    this.channelsSignal.set(
      updated.map((channel) =>
        activeId && idsEqual(channel.id, activeId)
          ? { ...channel, unreadCount: 0, mentionCount: 0 }
          : channel,
      ),
    );
  }

  async selectWorkspace(workspaceId: string): Promise<void> {
    this.activeWorkspaceId.set(workspaceId);
    try {
      if (this.usingDemo()) {
        this.spacesSignal.set(this.demoSpaces(workspaceId));
        this.channelsSignal.set(this.demoChannels(workspaceId));
        this.membersSignal.set(this.demoMembers());
        this.presenceSignal.set({
          'u-alice': 'online',
          'u-bob': 'away',
        });
      } else {
        const [spaces, channels, members, presence] = await Promise.all([
          this.api.getSpaces(workspaceId),
          this.api.getChannels(workspaceId),
          this.api.getMembers(workspaceId),
          this.api.getPresence(workspaceId).catch(() => ({}) as Record<string, PresenceStatus>),
        ]);
        this.spacesSignal.set(spaces);
        this.channelsSignal.set(channels);
        this.membersSignal.set(members);
        this.presenceSignal.set(presence);
        this.joinAllChannels();
      }
      const first = this.channelsSignal()[0];
      if (first) this.selectChannel(first.id);
      else this.setActiveChannel(null);
    } catch (err) {
      this.errorSignal.set(err instanceof Error ? err.message : 'Falha ao carregar channels');
    }
  }

  selectChannel(channelId: string): void {
    this.setActiveChannel(channelId);
    this.channelsSignal.update((list) =>
      list.map((c) =>
        idsEqual(c.id, channelId) ? { ...c, unreadCount: 0, mentionCount: 0 } : c,
      ),
    );
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
  }

  /** Keep every channel/DM joined so unread badges can bump live (B-088). */
  private joinAllChannels(): void {
    if (this.usingDemo()) return;
    void this.hub.joinChannels(this.channelsSignal().map((c) => c.id));
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

  async createSpace(name: string): Promise<Space | null> {
    const workspace = this.activeWorkspace();
    const trimmed = name.trim();
    if (!workspace || !trimmed) return null;

    if (this.usingDemo() || this.auth.isOfflineDemo()) {
      const space: Space = {
        id: `sp-${crypto.randomUUID()}`,
        workspaceId: workspace.id,
        name: trimmed,
        order: this.spacesSignal().length,
      };
      this.spacesSignal.update((list) => [...list, space]);
      return space;
    }

    const space = await this.api.createSpace(workspace.id, trimmed);
    this.spacesSignal.update((list) => [...list, space]);
    return space;
  }

  async createChannel(input: {
    name: string;
    type?: string;
    spaceId?: string | null;
    newSpaceName?: string;
  }): Promise<Channel | null> {
    const workspace = this.activeWorkspace();
    const trimmed = input.name.trim();
    if (!workspace || !trimmed) return null;

    let spaceId = input.spaceId ?? null;
    if (input.newSpaceName?.trim()) {
      const space = await this.createSpace(input.newSpaceName.trim());
      spaceId = space?.id ?? null;
    }

    if (this.usingDemo() || this.auth.isOfflineDemo()) {
      const channel: Channel = {
        id: `ch-${crypto.randomUUID()}`,
        workspaceId: workspace.id,
        name: trimmed,
        unreadCount: 0,
        type: (input.type ?? 'public').toLowerCase(),
        isPrivate: (input.type ?? 'public').toLowerCase() === 'private',
        spaceId,
      };
      this.channelsSignal.update((list) => [...list, channel]);
      this.selectChannel(channel.id);
      return channel;
    }

    const channel = await this.api.createChannel(workspace.id, {
      name: trimmed,
      type: input.type ?? 'Public',
      spaceId,
    });
    this.channelsSignal.update((list) => [...list, channel]);
    void this.hub.joinChannel(channel.id);
    this.selectChannel(channel.id);
    return channel;
  }

  async openDirectMessage(userId: string): Promise<Channel | null> {
    const workspace = this.activeWorkspace();
    if (!workspace) return null;

    if (this.usingDemo() || this.auth.isOfflineDemo()) {
      const member = this.membersSignal().find((m) => m.userId === userId);
      const existing = this.channelsSignal().find((c) => c.isDirect && c.peerUserId === userId);
      if (existing) {
        this.selectChannel(existing.id);
        return existing;
      }
      const channel: Channel = {
        id: `dm-${userId}`,
        workspaceId: workspace.id,
        name: member?.displayName ?? 'DM',
        unreadCount: 0,
        isDirect: true,
        type: 'direct',
        peerUserId: userId,
        peerDisplayName: member?.displayName,
      };
      this.channelsSignal.update((list) => [...list, channel]);
      this.selectChannel(channel.id);
      return channel;
    }

    const channel = await this.api.openDirectMessage(workspace.id, userId);
    this.channelsSignal.update((list) => {
      if (list.some((c) => c.id === channel.id)) {
        return list.map((c) => (c.id === channel.id ? { ...c, ...channel } : c));
      }
      return [...list, channel];
    });
    void this.hub.joinChannel(channel.id);
    this.selectChannel(channel.id);
    return channel;
  }

  bumpUnread(channelId: string): void {
    this.channelsSignal.update((list) =>
      list.map((c) =>
        idsEqual(c.id, channelId) ? { ...c, unreadCount: c.unreadCount + 1 } : c,
      ),
    );
  }

  bumpMention(channelId: string): void {
    this.channelsSignal.update((list) =>
      list.map((c) =>
        idsEqual(c.id, channelId)
          ? { ...c, mentionCount: (c.mentionCount ?? 0) + 1, unreadCount: c.unreadCount + 1 }
          : c,
      ),
    );
  }

  mentionLabels(): Record<string, string> {
    return Object.fromEntries(this.membersSignal().map((m) => [m.userId, m.displayName]));
  }

  private seedDemo(): void {
    const workspace: Workspace = {
      id: 'ws-demo',
      name: 'Atlantic Ops',
      slug: 'atlantic-ops',
      role: 'Member',
    };
    this.workspacesSignal.set([workspace]);
    this.activeWorkspaceId.set(workspace.id);
    this.spacesSignal.set(this.demoSpaces(workspace.id));
    this.channelsSignal.set(this.demoChannels(workspace.id));
    this.membersSignal.set(this.demoMembers());
    this.presenceSignal.set({
      'u-alice': 'online',
      'u-bob': 'away',
    });
    this.selectChannel('ch-general');
  }

  private demoMembers(): WorkspaceMember[] {
    return [
      {
        userId: 'u-alice',
        displayName: 'Alice Mendes',
        email: 'alice@vibechat.local',
        role: 'Member',
      },
      {
        userId: 'u-bob',
        displayName: 'Bob Costa',
        email: 'bob@vibechat.local',
        role: 'Member',
      },
    ];
  }

  private demoSpaces(workspaceId: string): Space[] {
    return [
      { id: 'sp-geral', workspaceId, name: 'Geral', order: 0 },
      { id: 'sp-eng', workspaceId, name: 'Engenharia', order: 1 },
    ];
  }

  private demoChannels(workspaceId: string): Channel[] {
    return [
      {
        id: 'ch-general',
        workspaceId,
        name: 'geral',
        description: 'Pulso do workspace',
        unreadCount: 2,
        type: 'public',
        spaceId: 'sp-geral',
      },
      {
        id: 'ch-design',
        workspaceId,
        name: 'design-system',
        description: 'Tokens e UI',
        unreadCount: 0,
        type: 'public',
        spaceId: 'sp-eng',
      },
      {
        id: 'ch-ops',
        workspaceId,
        name: 'incidentes',
        description: 'War room calma',
        unreadCount: 5,
        isPrivate: true,
        type: 'private',
        spaceId: 'sp-eng',
      },
    ];
  }
}
