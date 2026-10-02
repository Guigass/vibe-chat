import { ApiService } from '../api/api.service';
import { ui } from '../i18n/strings';
import {
  Channel,
  ContactSection,
  PresenceStatus,
  Space,
  SpaceGroup,
  Workspace,
  WorkspaceMember,
} from '../../shared/models/chat.models';
import { demoChannels, demoContactSections, demoMembers, demoPresence, demoSpaces } from './channel-demo';
import { withoutSelfContacts } from './channel-contacts';

export interface ChannelDirectoryDeps {
  api: ApiService;
  isDemo: () => boolean;
  isOfflineDemo: () => boolean;
  activeWorkspace: () => Workspace | null;
  workspaces: () => readonly Workspace[];
  spaces: () => readonly Space[];
  channels: () => readonly Channel[];
  members: () => readonly WorkspaceMember[];
  peerCandidates: () => WorkspaceMember[];
  profileId: () => string | undefined;
  setActiveWorkspaceId: (id: string) => void;
  setSpaces: (spaces: Space[]) => void;
  setChannels: (channels: Channel[]) => void;
  updateChannels: (fn: (list: Channel[]) => Channel[]) => void;
  updateSpaces: (fn: (list: Space[]) => Space[]) => void;
  setMembers: (members: WorkspaceMember[]) => void;
  setContactSections: (sections: ContactSection[]) => void;
  setPresence: (presence: Record<string, PresenceStatus>) => void;
  setError: (message: string | null) => void;
  selectChannel: (channelId: string) => void;
  setActiveChannel: (channelId: string | null) => void;
  joinAllChannels: () => void;
  joinChannel: (channelId: string) => void;
  upsertChannel: (channel: Channel) => void;
}

export async function selectWorkspaceDirectory(
  deps: ChannelDirectoryDeps,
  workspaceId: string,
): Promise<void> {
  deps.setActiveWorkspaceId(workspaceId);
  try {
    if (deps.isDemo()) {
      deps.setSpaces(demoSpaces(workspaceId));
      deps.setChannels(demoChannels(workspaceId));
      deps.setMembers(demoMembers());
      deps.setContactSections(demoContactSections(deps.peerCandidates()));
      deps.setPresence(demoPresence());
    } else {
      const workspace = deps.workspaces().find((item) => item.id === workspaceId);
      const guest = workspace?.role === 'Guest';
      const [spaces, channels, members, presence, sections] = await Promise.all([
        guest ? Promise.resolve([]) : deps.api.getSpaces(workspaceId),
        deps.api.getChannels(workspaceId),
        guest ? Promise.resolve([]) : deps.api.getMembers(workspaceId),
        guest
          ? Promise.resolve({} as Record<string, PresenceStatus>)
          : deps.api.getPresence(workspaceId).catch(() => ({}) as Record<string, PresenceStatus>),
        guest ? Promise.resolve([]) : deps.api.getGroupedContacts(workspaceId).catch(() => []),
      ]);
      deps.setSpaces(spaces);
      deps.setChannels(channels);
      deps.setMembers(members);
      deps.setContactSections(withoutSelfContacts(sections, deps.profileId()));
      deps.setPresence(presence);
      deps.joinAllChannels();
    }
    const first = deps.channels()[0];
    if (first) deps.selectChannel(first.id);
    else deps.setActiveChannel(null);
  } catch (err) {
    deps.setError(err instanceof Error ? err.message : ui.errorLoadChannels);
  }
}

export async function createSpaceDirectory(deps: ChannelDirectoryDeps, name: string): Promise<Space | null> {
  const workspace = deps.activeWorkspace();
  const trimmed = name.trim();
  if (!workspace || !trimmed) return null;

  if (deps.isDemo() || deps.isOfflineDemo()) {
    const space: Space = {
      id: `sp-${crypto.randomUUID()}`,
      workspaceId: workspace.id,
      name: trimmed,
      order: deps.spaces().length,
    };
    deps.updateSpaces((list) => [...list, space]);
    return space;
  }

  const space = await deps.api.createSpace(workspace.id, trimmed);
  deps.updateSpaces((list) => [...list, space]);
  return space;
}

export async function createChannelDirectory(
  deps: ChannelDirectoryDeps,
  input: {
    name: string;
    type?: string;
    spaceId?: string | null;
    newSpaceName?: string;
  },
): Promise<Channel | null> {
  const workspace = deps.activeWorkspace();
  const trimmed = input.name.trim();
  if (!workspace || !trimmed) return null;

  let spaceId = input.spaceId ?? null;
  if (input.newSpaceName?.trim()) {
    const space = await createSpaceDirectory(deps, input.newSpaceName.trim());
    spaceId = space?.id ?? null;
  }

  if (deps.isDemo() || deps.isOfflineDemo()) {
    const channel: Channel = {
      id: `ch-${crypto.randomUUID()}`,
      workspaceId: workspace.id,
      name: trimmed,
      unreadCount: 0,
      type: (input.type ?? 'public').toLowerCase(),
      isPrivate: (input.type ?? 'public').toLowerCase() === 'private',
      spaceId,
    };
    deps.updateChannels((list) => [...list, channel]);
    deps.selectChannel(channel.id);
    return channel;
  }

  const channel = await deps.api.createChannel(workspace.id, {
    name: trimmed,
    type: input.type ?? 'Public',
    spaceId,
  });
  deps.updateChannels((list) => [...list, channel]);
  deps.joinChannel(channel.id);
  deps.selectChannel(channel.id);
  return channel;
}

export async function openDirectMessageDirectory(
  deps: ChannelDirectoryDeps,
  userId: string,
): Promise<Channel | null> {
  const workspace = deps.activeWorkspace();
  if (!workspace) return null;

  if (deps.isDemo() || deps.isOfflineDemo()) {
    const member = deps.members().find((m) => m.userId === userId);
    const existing = deps.channels().find((c) => c.isDirect && c.peerUserId === userId);
    if (existing) {
      deps.selectChannel(existing.id);
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
    deps.updateChannels((list) => [...list, channel]);
    deps.selectChannel(channel.id);
    return channel;
  }

  const channel = await deps.api.openDirectMessage(workspace.id, userId);
  deps.updateChannels((list) => {
    if (list.some((c) => c.id === channel.id)) {
      return list.map((c) => (c.id === channel.id ? { ...c, ...channel } : c));
    }
    return [...list, channel];
  });
  deps.joinChannel(channel.id);
  deps.selectChannel(channel.id);
  return channel;
}

export async function openGroupDmDirectory(
  deps: ChannelDirectoryDeps,
  userIds: string[],
  name?: string,
): Promise<Channel | null> {
  const workspace = deps.activeWorkspace();
  if (!workspace || userIds.length === 0) return null;

  if (deps.isDemo() || deps.isOfflineDemo()) {
    const members = deps.members().filter((m) => userIds.includes(m.userId));
    const label = members.map((m) => m.displayName).join(', ') || ui.groupFallback;
    const channel: Channel = {
      id: `gdm-${userIds.slice().sort().join('-')}`,
      workspaceId: workspace.id,
      name: name?.trim() || label,
      unreadCount: 0,
      isDirect: true,
      isGroupDm: true,
      type: 'groupdm',
      participantCount: userIds.length + 1,
      participantNames: members.map((m) => m.displayName),
    };
    deps.updateChannels((list) => {
      const existing = list.find((c) => c.id === channel.id);
      return existing ? list : [...list, channel];
    });
    deps.selectChannel(channel.id);
    return channel;
  }

  const channel = await deps.api.openGroupDm(workspace.id, userIds, name);
  deps.upsertChannel(channel);
  deps.joinChannel(channel.id);
  deps.selectChannel(channel.id);
  return channel;
}

export async function addGroupDmParticipantsDirectory(
  deps: ChannelDirectoryDeps,
  channelId: string,
  userIds: string[],
): Promise<Channel | null> {
  if (userIds.length === 0) return null;
  if (deps.isDemo() || deps.isOfflineDemo()) return null;
  const channel = await deps.api.addGroupDmParticipants(channelId, userIds);
  deps.upsertChannel(channel);
  deps.selectChannel(channel.id);
  return channel;
}

export async function leaveGroupDmDirectory(
  deps: ChannelDirectoryDeps,
  channelId: string,
): Promise<void> {
  if (!deps.isDemo() && !deps.isOfflineDemo()) {
    await deps.api.leaveGroupDm(channelId);
  }
  deps.updateChannels((list) => list.filter((c) => c.id !== channelId));
  const remaining = deps.channels();
  if (remaining[0]) {
    deps.selectChannel(remaining[0].id);
  } else {
    deps.setActiveChannel(null);
  }
}

export async function renameGroupDmDirectory(
  deps: ChannelDirectoryDeps,
  channelId: string,
  name: string,
): Promise<Channel | null> {
  if (deps.isDemo() || deps.isOfflineDemo()) {
    deps.updateChannels((list) => list.map((c) => (c.id === channelId ? { ...c, name } : c)));
    return deps.channels().find((c) => c.id === channelId) ?? null;
  }
  const channel = await deps.api.renameGroupDm(channelId, name);
  deps.upsertChannel(channel);
  return channel;
}

export function groupChannelsBySpace(
  spaces: readonly Space[],
  publicChannels: readonly Channel[],
): SpaceGroup[] {
  const ordered = [...spaces].sort((a, b) => a.order - b.order || a.name.localeCompare(b.name));
  const grouped: SpaceGroup[] = ordered.map((space) => ({
    space,
    channels: publicChannels.filter((c) => c.spaceId === space.id),
  }));
  const ungrouped = publicChannels.filter((c) => !c.spaceId || !ordered.some((s) => s.id === c.spaceId));
  if (ungrouped.length) {
    grouped.push({ space: null, channels: ungrouped });
  }
  return grouped.filter((g) => g.channels.length > 0 || g.space !== null);
}

export function upsertChannelList(list: readonly Channel[], channel: Channel): Channel[] {
  if (list.some((c) => c.id === channel.id)) {
    return list.map((c) => (c.id === channel.id ? { ...c, ...channel } : c));
  }
  return [...list, channel];
}
