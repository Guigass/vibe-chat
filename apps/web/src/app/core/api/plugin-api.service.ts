import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

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
}
