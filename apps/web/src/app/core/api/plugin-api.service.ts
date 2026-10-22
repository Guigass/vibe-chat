import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import { PluginHistoryGrant } from '../../shared/models/chat.models';

export interface BuiltinPlugin {
  id: string;
  name: string;
  version: string;
  capabilities: string[];
  commands: string[];
}

export interface PluginCommandResult {
  name: string;
  posted: boolean;
  messageId?: string | null;
  sequence?: number | null;
  callbackDelivered: boolean;
}

@Injectable({ providedIn: 'root' })
export class PluginApiService extends HttpApiClient {
  listPluginBuiltins(workspaceId: string): Promise<BuiltinPlugin[]> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/plugins/builtins`);
  }

  invokePluginCommand(
    workspaceId: string,
    channelId: string,
    input: { name: string; args: string; idempotencyKey: string },
  ): Promise<PluginCommandResult> {
    return this.request(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/plugin-commands`,
      {
        method: 'POST',
        body: JSON.stringify(input),
      },
    );
  }

  grantPluginHistory(
    workspaceId: string,
    installedId: string,
    input: { channelId: string; windowStart: string; windowEnd: string },
  ): Promise<PluginHistoryGrant> {
    return this.request(
      `/api/v1/admin/workspaces/${workspaceId}/plugins/${installedId}/history-grants`,
      { method: 'POST', body: JSON.stringify(input) },
    );
  }

  revokePluginHistory(workspaceId: string, installedId: string, grantId: string): Promise<void> {
    return this.request(
      `/api/v1/admin/workspaces/${workspaceId}/plugins/${installedId}/history-grants/${grantId}`,
      { method: 'DELETE' },
    );
  }

  invokePluginAction(
    workspaceId: string,
    channelId: string,
    messageId: string,
    input: { action: string; idempotencyKey: string },
  ): Promise<{ action: string; status: string; idempotent: boolean }> {
    return this.request(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/messages/${messageId}/plugin-actions`,
      { method: 'POST', body: JSON.stringify(input) },
    );
  }
}
