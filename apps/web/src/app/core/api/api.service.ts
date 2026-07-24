import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';
import {
  AdminStats,
  AiSummaryResult,
  Channel,
  ChatMessage,
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

interface MessageDto {
  id: string;
  channelId: string;
  sequence: number;
  authorId: string;
  authorName?: string;
  body: string;
  createdAt: string;
  editedAt?: string | null;
  deletedAt?: string | null;
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
  }): Promise<ChatMessage> {
    const dto = await this.request<MessageDto>(`/api/v1/channels/${input.channelId}/messages`, {
      method: 'POST',
      body: JSON.stringify({
        messageId: input.clientMessageId,
        idempotencyKey: input.idempotencyKey,
        body: input.body,
      }),
    });
    return this.mapMessage(dto, this.auth.profile()?.id);
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

  private mapMessage(m: MessageDto, me?: string): ChatMessage {
    return {
      id: m.id,
      conversationId: m.channelId,
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
