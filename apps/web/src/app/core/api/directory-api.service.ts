import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import {
  Channel,
  ChannelRosterMember,
  ChannelRosterPage,
  PresenceStatus,
  Space,
  Workspace,
  WorkspaceMember,
  ContactGroup,
  ContactSection,
} from '../../shared/models/chat.models';
import {
  AvailabilityKind,
  MemberAvailability,
  UserStatusBody,
  UserStatusStateName,
} from '../../shared/status/user-status';
interface WorkspaceDto {
  id: string;
  name: string;
  slug: string;
  role?: string;
}
interface SpaceDto {
  id: string;
  workspaceId: string;
  name: string;
  order: number;
}

interface ChannelDto {
  id: string;
  workspaceId: string;
  name: string;
  type?: string;
  spaceId?: string | null;
  peerUserId?: string | null;
  peerDisplayName?: string | null;
  topic?: string | null;
  participantCount?: number | null;
  participantNames?: string[] | null;
  participantUserIds?: string[] | null;
  hasGuests?: boolean;
}

interface SlashCommandDto {
  name: string;
  description: string;
  usage: string;
  permission?: string | null;
}

interface PresenceDto {
  userId: string;
  status: string;
}

interface MemberDto {
  userId: string;
  displayName: string;
  email: string;
  role: string;
  avatarUrl?: string | null;
}

interface UserStatusResponseDto {
  presence?: string;
  availability?: string;
  status?: UserStatusBody | null;
}

interface MemberAvailabilityDto extends UserStatusResponseDto {
  userId: string;
}

function mapAvailability(userId: string, dto: UserStatusResponseDto): MemberAvailability {
  const presence = (dto.presence || 'offline').toLowerCase();
  const availability = (dto.availability || 'offline').toLowerCase();
  return {
    userId,
    presence: presence === 'online' || presence === 'away' ? presence : 'offline',
    availability: isAvailabilityKind(availability) ? availability : 'offline',
    status: dto.status ?? null,
  };
}

function isAvailabilityKind(value: string): value is AvailabilityKind {
  return value === 'available' || value === 'away' || value === 'busy' || value === 'vacation' || value === 'offline';
}

@Injectable({ providedIn: 'root' })
export class DirectoryApiService extends HttpApiClient {
  async getWorkspaces(): Promise<Workspace[]> {
    const rows = await this.request<WorkspaceDto[]>('/api/v1/workspaces');
    return rows.map((w) => ({ id: w.id, name: w.name, slug: w.slug, role: w.role }));
  }

  async getSpaces(workspaceId: string): Promise<Space[]> {
    const rows = await this.request<SpaceDto[]>(`/api/v1/workspaces/${workspaceId}/spaces`);
    return rows.map((s) => ({
      id: s.id,
      workspaceId: s.workspaceId,
      name: s.name,
      order: s.order ?? 0,
    }));
  }

  async createSpace(workspaceId: string, name: string): Promise<Space> {
    const dto = await this.request<SpaceDto>(`/api/v1/workspaces/${workspaceId}/spaces`, {
      method: 'POST',
      body: JSON.stringify({ name }),
    });
    return {
      id: dto.id,
      workspaceId: dto.workspaceId,
      name: dto.name,
      order: dto.order ?? 0,
    };
  }

  async getChannels(workspaceId: string): Promise<Channel[]> {
    const rows = await this.request<ChannelDto[]>(`/api/v1/workspaces/${workspaceId}/channels`);
    return rows.map((c) => this.mapChannel(c));
  }

  async createChannel(
    workspaceId: string,
    input: { name: string; type: string; spaceId?: string | null },
  ): Promise<Channel> {
    const dto = await this.request<ChannelDto>(`/api/v1/workspaces/${workspaceId}/channels`, {
      method: 'POST',
      body: JSON.stringify({
        name: input.name,
        type: input.type,
        spaceId: input.spaceId ?? null,
      }),
    });
    return this.mapChannel(dto);
  }

  async updateChannelTopic(
    workspaceId: string,
    channelId: string,
    topic: string,
  ): Promise<Channel> {
    const dto = await this.request<ChannelDto>(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/topic`,
      {
        method: 'PUT',
        body: JSON.stringify({ topic }),
      },
    );
    return this.mapChannel(dto);
  }

  async getCommands(workspaceId: string): Promise<
    { name: string; description: string; usage: string; permission?: string | null }[]
  > {
    const rows = await this.request<SlashCommandDto[]>(
      `/api/v1/workspaces/${workspaceId}/commands`,
    );
    return rows.map((row) => ({
      name: row.name,
      description: row.description,
      usage: row.usage,
      permission: row.permission ?? null,
    }));
  }

  async getMembers(workspaceId: string): Promise<WorkspaceMember[]> {
    const rows = await this.request<MemberDto[]>(`/api/v1/workspaces/${workspaceId}/members`);
    return rows.map((m) => ({
      userId: m.userId,
      displayName: m.displayName,
      email: m.email,
      role: m.role,
      avatarUrl: m.avatarUrl ?? null,
    }));
  }

  async getContactGroups(workspaceId: string): Promise<ContactGroup[]> {
    const rows = await this.request<ContactGroup[]>(`/api/v1/workspaces/${workspaceId}/contact-groups`);
    return rows.map((row) => ({
      id: row.id,
      workspaceId: row.workspaceId,
      kind: row.kind,
      name: row.name,
      order: row.order,
      ownerUserId: row.ownerUserId,
      memberUserIds: row.memberUserIds ?? [],
    }));
  }

  async createContactGroup(
    workspaceId: string,
    input: { name: string; kind: 'department' | 'personal'; order?: number },
  ): Promise<ContactGroup> {
    return this.request<ContactGroup>(`/api/v1/workspaces/${workspaceId}/contact-groups`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify({ name: input.name, kind: input.kind, order: input.order ?? null }),
    });
  }

  async updateContactGroup(
    workspaceId: string,
    groupId: string,
    input: { name?: string; order?: number },
  ): Promise<ContactGroup> {
    return this.request<ContactGroup>(`/api/v1/workspaces/${workspaceId}/contact-groups/${groupId}`, {
      method: 'PATCH',
      body: JSON.stringify(input),
    });
  }

  async deleteContactGroup(workspaceId: string, groupId: string): Promise<void> {
    await this.request(`/api/v1/workspaces/${workspaceId}/contact-groups/${groupId}`, {
      method: 'DELETE',
    });
  }

  async replaceContactGroupMembers(
    workspaceId: string,
    groupId: string,
    userIds: string[],
  ): Promise<ContactGroup> {
    return this.request<ContactGroup>(
      `/api/v1/workspaces/${workspaceId}/contact-groups/${groupId}/members`,
      {
        method: 'PUT',
        headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: JSON.stringify({ userIds }),
      },
    );
  }

  async getGroupedContacts(workspaceId: string): Promise<ContactSection[]> {
    const rows = await this.request<ContactSection[]>(
      `/api/v1/workspaces/${workspaceId}/contacts?grouped=true`,
    );
    return rows.map((row) => ({
      groupId: row.groupId,
      name: row.name,
      kind: row.kind,
      members: (row.members ?? []).map((member) => ({
        userId: member.userId,
        displayName: member.displayName,
        email: member.email,
        role: member.role,
      })),
    }));
  }

  async getAssignableRoles(workspaceId: string): Promise<string[]> {
    const result = await this.request<{ assignableRoles: string[] }>(
      `/api/v1/workspaces/${workspaceId}/roles`,
    );
    return result.assignableRoles ?? [];
  }

  async updateMemberRole(
    workspaceId: string,
    userId: string,
    role: string,
  ): Promise<WorkspaceMember> {
    const dto = await this.request<MemberDto>(
      `/api/v1/workspaces/${workspaceId}/members/${userId}/role`,
      {
        method: 'PUT',
        body: JSON.stringify({ role }),
      },
    );
    return {
      userId: dto.userId,
      displayName: dto.displayName,
      email: dto.email,
      role: dto.role,
    };
  }

  async inviteMember(
    workspaceId: string,
    input: { email: string; displayName?: string; role?: string },
  ): Promise<WorkspaceMember> {
    const dto = await this.request<MemberDto>(`/api/v1/workspaces/${workspaceId}/members`, {
      method: 'POST',
      body: JSON.stringify({
        email: input.email,
        displayName: input.displayName ?? null,
        role: input.role ?? 'Member',
      }),
    });
    return {
      userId: dto.userId,
      displayName: dto.displayName,
      email: dto.email,
      role: dto.role,
    };
  }

  async getPresence(workspaceId: string): Promise<Record<string, PresenceStatus>> {
    const rows = await this.request<PresenceDto[]>(`/api/v1/workspaces/${workspaceId}/presence`);
    const map: Record<string, PresenceStatus> = {};
    for (const row of rows) {
      const status = (row.status || 'offline').toLowerCase();
      map[row.userId] =
        status === 'online' || status === 'away' ? status : 'offline';
    }
    return map;
  }

  async getMyStatus(): Promise<MemberAvailability> {
    const dto = await this.request<UserStatusResponseDto>('/api/v1/me/status');
    const me = this.auth.profile()?.id ?? '';
    return mapAvailability(me, dto);
  }

  async setMyStatus(input: {
    state: UserStatusStateName;
    emoji: string;
    text: string;
    clearAtEndOfDay: boolean;
    expiresAt: string | null;
  }): Promise<MemberAvailability> {
    const dto = await this.request<UserStatusResponseDto>('/api/v1/me/status', {
      method: 'PUT',
      body: JSON.stringify({
        state: input.state,
        emoji: input.emoji,
        text: input.text,
        clearAtEndOfDay: input.clearAtEndOfDay,
        expiresAt: input.expiresAt,
      }),
    });
    const me = this.auth.profile()?.id ?? '';
    return mapAvailability(me, dto);
  }

  async clearMyStatus(): Promise<MemberAvailability> {
    const dto = await this.request<UserStatusResponseDto>('/api/v1/me/status', { method: 'DELETE' });
    const me = this.auth.profile()?.id ?? '';
    return mapAvailability(me, dto);
  }

  async getWorkspaceAvailability(workspaceId: string): Promise<MemberAvailability[]> {
    const rows = await this.request<MemberAvailabilityDto[]>(
      `/api/v1/workspaces/${workspaceId}/availability`,
    );
    return rows.map((row) => mapAvailability(row.userId, row));
  }

  async reportUserStatus(workspaceId: string, userId: string): Promise<void> {
    await this.request<void>(
      `/api/v1/workspaces/${workspaceId}/members/${userId}/status/report`,
      { method: 'POST' },
    );
  }

  async clearMemberStatus(workspaceId: string, userId: string): Promise<void> {
    await this.request<void>(`/api/v1/workspaces/${workspaceId}/members/${userId}/status`, {
      method: 'DELETE',
    });
  }

  async openDirectMessage(workspaceId: string, userId: string): Promise<Channel> {
    const dto = await this.request<ChannelDto>(`/api/v1/workspaces/${workspaceId}/dms`, {
      method: 'POST',
      body: JSON.stringify({ userId }),
    });
    return this.mapChannel(dto);
  }

  async openGroupDm(workspaceId: string, userIds: string[], name?: string): Promise<Channel> {
    const dto = await this.request<ChannelDto>(`/api/v1/workspaces/${workspaceId}/group-dms`, {
      method: 'POST',
      body: JSON.stringify({ userIds, name: name ?? null }),
    });
    return this.mapChannel(dto);
  }

  async addGroupDmParticipants(channelId: string, userIds: string[]): Promise<Channel> {
    const dto = await this.request<ChannelDto>(`/api/v1/channels/${channelId}/participants`, {
      method: 'POST',
      body: JSON.stringify({ userIds }),
    });
    return this.mapChannel(dto);
  }

  async leaveGroupDm(channelId: string): Promise<void> {
    await this.request<void>(`/api/v1/channels/${channelId}/participants/me`, {
      method: 'DELETE',
    });
  }

  async renameGroupDm(channelId: string, name: string): Promise<Channel> {
    const dto = await this.request<ChannelDto>(`/api/v1/channels/${channelId}`, {
      method: 'PATCH',
      body: JSON.stringify({ name }),
    });
    return this.mapChannel(dto);
  }

  async getChannelMembers(
    workspaceId: string,
    channelId: string,
    query = '',
  ): Promise<Array<{ userId: string; displayName: string; email: string }>> {
    const params = `?query=${encodeURIComponent(query)}`;
    const rows = await this.request<Array<{ userId: string; displayName: string; email: string }>>(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/members${params}`,
    );
    return rows.map((row) => ({
      userId: String(row.userId),
      displayName: row.displayName,
      email: row.email,
    }));
  }

  async getChannelRoster(
    workspaceId: string,
    channelId: string,
    options?: { cursor?: string; limit?: number },
  ): Promise<ChannelRosterPage> {
    const params = new URLSearchParams();
    if (options?.cursor) {
      params.set('cursor', options.cursor);
    }
    if (options?.limit) {
      params.set('limit', String(options.limit));
    }
    const queryString = params.toString();
    const query = queryString ? `?${queryString}` : '';
    const page = await this.request<ChannelRosterPage>(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/members${query}`,
    );
    return {
      items: (page.items ?? []).map((item) => this.mapRosterMember(item)),
      nextCursor: page.nextCursor ?? null,
      total: page.total ?? 0,
      canManage: !!page.canManage,
    };
  }

  async addChannelMember(workspaceId: string, channelId: string, userId: string): Promise<void> {
    await this.request(`/api/v1/workspaces/${workspaceId}/channels/${channelId}/members`, {
      method: 'POST',
      body: JSON.stringify({ userId }),
    });
  }

  async removeChannelMember(workspaceId: string, channelId: string, userId: string): Promise<void> {
    await this.request(`/api/v1/workspaces/${workspaceId}/channels/${channelId}/members/${userId}`, {
      method: 'DELETE',
    });
  }

  async leaveChannel(workspaceId: string, channelId: string): Promise<void> {
    await this.request(`/api/v1/workspaces/${workspaceId}/channels/${channelId}/members/me`, {
      method: 'DELETE',
    });
  }

  private mapRosterMember(item: ChannelRosterMember): ChannelRosterMember {
    const presence = item.presence === 'online' || item.presence === 'away' || item.presence === 'offline'
      ? item.presence
      : null;
    return {
      userId: String(item.userId),
      displayName: item.displayName,
      email: item.email ?? '',
      joinedAt: item.joinedAt ?? null,
      presence,
      isGuest: !!item.isGuest,
    };
  }

  private mapChannel(c: ChannelDto): Channel {
    const type = (c.type ?? 'Public').toLowerCase();
    const isGroupDm = type === 'groupdm';
    return {
      id: c.id,
      workspaceId: c.workspaceId,
      name: c.name,
      description: c.topic ?? undefined,
      unreadCount: 0,
      mentionCount: 0,
      type,
      spaceId: c.spaceId ?? null,
      isPrivate: type === 'private',
      isDirect: type === 'direct' || isGroupDm,
      isGroupDm,
      peerUserId: c.peerUserId ?? undefined,
      peerDisplayName: c.peerDisplayName ?? undefined,
      participantCount: c.participantCount ?? undefined,
      participantNames: c.participantNames ?? undefined,
      participantUserIds: c.participantUserIds ?? undefined,
      hasGuests: !!c.hasGuests,
    };
  }

  async createChannelInvite(
    workspaceId: string,
    channelId: string,
    input: { email?: string; expiresInDays?: number },
  ): Promise<{ id: string; url: string; expiresAt: string }> {
    return this.request<{ id: string; url: string; expiresAt: string }>(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/invites`,
      {
        method: 'POST',
        body: JSON.stringify({
          email: input.email || null,
          expiresInDays: input.expiresInDays ?? 7,
        }),
      },
    );
  }

  async acceptChannelInvite(token: string): Promise<{ channelId: string; workspaceId: string; channelName: string }> {
    return this.request<{ channelId: string; workspaceId: string; channelName: string }>(
      `/api/v1/invites/${encodeURIComponent(token)}/accept`,
      { method: 'POST' },
    );
  }
}
