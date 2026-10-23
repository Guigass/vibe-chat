import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface BridgeView {
  id: string;
  channelId: string;
  network: string;
  remoteRoom: string;
  destinationHost: string;
  status: 'pending' | 'active' | 'paused' | 'disconnected';
  capabilities: string[];
  consentRequired: boolean;
  deleteRemoteGuaranteed: boolean;
  deleteLimitation: string;
  canManage: boolean;
  token?: string | null;
}

@Injectable({ providedIn: 'root' })
export class BridgeApiService extends HttpApiClient {
  channelBridge(workspaceId: string, channelId: string): Promise<BridgeView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/channels/${channelId}/bridge`);
  }

  consentBridge(workspaceId: string, bridgeId: string): Promise<BridgeView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/bridges/${bridgeId}/consent`, {
      method: 'POST',
      body: JSON.stringify({ acknowledgeCopy: true, idempotencyKey: crypto.randomUUID() }),
    });
  }

  pauseBridge(workspaceId: string, bridgeId: string): Promise<BridgeView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/bridges/${bridgeId}/pause`, { method: 'POST' });
  }

  resumeBridge(workspaceId: string, bridgeId: string): Promise<BridgeView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/bridges/${bridgeId}/resume`, { method: 'POST' });
  }

  disconnectBridge(workspaceId: string, bridgeId: string): Promise<BridgeView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/bridges/${bridgeId}/disconnect`, { method: 'POST' });
  }
}
