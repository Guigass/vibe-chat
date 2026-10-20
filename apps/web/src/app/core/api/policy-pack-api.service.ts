import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface PolicyPackCatalogItem {
  id: string;
  version: number;
  label: string;
}

export interface PolicyPackCatalog {
  packs: PolicyPackCatalogItem[];
  certification: boolean;
}

export interface PolicyControlView {
  key: string;
  value: boolean | number;
  locked: boolean;
  enforced: boolean;
  source?: string;
  drifted?: boolean;
}

export interface PolicyPackDocument {
  assigned?: boolean;
  packId?: string;
  label?: string;
  certification?: boolean;
  controls?: PolicyControlView[];
}

export interface PolicyDrift {
  assigned: boolean;
  drifted: boolean;
  upgradeAvailable: boolean;
  controls: PolicyControlView[];
}

@Injectable({ providedIn: 'root' })
export class PolicyPackApiService extends HttpApiClient {
  catalog(workspaceId: string): Promise<PolicyPackCatalog> {
    return this.request(`/api/v1/workspaces/${workspaceId}/policy-packs/catalog`);
  }

  current(workspaceId: string): Promise<PolicyPackDocument> {
    return this.request(`/api/v1/workspaces/${workspaceId}/policy-packs`);
  }

  preview(workspaceId: string, packId: string, version: number): Promise<PolicyPackDocument> {
    return this.request(`/api/v1/workspaces/${workspaceId}/policy-packs/preview`, {
      method: 'POST',
      body: JSON.stringify({ packId, version, overrides: {} }),
    });
  }

  apply(workspaceId: string, packId: string, version: number): Promise<PolicyPackDocument> {
    return this.request(`/api/v1/workspaces/${workspaceId}/policy-packs/apply`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify({ packId, version, confirm: true, overrides: {} }),
    });
  }

  rollback(workspaceId: string): Promise<PolicyPackDocument> {
    return this.request(`/api/v1/workspaces/${workspaceId}/policy-packs/rollback`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify({ confirm: true }),
    });
  }

  drift(workspaceId: string): Promise<PolicyDrift> {
    return this.request(`/api/v1/workspaces/${workspaceId}/policy-packs/drift`);
  }

  exportPack(workspaceId: string): Promise<unknown> {
    return this.request(`/api/v1/workspaces/${workspaceId}/policy-packs/export`);
  }
}
