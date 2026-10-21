import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface DeveloperCredential {
  id: string;
  name: string;
  enabled: boolean;
  allowDms: boolean;
  channelIds: string[];
  tokenConfigured: boolean;
  last4: string | null;
  lastUsedAt: string | null;
  revoked: boolean;
  createdAt: string;
}

export interface DeveloperCredentialSecret extends DeveloperCredential {
  token: string | null;
  idempotent: boolean;
}

export interface DeveloperCatalog {
  schemaVersion: number;
  sandbox: boolean;
  liveSendEnabled: boolean;
  secretInUrl: boolean;
  capabilities: string[];
  rateLimit: { sendPerMinute: number; window: string; scope: string };
  changelog: { id: string; status: string; retiresOn: string | null }[];
  events: { eventType: string; schemaVersion: number; status: string }[];
  examples: { operation: string; synthetic: boolean }[];
}

export interface DeveloperUsage {
  sendPerMinute: number;
  window: string;
  scope: string;
  errors: number;
  calls: { action: string; status: number; occurredAt: string; synthetic: boolean; credentialId: string | null; operation: string | null }[];
}

export interface DeveloperExploreResult {
  synthetic: boolean;
  sandbox: boolean;
  delivered: boolean;
  operation: string;
  credentialId: string;
  status: string;
}

@Injectable({ providedIn: 'root' })
export class DeveloperApiService extends HttpApiClient {
  catalog(workspaceId: string): Promise<DeveloperCatalog> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/catalog`);
  }

  openApi(workspaceId: string): Promise<unknown> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/openapi`);
  }

  contractKit(workspaceId: string): Promise<unknown> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/contract-kit`);
  }

  credentials(workspaceId: string): Promise<DeveloperCredential[]> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/credentials`);
  }

  createCredential(
    workspaceId: string,
    body: { name: string; channelIds: string[]; allowDms: boolean },
    idempotencyKey: string,
  ): Promise<DeveloperCredentialSecret> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/credentials`, {
      method: 'POST',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  rotateCredential(workspaceId: string, credentialId: string, idempotencyKey: string): Promise<DeveloperCredentialSecret> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/credentials/${credentialId}/rotate`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  revokeCredential(workspaceId: string, credentialId: string): Promise<DeveloperCredential> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/credentials/${credentialId}/revoke`, {
      method: 'POST',
    });
  }

  explore(
    workspaceId: string,
    body: { credentialId: string; operation: string; channelId?: string },
  ): Promise<DeveloperExploreResult> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/explore`, {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  inspectWebhook(workspaceId: string): Promise<{ synthetic: boolean; delivered: boolean; eventType: string }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/webhook-inspector`, { method: 'POST' });
  }

  usage(workspaceId: string): Promise<DeveloperUsage> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/usage`);
  }
}
