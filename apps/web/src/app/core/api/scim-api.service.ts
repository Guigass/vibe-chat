import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface ScimTokenItem {
  id: string;
  name: string;
  last4: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export interface ScimTokenCreated extends ScimTokenItem {
  secret: string;
}

export interface ScimGroupItem {
  id: string;
  displayName: string;
  externalId: string | null;
}

export interface ScimMappingItem {
  id: string;
  groupId: string;
  role: string;
}

export interface ScimOverview {
  keycloakAdapter: boolean;
  tokens: ScimTokenItem[];
  groups: ScimGroupItem[];
  mappings: ScimMappingItem[];
}

export interface ScimPreview {
  mappingId: string | null;
  adds: { userId: string; role: string }[];
  conflicts: { userId: string; currentRole: string; mappedRole: string }[];
}

@Injectable({ providedIn: 'root' })
export class ScimApiService extends HttpApiClient {
  load(workspaceId: string): Promise<ScimOverview> {
    return this.request<ScimOverview>(`/api/v1/admin/workspaces/${workspaceId}/scim`);
  }

  createToken(workspaceId: string, name: string): Promise<ScimTokenCreated> {
    return this.request<ScimTokenCreated>(`/api/v1/admin/workspaces/${workspaceId}/scim/tokens`, {
      method: 'POST',
      body: JSON.stringify({ name }),
    });
  }

  rotateToken(workspaceId: string, tokenId: string): Promise<ScimTokenCreated> {
    return this.request<ScimTokenCreated>(
      `/api/v1/admin/workspaces/${workspaceId}/scim/tokens/${tokenId}/rotate`,
      { method: 'POST' },
    );
  }

  revokeToken(workspaceId: string, tokenId: string): Promise<void> {
    return this.request<void>(`/api/v1/admin/workspaces/${workspaceId}/scim/tokens/${tokenId}`, {
      method: 'DELETE',
    });
  }

  dryRun(workspaceId: string, groupId: string, role: string): Promise<ScimPreview> {
    return this.request<ScimPreview>(`/api/v1/admin/workspaces/${workspaceId}/scim/mappings/dry-run`, {
      method: 'POST',
      body: JSON.stringify({ groupId, role }),
    });
  }

  applyMapping(workspaceId: string, groupId: string, role: string): Promise<ScimPreview> {
    return this.request<ScimPreview>(`/api/v1/admin/workspaces/${workspaceId}/scim/mappings`, {
      method: 'POST',
      body: JSON.stringify({ groupId, role }),
    });
  }
}
