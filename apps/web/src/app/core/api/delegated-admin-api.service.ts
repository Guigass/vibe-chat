import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface DelegatedScopeView {
  scope: string;
  spaceId?: string | null;
}

export interface DelegatedMe {
  scopes: DelegatedScopeView[];
  areas: string[];
  canGrant: boolean;
}

export interface DelegatedAssignment {
  id: string;
  granteeId: string;
  scope: string;
  spaceId?: string | null;
  reason: string;
  expiresAt?: string | null;
  breakGlass: boolean;
}

export interface DelegatedUsageRow {
  resource: string;
  unit: string;
  used: number;
  previousUsed: number;
  softLimit: number | null;
  hardLimit: number | null;
  trend: string;
  action: string;
  reason?: string | null;
}

export interface DelegatedAlert {
  id: string;
  resource: string;
  kind: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class DelegatedAdminApiService extends HttpApiClient {
  me(workspaceId: string): Promise<DelegatedMe> {
    return this.request(`/api/v1/workspaces/${workspaceId}/delegated-admin/me`);
  }

  catalog(workspaceId: string): Promise<{ scopes: string[] }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/delegated-admin/catalog`);
  }

  assignments(workspaceId: string): Promise<{ assignments: DelegatedAssignment[] }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/delegated-admin/assignments`);
  }

  grant(
    workspaceId: string,
    body: { granteeId: string; scope: string; spaceId?: string; reason: string; breakGlass: boolean },
  ): Promise<DelegatedAssignment> {
    return this.request(`/api/v1/workspaces/${workspaceId}/delegated-admin/assignments`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify(body),
    });
  }

  revoke(workspaceId: string, assignmentId: string): Promise<unknown> {
    return this.request(`/api/v1/workspaces/${workspaceId}/delegated-admin/assignments/${assignmentId}/revoke`, {
      method: 'POST',
      body: '{}',
    });
  }

  usage(workspaceId: string): Promise<{ resources: DelegatedUsageRow[] }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/quotas/usage`);
  }

  alerts(workspaceId: string): Promise<{ alerts: DelegatedAlert[] }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/delegated-admin/alerts`);
  }
}
