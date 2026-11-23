import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface BotKnowledgeSummary {
  id: string;
  name: string;
  kind: 'file' | 'page' | 'url';
  classification: string;
  status: string;
  version: number;
  chunkCount: number;
  checksum: string;
  lastError: string | null;
  updatedAt: string;
}

export interface BotKnowledgeJob {
  id: string;
  status: string;
  attempt: number;
  error: string | null;
  finishedAt: string | null;
}

export interface BotKnowledgeGrant {
  id: string;
  botId: string;
  revokedAt: string | null;
}

export interface BotKnowledgeDetail extends BotKnowledgeSummary {
  contentSha256: string;
  locator: string;
  pageId: string | null;
  fileId: string | null;
  deletedAt: string | null;
  indexLagSeconds: number;
  purgeEvidence: string | null;
  jobs: BotKnowledgeJob[];
  grants: BotKnowledgeGrant[];
}

export interface BotKnowledgeDraft {
  name: string;
  classification: string;
  kind: 'file' | 'page' | 'url';
  text: string;
  url: string;
  pageId: string;
}

export interface BotKnowledgeCitation {
  sourceId: string;
  version: number;
  chunkId: string;
  excerpt: string;
  score: number;
}

export interface BotKnowledgeAnswer {
  evidence: 'cited' | 'insufficient';
  citations: BotKnowledgeCitation[];
}

@Injectable({ providedIn: 'root' })
export class BotKnowledgeApiService extends HttpApiClient {
  list(workspaceId: string): Promise<BotKnowledgeSummary[]> {
    return this.request(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}`);
  }

  get(workspaceId: string, sourceId: string): Promise<BotKnowledgeDetail> {
    return this.request(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}/${sourceId}`);
  }

  create(workspaceId: string, body: BotKnowledgeDraft, key: string): Promise<BotKnowledgeDetail> {
    return this.send(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}`, body, key);
  }

  pause(workspaceId: string, sourceId: string, key: string): Promise<BotKnowledgeDetail> {
    return this.send(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}/${sourceId}/pause`, {}, key);
  }

  resume(workspaceId: string, sourceId: string, key: string): Promise<BotKnowledgeDetail> {
    return this.send(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}/${sourceId}/resume`, {}, key);
  }

  reindex(workspaceId: string, sourceId: string, key: string): Promise<BotKnowledgeDetail> {
    return this.send(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}/${sourceId}/reindex`, {}, key);
  }

  remove(workspaceId: string, sourceId: string, key: string): Promise<unknown> {
    return this.send(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}/${sourceId}/delete`, {}, key);
  }

  grant(workspaceId: string, sourceId: string, botId: string, key: string): Promise<BotKnowledgeGrant> {
    return this.send(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}/${sourceId}/grants`, { botId }, key);
  }

  revoke(workspaceId: string, sourceId: string, grantId: string, key: string): Promise<BotKnowledgeGrant> {
    return this.send(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}/${sourceId}/grants/${grantId}/revoke`, {}, key);
  }

  ask(workspaceId: string, botId: string, query: string): Promise<BotKnowledgeAnswer> {
    return this.request(`/api/v1/admin/ai/knowledge/workspaces/${workspaceId}/retrieve`, {
      method: 'POST',
      body: JSON.stringify({ botId, query }),
    });
  }

  private send<T>(path: string, body: unknown, key: string): Promise<T> {
    return this.request(path, {
      method: 'POST',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': key },
    });
  }
}
