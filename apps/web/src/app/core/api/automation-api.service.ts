import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface AutomationDefinitionInput {
  contractVersion: 'automation.definition.v1';
  trigger: {
    type: 'message';
    channelIds: string[];
    formIds: string[];
    event: 'created';
    intervalMinutes: 0;
  };
  conditions: [];
  actions: Array<{
    type: 'post_message';
    capability: 'message.send';
    channelId: string;
    body: string;
    title: string;
    taskId: null;
    state: string;
    userIds: string[];
    connectorId: null;
  }>;
  channelIds: string[];
  secretRefs: string[];
}

export interface AutomationItem {
  id: string;
  name: string;
  status: string;
  enabled: boolean;
  publishedVersion: number;
  triggerType: string;
  identityUserId?: string | null;
  identityName?: string | null;
  capabilities: string[];
  channelIds: string[];
  circuitOpenUntil?: string | null;
  webhookToken?: string | null;
  updatedAt: string;
}

export interface AutomationStep {
  index: number;
  action: string;
  status: string;
  errorCode?: string | null;
  effectId?: string | null;
}

export interface AutomationRun {
  id: string;
  version: number;
  mode: string;
  status: string;
  depth: number;
  errorCode?: string | null;
  eventJson: string;
  steps: AutomationStep[];
}

@Injectable({ providedIn: 'root' })
export class AutomationApiService extends HttpApiClient {
  list(workspaceId: string): Promise<{ items: AutomationItem[] }> {
    return this.request<{ items: AutomationItem[] }>(`/api/v1/workspaces/${workspaceId}/automations`);
  }

  create(workspaceId: string, name: string, definition: AutomationDefinitionInput): Promise<AutomationItem> {
    return this.request<AutomationItem>(`/api/v1/workspaces/${workspaceId}/automations`, {
      method: 'POST',
      body: JSON.stringify({ idempotencyKey: crypto.randomUUID(), name, definition }),
    });
  }

  publish(workspaceId: string, automationId: string): Promise<AutomationItem> {
    return this.request<AutomationItem>(`/api/v1/workspaces/${workspaceId}/automations/${automationId}/publish`, { method: 'POST' });
  }

  setEnabled(workspaceId: string, automationId: string, enabled: boolean): Promise<AutomationItem> {
    const action = enabled ? 'enable' : 'disable';
    return this.request<AutomationItem>(`/api/v1/workspaces/${workspaceId}/automations/${automationId}/${action}`, { method: 'POST' });
  }

  dryRun(workspaceId: string, automationId: string, channelId: string): Promise<AutomationRun> {
    return this.request<AutomationRun>(`/api/v1/workspaces/${workspaceId}/automations/${automationId}/dry-run`, {
      method: 'POST',
      body: JSON.stringify({
        eventId: crypto.randomUUID(),
        type: 'message',
        eventName: 'created',
        channelId,
        state: 'created',
      }),
    });
  }

  runs(workspaceId: string, automationId: string): Promise<{ items: AutomationRun[] }> {
    return this.request<{ items: AutomationRun[] }>(`/api/v1/workspaces/${workspaceId}/automations/${automationId}/runs`);
  }
}
