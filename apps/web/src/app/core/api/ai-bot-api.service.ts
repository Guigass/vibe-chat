import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface AiBotSummary {
  id: string;
  name: string;
  description: string;
  status: 'Draft' | 'Published' | 'Disabled';
  revision: number;
  activeVersion: number | null;
  principalUserId: string;
}

export interface AiBotDraft {
  name: string;
  description: string;
  avatarKey: string;
  allowDms: boolean;
  systemPrompt: string;
  provider: string;
  modelId: string;
  parameters: { temperature?: number };
  maxTokens: number;
  timeoutMs: number;
  fallbackModelId: string | null;
  dataClass: string;
  channelIds: string[];
  skillVersionIds: string[];
  knowledgeGrantIds: string[];
  mcpToolGrantIds: string[];
  guardrailPolicyVersion: string | null;
  maxTurns: number;
  maxInputTokens: number;
  maxOutputTokens: number;
  dailyBudgetUsd: number;
}

export interface AiBotVersionSummary {
  versionNumber: number;
  publishedAt: string;
  provider: string;
  modelId: string;
  name: string;
}

export interface AiBotDetail extends AiBotSummary {
  draft: AiBotDraft;
  versions: AiBotVersionSummary[];
}

export interface AiBotFieldChange {
  field: string;
  before: string | null;
  after: string | null;
}

export interface AiBotDiff {
  fromVersion: number;
  toVersion: number;
  changes: AiBotFieldChange[];
}

@Injectable({ providedIn: 'root' })
export class AiBotApiService extends HttpApiClient {
  list(workspaceId: string): Promise<AiBotSummary[]> {
    return this.request(`/api/v1/admin/ai/bots/workspaces/${workspaceId}`);
  }

  get(workspaceId: string, botId: string): Promise<AiBotDetail> {
    return this.request(`/api/v1/admin/ai/bots/workspaces/${workspaceId}/${botId}`);
  }

  create(workspaceId: string, body: AiBotDraft, key: string): Promise<AiBotDetail> {
    return this.send(`/api/v1/admin/ai/bots/workspaces/${workspaceId}`, 'POST', body, key);
  }

  update(workspaceId: string, botId: string, body: AiBotDraft, revision: number, key: string): Promise<AiBotDetail> {
    return this.send(`/api/v1/admin/ai/bots/workspaces/${workspaceId}/${botId}`, 'PUT', { ...body, expectedRevision: revision }, key);
  }

  diff(workspaceId: string, botId: string, fromVersion: number, toVersion: number): Promise<AiBotDiff> {
    return this.request(`/api/v1/admin/ai/bots/workspaces/${workspaceId}/${botId}/diff?fromVersion=${fromVersion}&toVersion=${toVersion}`);
  }

  publish(workspaceId: string, botId: string, body: AiBotDraft, revision: number, key: string): Promise<AiBotDetail> {
    return this.send(`/api/v1/admin/ai/bots/workspaces/${workspaceId}/${botId}/publish`, 'POST', { ...body, expectedRevision: revision }, key);
  }

  disable(workspaceId: string, botId: string, revision: number, key: string): Promise<AiBotDetail> {
    return this.send(`/api/v1/admin/ai/bots/workspaces/${workspaceId}/${botId}/disable`, 'POST', { expectedRevision: revision }, key);
  }

  rollback(workspaceId: string, botId: string, revision: number, versionNumber: number, key: string): Promise<AiBotDetail> {
    return this.send(
      `/api/v1/admin/ai/bots/workspaces/${workspaceId}/${botId}/rollback`,
      'POST',
      { expectedRevision: revision, versionNumber },
      key,
    );
  }

  private send<T>(path: string, method: string, body: unknown, key: string): Promise<T> {
    return this.request(path, {
      method,
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': key },
    });
  }
}
