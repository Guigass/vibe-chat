import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';
import {
  AdminConversationItem,
  AdminConversationMessageItem,
  AdminStats,
  AuditEventItem,
  AiSuggestReplyResult,
  AiSummaryResult,
  Channel,
  ChatMessage,
  ChatThread,
  MessageAttachment,
  PresenceStatus,
  ReactionSummary,
  CredentialRotateResult,
  ReencryptSettingsResult,
  RotateCredentialInput,
  SearchMessagesResult,
  SensitiveSettings,
  Space,
  UpdateSensitiveSettingsInput,
  Workspace,
  WorkspaceMember,
  MessageLinkPreview,
} from '../../shared/models/chat.models';

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
}

interface AttachmentDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  status?: string;
  kind?: string;
  durationMs?: number;
  waveform?: number[];
  thumbnailStatus?: string | null;
  width?: number | null;
  height?: number | null;
  pageCount?: number | null;
}

interface ReactionSummaryDto {
  emoji: string;
  count: number;
  me: boolean;
}

interface ReplyToDto {
  messageId: string;
  authorName: string;
  preview: string;
  deleted: boolean;
}

interface ForwardedFromDto {
  messageId: string;
  channelId: string;
  channelName: string;
  authorName: string;
  createdAt: string;
  isDirect?: boolean;
}

interface LinkPreviewDto {
  id: string;
  url: string;
  title?: string | null;
  description?: string | null;
  siteName?: string | null;
  hasImage: boolean;
  status: string;
}

interface ChannelMessagesDto {
  messages: MessageDto[];
  hasMoreBefore: boolean;
  hasMoreAfter: boolean;
}

export interface ChannelMessagesPage {
  messages: ChatMessage[];
  hasMoreBefore: boolean;
  hasMoreAfter: boolean;
}

interface MessageDto {
  id: string;
  channelId: string;
  conversationId?: string | null;
  sequence: number;
  authorId: string;
  authorName?: string;
  body: string;
  createdAt: string;
  editedAt?: string | null;
  deletedAt?: string | null;
  attachments?: AttachmentDto[] | null;
  threadId?: string | null;
  replyToMessageId?: string | null;
  replyTo?: ReplyToDto | null;
  forwardedFromMessageId?: string | null;
  forwardedFromChannelId?: string | null;
  forwardedFrom?: ForwardedFromDto | null;
  replyCount?: number;
  reactions?: ReactionSummaryDto[] | null;
  linkPreview?: LinkPreviewDto | null;
}

interface ForwardMessageResponseDto {
  messages: MessageDto[];
}

interface ToggleReactionDto {
  messageId: string;
  channelId: string;
  emoji: string;
  added: boolean;
  reactions: ReactionSummaryDto[];
}

interface ThreadDto {
  id: string;
  channelId: string;
  parentMessageId: string;
  createdBy: string;
  createdAt: string;
  replyCount: number;
  parentMessage?: MessageDto | null;
}

interface AttachmentUploadDto {
  attachmentId: string;
  uploadUrl: string;
  expiresAt: string;
  requiredHeaders: Record<string, string>;
  maxSizeBytes: number;
  fileName: string;
  contentType: string;
}

interface AttachmentDownloadDto {
  attachmentId: string;
  downloadUrl: string;
  expiresAt: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

interface LinkPreviewImageDto {
  messageId?: string;
  downloadUrl: string;
  expiresAt: string;
  contentType: string;
}

interface AdminDashboardDto {
  users: number;
  onlineUsers: number;
  workspaces: number;
  channels: number;
  messages: number;
  realtimeConnections: number;
  outboxPending: number;
  processingFailures: number;
  health: {
    postgres: 'up' | 'down' | 'degraded';
    redis: 'up' | 'down' | 'degraded';
    storage: 'up' | 'down' | 'degraded';
  };
  appVersion: string;
  grafanaUrl: string;
}

interface AuditEventDto {
  id: string;
  action: string;
  entityType: string;
  entityId?: string | null;
  actorUserId?: string | null;
  occurredAt: string;
  metadataJson?: string;
}

interface AdminConversationDto {
  id: string;
  workspaceId: string;
  name: string;
  type: string;
  spaceId?: string | null;
  peerUserId?: string | null;
  peerDisplayName?: string | null;
}

interface AdminConversationMessageDto {
  id: string;
  channelId: string;
  conversationId: string;
  sequence: number;
  authorId: string;
  authorName: string;
  body: string;
  createdAt: string;
  editedAt?: string | null;
  deletedAt?: string | null;
  deletedBy?: string | null;
  deletedByName?: string | null;
  threadId?: string | null;
  replyToMessageId?: string | null;
  replyCount?: number;
  attachments?: AttachmentDto[];
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly auth = inject(AuthService);
  private readonly baseUrl = environment.apiUrl;

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

  async openDirectMessage(workspaceId: string, userId: string): Promise<Channel> {
    const dto = await this.request<ChannelDto>(`/api/v1/workspaces/${workspaceId}/dms`, {
      method: 'POST',
      body: JSON.stringify({ userId }),
    });
    return this.mapChannel(dto);
  }

  async getMessages(
    channelId: string,
    options: { take?: number; after?: number; before?: number; around?: number } = {},
  ): Promise<ChannelMessagesPage> {
    const take = options.take ?? 50;
    const params = new URLSearchParams({ limit: String(take) });
    if (options.after !== undefined) params.set('after', String(options.after));
    if (options.before !== undefined) params.set('before', String(options.before));
    if (options.around !== undefined) params.set('around', String(options.around));
    const dto = await this.request<ChannelMessagesDto>(
      `/api/v1/channels/${channelId}/messages?${params.toString()}`,
    );
    const me = this.auth.profile()?.id;
    return {
      messages: dto.messages.map((m) => this.mapMessage(m, me)),
      hasMoreBefore: dto.hasMoreBefore,
      hasMoreAfter: dto.hasMoreAfter,
    };
  }

  async sendMessage(input: {
    channelId: string;
    body: string;
    clientMessageId: string;
    idempotencyKey: string;
    attachmentIds?: string[];
    replyToMessageId?: string;
  }): Promise<ChatMessage> {
    const dto = await this.request<MessageDto>(`/api/v1/channels/${input.channelId}/messages`, {
      method: 'POST',
      body: JSON.stringify({
        messageId: input.clientMessageId,
        idempotencyKey: input.idempotencyKey,
        body: input.body,
        attachmentIds: input.attachmentIds ?? [],
        replyToMessageId: input.replyToMessageId ?? null,
      }),
    });
    return this.mapMessage(dto, this.auth.profile()?.id);
  }

  async forwardMessage(input: {
    workspaceId: string;
    messageId: string;
    targetChannelIds: string[];
    comment?: string;
    idempotencyKey: string;
  }): Promise<ChatMessage[]> {
    const dto = await this.request<ForwardMessageResponseDto>(
      `/api/v1/workspaces/${input.workspaceId}/messages/${input.messageId}/forward`,
      {
        method: 'POST',
        body: JSON.stringify({
          targetChannelIds: input.targetChannelIds,
          comment: input.comment ?? null,
          idempotencyKey: input.idempotencyKey,
        }),
      },
    );
    const me = this.auth.profile()?.id;
    return (dto.messages ?? []).map((m) => this.mapMessage(m, me));
  }

  async openThread(channelId: string, messageId: string): Promise<ChatThread> {
    const dto = await this.request<ThreadDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}/threads`,
      { method: 'POST', body: '{}' },
    );
    return this.mapThread(dto);
  }

  async getThread(threadId: string): Promise<ChatThread> {
    const dto = await this.request<ThreadDto>(`/api/v1/threads/${threadId}`);
    return this.mapThread(dto);
  }

  async getThreadMessages(threadId: string, take = 50): Promise<ChatMessage[]> {
    const rows = await this.request<MessageDto[]>(
      `/api/v1/threads/${threadId}/messages?limit=${take}`,
    );
    const me = this.auth.profile()?.id;
    return rows.map((m) => this.mapMessage(m, me));
  }

  async sendThreadMessage(input: {
    threadId: string;
    body: string;
    clientMessageId: string;
    idempotencyKey: string;
    replyToMessageId?: string;
  }): Promise<ChatMessage> {
    const dto = await this.request<MessageDto>(`/api/v1/threads/${input.threadId}/messages`, {
      method: 'POST',
      body: JSON.stringify({
        messageId: input.clientMessageId,
        idempotencyKey: input.idempotencyKey,
        body: input.body,
        replyToMessageId: input.replyToMessageId ?? null,
        threadId: input.threadId,
      }),
    });
    return this.mapMessage(dto, this.auth.profile()?.id);
  }

  async initiateAttachmentUpload(input: {
    channelId: string;
    fileName: string;
    contentType: string;
    sizeBytes: number;
    kind?: 'File' | 'Audio';
    durationMs?: number;
    waveform?: number[];
  }): Promise<AttachmentUploadDto> {
    return this.request<AttachmentUploadDto>(`/api/v1/channels/${input.channelId}/attachments`, {
      method: 'POST',
      body: JSON.stringify({
        fileName: input.fileName,
        contentType: input.contentType,
        sizeBytes: input.sizeBytes,
        kind: input.kind,
        durationMs: input.durationMs,
        waveform: input.waveform,
      }),
    });
  }

  async completeAttachmentUpload(channelId: string, attachmentId: string): Promise<MessageAttachment> {
    const dto = await this.request<AttachmentDto>(
      `/api/v1/channels/${channelId}/attachments/${attachmentId}/complete`,
      { method: 'POST', body: '{}' },
    );
    return this.mapAttachment(dto);
  }

  async transcribeAttachment(input: {
    workspaceId: string;
    channelId: string;
    messageId: string;
    attachmentId: string;
  }): Promise<{ text: string; language: string; provider: string }> {
    return this.request(`/api/v1/workspaces/${input.workspaceId}/channels/${input.channelId}/messages/${input.messageId}/attachments/${input.attachmentId}/transcribe`, {
      method: 'POST',
      body: '{}',
    });
  }

  async getAttachmentDownload(
    channelId: string,
    attachmentId: string,
  ): Promise<AttachmentDownloadDto> {
    return this.request<AttachmentDownloadDto>(
      `/api/v1/channels/${channelId}/attachments/${attachmentId}/download`,
    );
  }

  async getAttachmentThumbnail(
    channelId: string,
    attachmentId: string,
  ): Promise<AttachmentDownloadDto> {
    return this.request<AttachmentDownloadDto>(
      `/api/v1/channels/${channelId}/attachments/${attachmentId}/thumbnail`,
    );
  }

  async deleteMessageLinkPreview(channelId: string, messageId: string): Promise<void> {
    await this.request(`/api/v1/channels/${channelId}/messages/${messageId}/link-preview`, {
      method: 'DELETE',
    });
  }

  async getLinkPreviewImage(
    channelId: string,
    messageId: string,
  ): Promise<LinkPreviewImageDto> {
    return this.request<LinkPreviewImageDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}/link-preview/image`,
    );
  }

  async uploadFileToPresignedUrl(
    uploadUrl: string,
    file: File,
    requiredHeaders: Record<string, string>,
    onProgress?: (percent: number) => void,
    signal?: AbortSignal,
  ): Promise<void> {
    await new Promise<void>((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open('PUT', uploadUrl);
      for (const [key, value] of Object.entries(requiredHeaders ?? {})) {
        xhr.setRequestHeader(key, value);
      }
      xhr.upload.onprogress = (event) => {
        if (!event.lengthComputable) return;
        onProgress?.(Math.round((event.loaded / event.total) * 100));
      };
      xhr.onload = () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          onProgress?.(100);
          resolve();
          return;
        }
        reject(new Error(`Upload failed (${xhr.status})`));
      };
      xhr.onerror = () => reject(new Error('Upload failed (network)'));
      xhr.onabort = () => reject(new DOMException('Upload aborted', 'AbortError'));
      signal?.addEventListener('abort', () => xhr.abort(), { once: true });
      xhr.send(file);
    });
  }

  async editMessage(channelId: string, messageId: string, body: string): Promise<ChatMessage> {
    const dto = await this.request<MessageDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}`,
      {
        method: 'PUT',
        body: JSON.stringify({ body }),
      },
    );
    return this.mapMessage(dto, this.auth.profile()?.id);
  }

  async deleteMessage(channelId: string, messageId: string): Promise<void> {
    await this.request(`/api/v1/channels/${channelId}/messages/${messageId}`, {
      method: 'DELETE',
    });
  }

  async toggleReaction(
    channelId: string,
    messageId: string,
    emoji: string,
  ): Promise<{ messageId: string; channelId: string; emoji: string; added: boolean; reactions: ReactionSummary[] }> {
    const dto = await this.request<ToggleReactionDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}/reactions`,
      {
        method: 'PUT',
        body: JSON.stringify({ emoji }),
      },
    );
    return {
      messageId: dto.messageId,
      channelId: dto.channelId,
      emoji: dto.emoji,
      added: dto.added,
      reactions: (dto.reactions ?? []).map((r) => ({
        emoji: r.emoji,
        count: r.count,
        me: !!r.me,
      })),
    };
  }

  async getReactionUsers(
    channelId: string,
    messageId: string,
    emoji: string,
  ): Promise<{ emoji: string; users: Array<{ userId: string; displayName: string }>; total: number }> {
    const dto = await this.request<{
      emoji: string;
      users: Array<{ userId: string; displayName: string }>;
      total: number;
    }>(
      `/api/v1/channels/${channelId}/messages/${messageId}/reactions/${encodeURIComponent(emoji)}/users`,
    );
    return {
      emoji: dto.emoji,
      users: (dto.users ?? []).map((user) => ({
        userId: String(user.userId),
        displayName: user.displayName,
      })),
      total: dto.total ?? dto.users?.length ?? 0,
    };
  }

  async upsertReadCursor(channelId: string, lastReadSequence: number): Promise<void> {
    await this.request(`/api/v1/channels/${channelId}/read-cursor`, {
      method: 'PUT',
      body: JSON.stringify({ lastReadSequence }),
    });
  }

  async getUnreadCount(channelId: string): Promise<{ unreadCount: number; mentionCount: number }> {
    return this.request<{ unreadCount: number; mentionCount: number }>(
      `/api/v1/channels/${channelId}/unread-count`,
    );
  }

  async getChannelMembers(
    workspaceId: string,
    channelId: string,
    query = '',
  ): Promise<Array<{ userId: string; displayName: string; email: string }>> {
    const params = query ? `?query=${encodeURIComponent(query)}` : '';
    const rows = await this.request<Array<{ userId: string; displayName: string; email: string }>>(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/members${params}`,
    );
    return rows.map((row) => ({
      userId: String(row.userId),
      displayName: row.displayName,
      email: row.email,
    }));
  }

  async searchMessages(input: {
    workspaceId: string;
    q: string;
    channelId?: string;
    limit?: number;
  }): Promise<SearchMessagesResult> {
    const params = new URLSearchParams({
      workspaceId: input.workspaceId,
      q: input.q,
    });
    if (input.channelId) {
      params.set('channelId', input.channelId);
    }
    if (input.limit) {
      params.set('limit', String(input.limit));
    }
    return this.request<SearchMessagesResult>(`/api/v1/search/messages?${params.toString()}`);
  }

  async getAdminStats(): Promise<AdminStats> {
    return this.request<AdminDashboardDto>('/api/v1/admin/dashboard');
  }

  async getAdminAuditEvents(limit = 40): Promise<AuditEventItem[]> {
    const result = await this.request<{ items: AuditEventDto[] }>(
      `/api/v1/admin/audit-events?limit=${limit}`,
    );
    return (result.items ?? []).map((row) => ({
      id: row.id,
      action: row.action,
      entityType: row.entityType,
      entityId: row.entityId ?? null,
      actorUserId: row.actorUserId ?? null,
      occurredAt: row.occurredAt,
      metadataJson: row.metadataJson ?? '{}',
    }));
  }

  async getAdminConversations(input?: {
    workspaceId?: string;
    limit?: number;
  }): Promise<AdminConversationItem[]> {
    const params = new URLSearchParams();
    if (input?.workspaceId) {
      params.set('workspaceId', input.workspaceId);
    }
    if (input?.limit) {
      params.set('limit', String(input.limit));
    }
    const query = params.toString();
    const result = await this.request<{ items: AdminConversationDto[] }>(
      `/api/v1/admin/conversations${query ? `?${query}` : ''}`,
    );
    return (result.items ?? []).map((row) => ({
      id: row.id,
      workspaceId: row.workspaceId,
      name: row.name,
      type: row.type,
      spaceId: row.spaceId ?? null,
      peerUserId: row.peerUserId ?? null,
      peerDisplayName: row.peerDisplayName ?? null,
    }));
  }

  async getAdminConversationMessages(
    channelId: string,
    input?: { after?: number; limit?: number },
  ): Promise<AdminConversationMessageItem[]> {
    const params = new URLSearchParams();
    if (input?.after != null) {
      params.set('after', String(input.after));
    }
    if (input?.limit) {
      params.set('limit', String(input.limit));
    }
    const query = params.toString();
    const result = await this.request<{ items: AdminConversationMessageDto[] }>(
      `/api/v1/admin/conversations/${channelId}/messages${query ? `?${query}` : ''}`,
    );
    return (result.items ?? []).map((row) => this.mapAdminConversationMessage(row));
  }

  async getAdminThreadMessages(
    threadId: string,
    input?: { after?: number; limit?: number },
  ): Promise<AdminConversationMessageItem[]> {
    const params = new URLSearchParams();
    if (input?.after != null) {
      params.set('after', String(input.after));
    }
    if (input?.limit) {
      params.set('limit', String(input.limit));
    }
    const query = params.toString();
    const result = await this.request<{ items: AdminConversationMessageDto[] }>(
      `/api/v1/admin/threads/${threadId}/messages${query ? `?${query}` : ''}`,
    );
    return (result.items ?? []).map((row) => this.mapAdminConversationMessage(row));
  }

  async getAdminSensitiveSettings(workspaceId?: string): Promise<SensitiveSettings> {
    const query = workspaceId ? `?workspaceId=${encodeURIComponent(workspaceId)}` : '';
    return this.request<SensitiveSettings>(`/api/v1/admin/settings${query}`);
  }

  async updateAdminSensitiveSettings(input: UpdateSensitiveSettingsInput): Promise<SensitiveSettings> {
    return this.request<SensitiveSettings>('/api/v1/admin/settings', {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  }

  async rotateAdminOpenRouterCredential(input: RotateCredentialInput): Promise<CredentialRotateResult> {
    return this.request<CredentialRotateResult>('/api/v1/admin/settings/credentials/openrouter/rotate', {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  async rotateAdminSmtpCredential(input: RotateCredentialInput): Promise<CredentialRotateResult> {
    return this.request<CredentialRotateResult>('/api/v1/admin/settings/credentials/smtp/rotate', {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  async rotateAdminWebhookCredential(input: RotateCredentialInput): Promise<CredentialRotateResult> {
    return this.request<CredentialRotateResult>('/api/v1/admin/settings/credentials/webhook/rotate', {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  async reencryptAdminSettings(workspaceId?: string): Promise<ReencryptSettingsResult> {
    return this.request<ReencryptSettingsResult>('/api/v1/admin/settings/encryption/reencrypt', {
      method: 'POST',
      body: JSON.stringify({ workspaceId }),
    });
  }

  async downloadWorkspaceExport(workspaceId: string): Promise<void> {
    const headers = new Headers({ Accept: 'application/zip' });
    const devUser = this.auth.devUser();
    if (devUser) {
      headers.set('X-Dev-User', devUser);
    } else {
      const token = await this.auth.getAccessToken();
      if (token) {
        headers.set('Authorization', `Bearer ${token}`);
      }
    }

    const response = await fetch(
      `${this.baseUrl}/api/v1/admin/workspaces/${workspaceId}/export`,
      { headers },
    );
    if (!response.ok) {
      const text = await response.text().catch(() => '');
      const error = new Error(text || `HTTP ${response.status}`) as Error & { status: number };
      error.status = response.status;
      throw error;
    }

    const blob = await response.blob();
    const disposition = response.headers.get('Content-Disposition') ?? '';
    const match = /filename\*?=(?:UTF-8''|")?([^\";]+)/i.exec(disposition);
    const fileName = match?.[1]
      ? decodeURIComponent(match[1].replace(/"/g, ''))
      : `vibechat-export-${workspaceId}.zip`;

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  async summarizeChannel(workspaceId: string, channelId: string): Promise<AiSummaryResult> {
    const result = await this.request<{ summary: string }>(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/ai/summarize`,
      {
        method: 'POST',
        body: JSON.stringify({}),
      },
    );
    return {
      channelId,
      summary: result.summary,
      messageCount: 0,
      generatedAt: new Date().toISOString(),
    };
  }

  async suggestChannelReply(workspaceId: string, channelId: string): Promise<AiSuggestReplyResult> {
    const result = await this.request<{ suggestion: string }>(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/ai/suggest-reply`,
      {
        method: 'POST',
        body: JSON.stringify({}),
      },
    );
    return {
      channelId,
      suggestion: result.suggestion,
      generatedAt: new Date().toISOString(),
    };
  }

  private mapChannel(c: ChannelDto): Channel {
    const type = (c.type ?? 'Public').toLowerCase();
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
      isDirect: type === 'direct',
      peerUserId: c.peerUserId ?? undefined,
      peerDisplayName: c.peerDisplayName ?? undefined,
    };
  }

  private mapThread(t: ThreadDto): ChatThread {
    const me = this.auth.profile()?.id;
    return {
      id: t.id,
      channelId: t.channelId,
      parentMessageId: t.parentMessageId,
      createdBy: t.createdBy,
      createdAt: t.createdAt,
      replyCount: t.replyCount ?? 0,
      parentMessage: t.parentMessage ? this.mapMessage(t.parentMessage, me) : null,
    };
  }

  private mapMessage(m: MessageDto, me?: string): ChatMessage {
    const conversationId = m.conversationId || m.channelId;
    return {
      id: m.id,
      conversationId,
      channelId: m.channelId,
      authorUserId: m.authorId,
      authorName: m.authorName || m.authorId,
      body: m.deletedAt ? '' : m.body,
      createdAt: m.createdAt,
      editedAt: m.editedAt,
      deletedAt: m.deletedAt,
      seq: m.sequence,
      status: 'persisted',
      mine: !!me && me === m.authorId,
      attachments: (m.attachments ?? []).map((a) => this.mapAttachment(a)),
      threadId: m.threadId ?? null,
      replyToMessageId: m.replyToMessageId ?? null,
      replyTo: m.replyTo
        ? {
            messageId: String(m.replyTo.messageId),
            authorName: m.replyTo.authorName ?? '',
            preview: m.replyTo.preview ?? '',
            deleted: !!m.replyTo.deleted,
          }
        : null,
      forwardedFromMessageId: m.forwardedFromMessageId ?? null,
      forwardedFromChannelId: m.forwardedFromChannelId ?? null,
      forwardedFrom: m.forwardedFrom
        ? {
            messageId: String(m.forwardedFrom.messageId),
            channelId: String(m.forwardedFrom.channelId),
            channelName: m.forwardedFrom.channelName ?? '',
            authorName: m.forwardedFrom.authorName ?? '',
            createdAt: m.forwardedFrom.createdAt,
            isDirect: !!m.forwardedFrom.isDirect,
          }
        : null,
      replyCount: m.replyCount ?? 0,
      reactions: (m.reactions ?? []).map((r) => ({
        emoji: r.emoji,
        count: r.count,
        me: !!r.me,
      })),
      linkPreview: this.mapLinkPreview(m.linkPreview),
    };
  }

  private mapLinkPreview(preview?: LinkPreviewDto | null): MessageLinkPreview | null {
    if (!preview?.id || !preview.url) return null;
    return {
      id: String(preview.id),
      url: preview.url,
      title: preview.title ?? null,
      description: preview.description ?? null,
      siteName: preview.siteName ?? null,
      hasImage: !!preview.hasImage,
      status: preview.status ?? 'Ready',
    };
  }

  private mapAttachment(a: AttachmentDto): MessageAttachment {
    return {
      id: a.id,
      fileName: a.fileName,
      contentType: a.contentType,
      sizeBytes: a.sizeBytes,
      status: a.status,
      kind: a.kind === 'Audio' ? 'Audio' : 'File',
      durationMs: a.durationMs,
      waveform: a.waveform,
      thumbnailStatus: a.thumbnailStatus ?? null,
      width: a.width ?? null,
      height: a.height ?? null,
      pageCount: a.pageCount ?? null,
    };
  }

  private mapAdminConversationMessage(row: AdminConversationMessageDto): AdminConversationMessageItem {
    return {
      id: row.id,
      channelId: row.channelId,
      conversationId: row.conversationId,
      sequence: row.sequence,
      authorId: row.authorId,
      authorName: row.authorName,
      body: row.body,
      createdAt: row.createdAt,
      editedAt: row.editedAt ?? null,
      deletedAt: row.deletedAt ?? null,
      deletedBy: row.deletedBy ?? null,
      deletedByName: row.deletedByName ?? null,
      threadId: row.threadId ?? null,
      replyToMessageId: row.replyToMessageId ?? null,
      replyCount: row.replyCount ?? 0,
      attachments: (row.attachments ?? []).map((a) => ({
        id: a.id,
        fileName: a.fileName,
        contentType: a.contentType,
        sizeBytes: a.sizeBytes,
        status: a.status ?? 'Ready',
      })),
    };
  }

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const headers = new Headers(init.headers ?? {});
    headers.set('Accept', 'application/json');
    if (init.body && !headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json');
    }

    const devUser = this.auth.devUser();
    if (devUser) {
      headers.set('X-Dev-User', devUser);
    } else {
      const token = await this.auth.getAccessToken();
      if (token) {
        headers.set('Authorization', `Bearer ${token}`);
      }
    }

    const response = await fetch(`${this.baseUrl}${path}`, {
      ...init,
      headers,
    });

    if (!response.ok) {
      const text = await response.text().catch(() => '');
      const error = new Error(text || `HTTP ${response.status}`) as Error & { status: number };
      error.status = response.status;
      throw error;
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return (await response.json()) as T;
  }
}
