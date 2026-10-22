import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface PluginSdkCatalog {
  schemaVersion: number;
  sdkVersion: string;
  apiContract: string;
  license: string;
  sandbox: boolean;
  published: boolean;
  install: { dotnet: string; typescript: string };
  examples: { id: string; operation: string; synthetic: boolean }[];
  failures: string[];
}

export interface PluginSdkSandboxResult {
  synthetic: boolean;
  sandbox: boolean;
  delivered: boolean;
  operation: string;
  status: string;
}

@Injectable({ providedIn: 'root' })
export class SdkApiService extends HttpApiClient {
  catalog(workspaceId: string): Promise<PluginSdkCatalog> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/sdk`);
  }

  verify(
    workspaceId: string,
    body: { body: string; signature: string; deliveryId: string; deliveredAt: string; seenDeliveryIds: string[] },
  ): Promise<{ synthetic: boolean; accepted: boolean; reason: string }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/sdk/verify`, {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  contractTests(workspaceId: string): Promise<{ passed: boolean; synthetic: boolean }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/sdk/contract-tests`, { method: 'POST' });
  }

  sandbox(
    workspaceId: string,
    body: { operation: string; channelId: string; body: string; idempotencyKey: string },
  ): Promise<PluginSdkSandboxResult> {
    return this.request(`/api/v1/workspaces/${workspaceId}/developer/sdk/sandbox`, {
      method: 'POST',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': body.idempotencyKey },
    });
  }
}
