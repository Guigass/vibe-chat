import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import type { AttachmentDto } from './api-mappers';
import {
  AdminConversationItem,
  AdminConversationMessageItem,
  AdminStats,
  AuditEventItem,
  CredentialRotateResult,
  ReencryptSettingsResult,
  RotateCredentialInput,
  RotateVapidInput,
  SensitiveSettings,
  UpdateSensitiveSettingsInput,
  UpsertWebhookEndpointInput,
  WebhookEndpointSettings,
  IntegrationBot,
  InstalledPlugin,
  OnboardingItem,
  TemplatePlan,
  WorkspaceOnboardingState,
  WorkspaceTemplateCatalog,
} from '../../shared/models/chat.models';

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
export class AdminApiService extends HttpApiClient {
  listInstalledPlugins(workspaceId: string): Promise<InstalledPlugin[]> {
    return this.request<InstalledPlugin[]>(`/api/v1/admin/workspaces/${workspaceId}/plugins`);
  }

  installPlugin(
    workspaceId: string,
    input: { builtinId?: string; manifest?: Record<string, unknown>; channelIds: string[]; allowDms: boolean },
  ): Promise<InstalledPlugin> {
    return this.request<InstalledPlugin>(`/api/v1/admin/workspaces/${workspaceId}/plugins`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  updateInstalledPlugin(workspaceId: string, installedId: string, enabled: boolean): Promise<InstalledPlugin> {
    return this.request<InstalledPlugin>(`/api/v1/admin/workspaces/${workspaceId}/plugins/${installedId}`, {
      method: 'PATCH',
      body: JSON.stringify({ enabled }),
    });
  }

  uninstallPlugin(workspaceId: string, installedId: string): Promise<void> {
    return this.request<void>(`/api/v1/admin/workspaces/${workspaceId}/plugins/${installedId}`, {
      method: 'DELETE',
    });
  }

  rotateInstalledPlugin(workspaceId: string, installedId: string): Promise<InstalledPlugin> {
    return this.request<InstalledPlugin>(`/api/v1/admin/workspaces/${workspaceId}/plugins/${installedId}/rotate`, {
      method: 'POST',
    });
  }

  listWorkspaceTemplates(workspaceId: string): Promise<WorkspaceTemplateCatalog> {
    return this.request<WorkspaceTemplateCatalog>(`/api/v1/admin/workspaces/${workspaceId}/templates`);
  }

  previewWorkspaceTemplate(
    workspaceId: string,
    body: { templateId?: string; manifest?: Record<string, unknown> },
  ): Promise<TemplatePlan> {
    return this.request<TemplatePlan>(`/api/v1/admin/workspaces/${workspaceId}/templates/preview`, {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  applyWorkspaceTemplate(
    workspaceId: string,
    body: { templateId?: string; manifest?: Record<string, unknown>; dryRun?: boolean },
    idempotencyKey: string,
  ): Promise<TemplatePlan> {
    return this.request<TemplatePlan>(`/api/v1/admin/workspaces/${workspaceId}/templates/apply`, {
      method: 'POST',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  exportWorkspaceTemplate(workspaceId: string): Promise<Record<string, unknown>> {
    return this.request<Record<string, unknown>>(`/api/v1/admin/workspaces/${workspaceId}/templates/export`);
  }

  getOnboarding(workspaceId: string): Promise<WorkspaceOnboardingState> {
    return this.request<WorkspaceOnboardingState>(`/api/v1/admin/workspaces/${workspaceId}/onboarding`);
  }

  updateOnboarding(
    workspaceId: string,
    body: { status?: string; items?: OnboardingItem[] },
  ): Promise<WorkspaceOnboardingState> {
    return this.request<WorkspaceOnboardingState>(`/api/v1/admin/workspaces/${workspaceId}/onboarding`, {
      method: 'PUT',
      body: JSON.stringify(body),
    });
  }

  listIntegrationBots(workspaceId: string): Promise<IntegrationBot[]> {
    return this.request<IntegrationBot[]>(`/api/v1/admin/workspaces/${workspaceId}/bots`);
  }

  createIntegrationBot(
    workspaceId: string,
    input: { name: string; channelIds: string[]; allowDms: boolean },
  ): Promise<IntegrationBot> {
    return this.request<IntegrationBot>(`/api/v1/admin/workspaces/${workspaceId}/bots`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  updateIntegrationBot(
    workspaceId: string,
    botId: string,
    input: { name: string; channelIds: string[]; allowDms: boolean; enabled: boolean },
  ): Promise<IntegrationBot> {
    return this.request<IntegrationBot>(`/api/v1/admin/workspaces/${workspaceId}/bots/${botId}`, {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  }

  rotateIntegrationBot(workspaceId: string, botId: string): Promise<IntegrationBot> {
    return this.request<IntegrationBot>(`/api/v1/admin/workspaces/${workspaceId}/bots/${botId}/rotate`, {
      method: 'POST',
    });
  }

  revokeIntegrationBot(workspaceId: string, botId: string): Promise<void> {
    return this.request<void>(`/api/v1/admin/workspaces/${workspaceId}/bots/${botId}/revoke`, {
      method: 'POST',
    });
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

  async createAdminWebhook(input: UpsertWebhookEndpointInput): Promise<WebhookEndpointSettings> {
    return this.request<WebhookEndpointSettings>('/api/v1/admin/webhooks', {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  async updateAdminWebhook(
    endpointId: string,
    input: UpsertWebhookEndpointInput,
  ): Promise<WebhookEndpointSettings> {
    return this.request<WebhookEndpointSettings>(`/api/v1/admin/webhooks/${endpointId}`, {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  }

  async deleteAdminWebhook(endpointId: string, workspaceId?: string): Promise<void> {
    const query = workspaceId ? `?workspaceId=${encodeURIComponent(workspaceId)}` : '';
    await this.request<void>(`/api/v1/admin/webhooks/${endpointId}${query}`, { method: 'DELETE' });
  }

  async rotateAdminWebhookEndpoint(
    endpointId: string,
    input: RotateCredentialInput,
  ): Promise<CredentialRotateResult> {
    return this.request<CredentialRotateResult>(`/api/v1/admin/webhooks/${endpointId}/rotate`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  async testAdminWebhook(
    endpointId: string,
    workspaceId?: string,
  ): Promise<{ ok: boolean; statusCode: number | null; lastError: string | null }> {
    return this.request(`/api/v1/admin/webhooks/${endpointId}/test`, {
      method: 'POST',
      body: JSON.stringify({ workspaceId }),
    });
  }

  async rotateAdminVapidCredential(input: RotateVapidInput): Promise<CredentialRotateResult> {
    return this.request<CredentialRotateResult>('/api/v1/admin/settings/credentials/vapid/rotate', {
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
}
