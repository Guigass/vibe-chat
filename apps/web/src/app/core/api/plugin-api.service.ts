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

export interface PluginRegistryEntry {
  accepted: boolean;
  reason?: string | null;
  pluginId: string;
  version: string;
  publisher: string;
  source: string;
  signatureKeyId: string;
  signatureFingerprint: string;
  capabilities: string[];
  risk: string;
  advisory?: string | null;
}

export interface PluginRegistryInstallView {
  id: string;
  pluginId: string;
  version: string;
  previousVersion?: string | null;
  publisher: string;
  source: string;
  signatureKeyId: string;
  signatureFingerprint: string;
  capabilities: string[];
  risk: string;
  status: string;
  token?: string | null;
}

export interface PluginRegistryChange {
  index: string;
  pluginId: string;
  version: string;
  grantCapabilities?: string[];
  confirmCapabilities?: string[];
  channelIds?: string[];
  allowDms?: boolean;
  idempotencyKey: string;
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

  listPluginRegistry(workspaceId: string): Promise<{ installs: PluginRegistryInstallView[] }> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/plugin-registry`);
  }

  previewPluginRegistry(workspaceId: string, index: string): Promise<{ entries: PluginRegistryEntry[] }> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/plugin-registry/preview`, {
      method: 'POST',
      body: JSON.stringify({ index }),
    });
  }

  installPluginRegistry(
    workspaceId: string,
    input: PluginRegistryChange,
  ): Promise<PluginRegistryInstallView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/plugin-registry/install`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  updatePluginRegistry(workspaceId: string, input: PluginRegistryChange): Promise<PluginRegistryInstallView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/plugin-registry/update`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  rollbackPluginRegistry(workspaceId: string, pluginId: string, idempotencyKey: string): Promise<PluginRegistryInstallView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/plugin-registry/rollback`, {
      method: 'POST',
      body: JSON.stringify({ pluginId, idempotencyKey }),
    });
  }

  revokePluginRegistry(
    workspaceId: string,
    pluginId: string,
    version: string,
    idempotencyKey: string,
  ): Promise<PluginRegistryInstallView> {
    return this.request(`/api/v1/admin/workspaces/${workspaceId}/plugin-registry/revoke`, {
      method: 'POST',
      body: JSON.stringify({ pluginId, version, idempotencyKey }),
    });
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
