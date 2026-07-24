import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';
import {
  AdminStats,
  AiSummaryResult,
  Channel,
  ChatMessage,
  ChatThread,
  MessageAttachment,
  SearchMessagesResult,
  Workspace,
  WorkspaceMember,
} from '../../shared/models/chat.models';

interface WorkspaceDto {
  id: string;
  name: string;
  slug: string;
  role?: string;
}

interface ChannelDto {
  id: string;
  workspaceId: string;
  name: string;
  type?: string;
  peerUserId?: string | null;
  peerDisplayName?: string | null;
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
  replyCount?: number;
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

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly auth = inject(AuthService);
  private readonly baseUrl = environment.apiUrl;

  async getWorkspaces(): Promise<Workspace[]> {
    const rows = await this.request<WorkspaceDto[]>('/api/v1/workspaces');
    return rows.map((w) => ({ id: w.id, name: w.name, slug: w.slug }));
  }

  async getChannels(workspaceId: string): Promise<Channel[]> {
    const rows = await this.request<ChannelDto[]>(`/api/v1/workspaces/${workspaceId}/channels`);
    return rows.map((c) => this.mapChannel(c));
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

  async openDirectMessage(workspaceId: string, userId: string): Promise<Channel> {
    const dto = await this.request<ChannelDto>(`/api/v1/workspaces/${workspaceId}/dms`, {
      method: 'POST',
      body: JSON.stringify({ userId }),
    });
    return this.mapChannel(dto);
  }

  async getMessages(channelId: string, take = 50): Promise<ChatMessage[]> {
    const rows = await this.request<MessageDto[]>(
      `/api/v1/channels/${channelId}/messages?limit=${take}`,
    );
    const me = this.auth.profile()?.id;
    return rows.map((m) => this.mapMessage(m, me));
  }

  async sendMessage(input: {
    channelId: string;
    body: string;
    clientMessageId: string;
    idempotencyKey: string;
    attachmentIds?: string[];
  }): Promise<ChatMessage> {
    const dto = await this.request<MessageDto>(`/api/v1/channels/${input.channelId}/messages`, {
      method: 'POST',
      body: JSON.stringify({
        messageId: input.clientMessageId,
        idempotencyKey: input.idempotencyKey,
        body: input.body,
        attachmentIds: input.attachmentIds ?? [],
      }),
    });
    return this.mapMessage(dto, this.auth.profile()?.id);
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
  }): Promise<AttachmentUploadDto> {
    return this.request<AttachmentUploadDto>(`/api/v1/channels/${input.channelId}/attachments`, {
      method: 'POST',
      body: JSON.stringify({
        fileName: input.fileName,
        contentType: input.contentType,
        sizeBytes: input.sizeBytes,
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

  async getAttachmentDownload(
    channelId: string,
    attachmentId: string,
  ): Promise<AttachmentDownloadDto> {
    return this.request<AttachmentDownloadDto>(
      `/api/v1/channels/${channelId}/attachments/${attachmentId}/download`,
    );
  }

  async uploadFileToPresignedUrl(
    uploadUrl: string,
    file: File,
    requiredHeaders: Record<string, string>,
  ): Promise<void> {
    const headers = new Headers();
    for (const [key, value] of Object.entries(requiredHeaders ?? {})) {
      headers.set(key, value);
    }
    const response = await fetch(uploadUrl, {
      method: 'PUT',
      headers,
      body: file,
    });
    if (!response.ok) {
      throw new Error(`Upload failed (${response.status})`);
    }
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

  async upsertReadCursor(channelId: string, lastReadSequence: number): Promise<void> {
    await this.request(`/api/v1/channels/${channelId}/read-cursor`, {
      method: 'PUT',
      body: JSON.stringify({ lastReadSequence }),
    });
  }

  async getUnreadCount(channelId: string): Promise<number> {
    const result = await this.request<{ unreadCount: number }>(
      `/api/v1/channels/${channelId}/unread-count`,
    );
    return result.unreadCount;
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

  private mapChannel(c: ChannelDto): Channel {
    const type = (c.type ?? 'Public').toLowerCase();
    return {
      id: c.id,
      workspaceId: c.workspaceId,
      name: c.name,
      unreadCount: 0,
      type,
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
      replyCount: m.replyCount ?? 0,
    };
  }

  private mapAttachment(a: AttachmentDto): MessageAttachment {
    return {
      id: a.id,
      fileName: a.fileName,
      contentType: a.contentType,
      sizeBytes: a.sizeBytes,
      status: a.status,
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
      throw new Error(text || `HTTP ${response.status}`);
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return (await response.json()) as T;
  }
}
