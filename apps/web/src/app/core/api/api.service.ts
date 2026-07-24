import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';
import {
  AdminStats,
  AiSummaryResult,
  Channel,
  ChatMessage,
  Workspace,
} from '../../shared/models/chat.models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly auth = inject(AuthService);
  private readonly baseUrl = environment.apiUrl;

  async getWorkspaces(): Promise<Workspace[]> {
    return this.request<Workspace[]>('/api/workspaces');
  }

  async getChannels(workspaceId: string): Promise<Channel[]> {
    return this.request<Channel[]>(`/api/workspaces/${workspaceId}/channels`);
  }

  async getMessages(channelId: string, take = 50): Promise<ChatMessage[]> {
    return this.request<ChatMessage[]>(`/api/channels/${channelId}/messages?take=${take}`);
  }

  async sendMessage(input: {
    channelId: string;
    body: string;
    clientMessageId: string;
    idempotencyKey: string;
  }): Promise<ChatMessage> {
    return this.request<ChatMessage>(`/api/channels/${input.channelId}/messages`, {
      method: 'POST',
      headers: {
        'Idempotency-Key': input.idempotencyKey,
      },
      body: JSON.stringify({
        body: input.body,
        contentType: 'text/plain',
        clientMessageId: input.clientMessageId,
      }),
    });
  }

  async getAdminStats(): Promise<AdminStats> {
    return this.request<AdminStats>('/api/admin/stats');
  }

  async summarizeChannel(channelId: string): Promise<AiSummaryResult> {
    return this.request<AiSummaryResult>(`/api/ai/channels/${channelId}/summarize`, {
      method: 'POST',
      body: JSON.stringify({}),
    });
  }

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const token = await this.auth.getAccessToken();
    const headers = new Headers(init.headers ?? {});
    headers.set('Accept', 'application/json');
    if (init.body && !headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json');
    }
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
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
