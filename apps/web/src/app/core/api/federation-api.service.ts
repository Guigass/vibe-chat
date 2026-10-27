import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface FederationView {
  id: string;
  trustId: string;
  roomId?: string | null;
  channelId?: string | null;
  domain: string;
  remoteInstanceId: string;
  remoteRoom?: string | null;
  status: 'pending' | 'active' | 'paused' | 'revoked';
  consentRequired: boolean;
  copied: string[];
  deleteRemoteGuaranteed: boolean;
  deleteLimitation: string;
  remote: boolean;
  canManage: boolean;
  identities: { remoteSubject: string; displayName: string; remoteInstanceId: string; remote: boolean }[];
}

@Injectable({ providedIn: 'root' })
export class FederationApiService extends HttpApiClient {
  channelFederation(workspaceId: string, channelId: string): Promise<FederationView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/channels/${channelId}/federation`);
  }

  consentFederation(workspaceId: string, trustId: string): Promise<FederationView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/federation/trusts/${trustId}/consent`, {
      method: 'POST',
      body: JSON.stringify({ acknowledgeCopy: true }),
    });
  }

  pauseFederation(workspaceId: string, trustId: string): Promise<FederationView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/federation/trusts/${trustId}/pause`, {
      method: 'POST',
    });
  }

  resumeFederation(workspaceId: string, trustId: string): Promise<FederationView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/federation/trusts/${trustId}/resume`, {
      method: 'POST',
    });
  }

  revokeFederation(workspaceId: string, trustId: string): Promise<FederationView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/federation/trusts/${trustId}/revoke`, {
      method: 'POST',
    });
  }
}
