import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface ConnectorCatalogItem {
  id: string;
  scopes: string[];
  oauth: boolean;
}

export interface ConnectorInstallation {
  id: string;
  provider: string;
  status: string;
  destinationKind: string;
  destinationId: string;
  scopes: string[];
  secretMask: string | null;
  circuit: string;
  lastErrorCode: string | null;
  secretVersion: number;
  inboundPath: string;
}

export interface CreateConnectorInput {
  idempotencyKey: string;
  provider: string;
  destinationKind: string;
  destinationId: string;
  scopes: string[];
  token?: string;
  clientId?: string;
  baseUrl?: string;
}

@Injectable({ providedIn: 'root' })
export class ConnectorApiService extends HttpApiClient {
  catalog(workspaceId: string): Promise<ConnectorCatalogItem[]> {
    return this.request<ConnectorCatalogItem[]>(`/api/v1/workspaces/${workspaceId}/connectors/catalog`);
  }

  list(workspaceId: string): Promise<ConnectorInstallation[]> {
    return this.request<ConnectorInstallation[]>(`/api/v1/workspaces/${workspaceId}/connectors`);
  }

  create(workspaceId: string, body: CreateConnectorInput): Promise<ConnectorInstallation> {
    return this.request<ConnectorInstallation>(`/api/v1/workspaces/${workspaceId}/connectors`, {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  rotate(workspaceId: string, installationId: string, token: string): Promise<ConnectorInstallation> {
    return this.request<ConnectorInstallation>(`/api/v1/workspaces/${workspaceId}/connectors/${installationId}/rotate`, {
      method: 'POST',
      body: JSON.stringify({ token }),
    });
  }

  revoke(workspaceId: string, installationId: string): Promise<ConnectorInstallation> {
    return this.request<ConnectorInstallation>(`/api/v1/workspaces/${workspaceId}/connectors/${installationId}/revoke`, {
      method: 'POST',
      body: '{}',
    });
  }

  reconnect(workspaceId: string, installationId: string): Promise<{ ok: boolean; error: string | null }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/connectors/${installationId}/reconnect`, {
      method: 'POST',
      body: '{}',
    });
  }
}
