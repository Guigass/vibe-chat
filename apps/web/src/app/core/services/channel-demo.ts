import {
  Channel,
  ContactSection,
  PresenceStatus,
  Space,
  Workspace,
  WorkspaceMember,
} from '../../shared/models/chat.models';

export function demoWorkspace(): Workspace {
  return {
    id: 'ws-demo',
    name: 'Atlantic Ops',
    slug: 'atlantic-ops',
    role: 'Member',
  };
}

export function demoMembers(): WorkspaceMember[] {
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

export function demoSpaces(workspaceId: string): Space[] {
  return [
    { id: 'sp-geral', workspaceId, name: 'Geral', order: 0 },
    { id: 'sp-eng', workspaceId, name: 'Engenharia', order: 1 },
  ];
}

export function demoChannels(workspaceId: string): Channel[] {
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

export function demoContactSections(members: WorkspaceMember[]): ContactSection[] {
  return [{ groupId: null, name: null, kind: null, members }];
}

export function demoPresence(): Record<string, PresenceStatus> {
  return {
    'u-alice': 'online',
    'u-bob': 'away',
  };
}
