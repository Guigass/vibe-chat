import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { Channel, Workspace, WorkspaceMember } from '../../shared/models/chat.models';

@Injectable({ providedIn: 'root' })
export class ChannelStore {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  private readonly workspacesSignal = signal<Workspace[]>([]);
  private readonly channelsSignal = signal<Channel[]>([]);
  private readonly membersSignal = signal<WorkspaceMember[]>([]);
  private readonly activeWorkspaceId = signal<string | null>(null);
  private readonly activeChannelId = signal<string | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly usingDemo = signal(false);

  readonly workspaces = this.workspacesSignal.asReadonly();
  readonly channels = this.channelsSignal.asReadonly();
  readonly members = this.membersSignal.asReadonly();
  readonly activeWorkspace = computed(
    () => this.workspacesSignal().find((w) => w.id === this.activeWorkspaceId()) ?? null,
  );
  readonly activeChannel = computed(
    () => this.channelsSignal().find((c) => c.id === this.activeChannelId()) ?? null,
  );
  readonly publicChannels = computed(() =>
    this.channelsSignal().filter((c) => !c.isDirect),
  );
  readonly directChannels = computed(() =>
    this.channelsSignal().filter((c) => !!c.isDirect),
  );
  readonly peerCandidates = computed(() => {
    const me = this.auth.profile()?.id;
    return this.membersSignal().filter((m) => m.userId !== me);
  });
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly isDemo = this.usingDemo.asReadonly();

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
    const updated = await Promise.all(
      channels.map(async (channel) => {
        try {
          const unreadCount = await this.api.getUnreadCount(channel.id);
          return { ...channel, unreadCount };
        } catch {
          return channel;
        }
      }),
    );
    this.channelsSignal.set(updated);
  }

  async selectWorkspace(workspaceId: string): Promise<void> {
    this.activeWorkspaceId.set(workspaceId);
    try {
      if (this.usingDemo()) {
        this.channelsSignal.set(this.demoChannels(workspaceId));
        this.membersSignal.set(this.demoMembers());
      } else {
        const [channels, members] = await Promise.all([
          this.api.getChannels(workspaceId),
          this.api.getMembers(workspaceId),
        ]);
        this.channelsSignal.set(channels);
        this.membersSignal.set(members);
      }
      const first = this.channelsSignal()[0];
      this.activeChannelId.set(first?.id ?? null);
    } catch (err) {
      this.errorSignal.set(err instanceof Error ? err.message : 'Falha ao carregar channels');
    }
  }

  selectChannel(channelId: string): void {
    this.activeChannelId.set(channelId);
    this.channelsSignal.update((list) =>
      list.map((c) => (c.id === channelId ? { ...c, unreadCount: 0 } : c)),
    );
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
    this.selectChannel(channel.id);
    return channel;
  }

  bumpUnread(channelId: string): void {
    if (channelId === this.activeChannelId()) return;
    this.channelsSignal.update((list) =>
      list.map((c) =>
        c.id === channelId ? { ...c, unreadCount: c.unreadCount + 1 } : c,
      ),
    );
  }

  private seedDemo(): void {
    const workspace: Workspace = {
      id: 'ws-demo',
      name: 'Atlantic Ops',
      slug: 'atlantic-ops',
    };
    this.workspacesSignal.set([workspace]);
    this.activeWorkspaceId.set(workspace.id);
    this.channelsSignal.set(this.demoChannels(workspace.id));
    this.membersSignal.set(this.demoMembers());
    this.activeChannelId.set('ch-general');
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

  private demoChannels(workspaceId: string): Channel[] {
    return [
      {
        id: 'ch-general',
        workspaceId,
        name: 'geral',
        description: 'Pulso do workspace',
        unreadCount: 2,
        type: 'public',
      },
      {
        id: 'ch-design',
        workspaceId,
        name: 'design-system',
        description: 'Tokens e UI',
        unreadCount: 0,
        type: 'public',
      },
      {
        id: 'ch-ops',
        workspaceId,
        name: 'incidentes',
        description: 'War room calma',
        unreadCount: 5,
        isPrivate: true,
        type: 'private',
      },
    ];
  }
}
