import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface KnowledgeCollection {
  id: string;
  title: string;
  slug: string;
  scope: 'workspace' | 'space' | 'channel';
  scopeId: string;
  parentId: string | null;
  updatedAt: string;
}

export interface KnowledgeReference {
  kind: string;
  targetId: string;
  state: 'visible' | 'redacted';
  label: string | null;
  channelId: string | null;
}

export interface KnowledgePage {
  id: string;
  collectionId: string;
  title: string;
  slug: string;
  body: string;
  version: number;
  createdBy: string;
  updatedBy: string;
  createdAt: string;
  updatedAt: string;
  deleted: boolean;
  references: KnowledgeReference[];
}

export interface KnowledgePageSummary {
  id: string;
  collectionId: string;
  title: string;
  slug: string;
  version: number;
  updatedAt: string;
}

export interface KnowledgeVersion {
  version: number;
  title: string;
  body: string;
  action: string;
  actorUserId: string;
  restoredFromVersion: number | null;
  at: string;
}

export interface KnowledgeAccess {
  canEdit: boolean;
  canManage: boolean;
}

@Injectable({ providedIn: 'root' })
export class KnowledgeApiService extends HttpApiClient {
  async listCollections(workspaceId: string): Promise<KnowledgeAccess & { items: KnowledgeCollection[] }> {
    const dto = await this.request<{
      items: KnowledgeCollection[];
      canEdit: boolean;
      canManage: boolean;
    }>(`/api/v1/workspaces/${workspaceId}/knowledge/collections`);
    return {
      canEdit: dto.canEdit,
      canManage: dto.canManage,
      items: (dto.items ?? []).map((item) => ({ ...item, id: String(item.id), parentId: item.parentId ? String(item.parentId) : null })),
    };
  }

  async createCollection(workspaceId: string, input: {
    idempotencyKey: string;
    title: string;
    scope: 'workspace' | 'channel';
    scopeId: string | null;
  }): Promise<KnowledgeCollection> {
    return this.request<KnowledgeCollection>(`/api/v1/workspaces/${workspaceId}/knowledge/collections`, {
      method: 'POST',
      body: JSON.stringify({ ...input, parentId: null }),
    });
  }

  async listPages(workspaceId: string, collectionId: string): Promise<KnowledgePageSummary[]> {
    const dto = await this.request<{ items: KnowledgePageSummary[] }>(
      `/api/v1/workspaces/${workspaceId}/knowledge/pages?collectionId=${collectionId}`,
    );
    return dto.items ?? [];
  }

  async createPage(workspaceId: string, input: {
    idempotencyKey: string;
    collectionId: string;
    title: string;
    body: string;
  }): Promise<KnowledgePage> {
    return this.request<KnowledgePage>(`/api/v1/workspaces/${workspaceId}/knowledge/pages`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  async getPage(workspaceId: string, pageId: string): Promise<KnowledgePage> {
    return this.request<KnowledgePage>(`/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}`);
  }

  async updatePage(workspaceId: string, pageId: string, input: {
    version: number;
    title: string;
    body: string;
  }): Promise<KnowledgePage> {
    return this.request<KnowledgePage>(`/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}`, {
      method: 'PATCH',
      body: JSON.stringify(input),
    });
  }

  async history(workspaceId: string, pageId: string): Promise<KnowledgeVersion[]> {
    const dto = await this.request<{ items: KnowledgeVersion[] }>(
      `/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}/history`,
    );
    return dto.items ?? [];
  }

  async restore(workspaceId: string, pageId: string, expectedVersion: number, sourceVersion: number): Promise<KnowledgePage> {
    return this.request<KnowledgePage>(`/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}/restore`, {
      method: 'POST',
      body: JSON.stringify({ expectedVersion, sourceVersion }),
    });
  }

  async removePage(workspaceId: string, pageId: string): Promise<void> {
    await this.request<void>(`/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}`, { method: 'DELETE' });
  }

  async search(workspaceId: string, query: string): Promise<KnowledgePageSummary[]> {
    const dto = await this.request<{ items: KnowledgePageSummary[] }>(
      `/api/v1/workspaces/${workspaceId}/knowledge/search?q=${encodeURIComponent(query)}`,
    );
    return dto.items ?? [];
  }
}
