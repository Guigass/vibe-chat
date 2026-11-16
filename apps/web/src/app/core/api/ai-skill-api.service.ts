import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface AiSkillSummary {
  id: string;
  slug: string;
  name: string;
  description: string;
  status: 'Draft' | 'Published' | 'Deprecated';
  revision: number;
  activeVersion: number | null;
  activeVersionId: string | null;
}

export interface AiSkillExample {
  name?: string;
  input?: Record<string, unknown>;
  expected?: string;
}

export interface AiSkillDraft {
  slug: string;
  name: string;
  description: string;
  instructions: string;
  argumentsSchema: { type?: string };
  examples: AiSkillExample[];
  compatibility: string;
  classification: string;
  knowledgeGrantIds: string[];
  mcpToolGrantIds: string[];
}

export interface AiSkillVersionSummary {
  id: string;
  versionNumber: number;
  publishedAt: string;
  instructionsSha256: string;
}

export interface AiSkillDetail extends AiSkillSummary {
  ownerUserId: string;
  draft: AiSkillDraft;
  versions: AiSkillVersionSummary[];
}

export interface AiSkillFieldChange {
  field: string;
  before: string | null;
  after: string | null;
}

export interface AiSkillDiff {
  fromVersion: number;
  toVersion: number;
  changes: AiSkillFieldChange[];
}

export interface AiSkillPromptLayer {
  order: number;
  layer: 'platform' | 'workspace' | 'bot' | 'skill';
  skillVersionId: string | null;
  instructionsSha256: string | null;
}

export interface AiSkillComposition {
  layers: AiSkillPromptLayer[];
  warning: string;
}

@Injectable({ providedIn: 'root' })
export class AiSkillApiService extends HttpApiClient {
  list(workspaceId: string): Promise<AiSkillSummary[]> {
    return this.request(`/api/v1/admin/ai/skills/workspaces/${workspaceId}`);
  }

  get(workspaceId: string, skillId: string): Promise<AiSkillDetail> {
    return this.request(`/api/v1/admin/ai/skills/workspaces/${workspaceId}/${skillId}`);
  }

  create(workspaceId: string, body: AiSkillDraft, key: string): Promise<AiSkillDetail> {
    return this.send(`/api/v1/admin/ai/skills/workspaces/${workspaceId}`, 'POST', body, key);
  }

  update(workspaceId: string, skillId: string, body: AiSkillDraft, revision: number, key: string): Promise<AiSkillDetail> {
    return this.send(`/api/v1/admin/ai/skills/workspaces/${workspaceId}/${skillId}`, 'PUT', { ...body, expectedRevision: revision }, key);
  }

  diff(workspaceId: string, skillId: string, fromVersion: number, toVersion: number): Promise<AiSkillDiff> {
    return this.request(`/api/v1/admin/ai/skills/workspaces/${workspaceId}/${skillId}/diff?fromVersion=${fromVersion}&toVersion=${toVersion}`);
  }

  export(workspaceId: string, skillId: string, version: number): Promise<unknown> {
    return this.request(`/api/v1/admin/ai/skills/workspaces/${workspaceId}/${skillId}/export?version=${version}`);
  }

  publish(workspaceId: string, skillId: string, body: AiSkillDraft, revision: number, key: string): Promise<AiSkillDetail> {
    return this.send(`/api/v1/admin/ai/skills/workspaces/${workspaceId}/${skillId}/publish`, 'POST', { ...body, expectedRevision: revision }, key);
  }

  deprecate(workspaceId: string, skillId: string, revision: number, key: string): Promise<AiSkillDetail> {
    return this.send(`/api/v1/admin/ai/skills/workspaces/${workspaceId}/${skillId}/deprecate`, 'POST', { expectedRevision: revision }, key);
  }

  clone(workspaceId: string, skillId: string, versionNumber: number, key: string): Promise<AiSkillDetail> {
    return this.send(`/api/v1/admin/ai/skills/workspaces/${workspaceId}/${skillId}/clone`, 'POST', { versionNumber }, key);
  }

  rollback(workspaceId: string, skillId: string, revision: number, versionNumber: number, key: string): Promise<AiSkillDetail> {
    return this.send(
      `/api/v1/admin/ai/skills/workspaces/${workspaceId}/${skillId}/rollback`,
      'POST',
      { expectedRevision: revision, versionNumber },
      key,
    );
  }

  import(workspaceId: string, manifest: unknown, key: string): Promise<AiSkillDetail> {
    return this.send(`/api/v1/admin/ai/skills/workspaces/${workspaceId}/import`, 'POST', manifest, key);
  }

  compose(workspaceId: string, skillVersionIds: string[]): Promise<AiSkillComposition> {
    return this.request(`/api/v1/admin/ai/skills/workspaces/${workspaceId}/compose`, {
      method: 'POST',
      body: JSON.stringify({ skillVersionIds }),
    });
  }

  private send<T>(path: string, method: string, body: unknown, key: string): Promise<T> {
    return this.request(path, {
      method,
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': key },
    });
  }
}
