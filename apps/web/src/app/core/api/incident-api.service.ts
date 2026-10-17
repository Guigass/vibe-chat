import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export type IncidentStatus = 'declared' | 'mitigating' | 'resolved';
export type IncidentSeverity = 'sev1' | 'sev2' | 'sev3' | 'sev4';

export interface IncidentStep {
  id: string;
  index: number;
  code: string;
  status: 'pending' | 'done' | 'failed';
  errorCode: string | null;
  automationId: string | null;
}

export interface IncidentTimelineItem {
  id: string;
  kind: string;
  sourceId: string;
  createdAt: string;
  note: string | null;
}

export interface IncidentDetail {
  id: string;
  channelId: string;
  title: string;
  severity: IncidentSeverity;
  status: IncidentStatus;
  commanderUserId: string;
  correlationId: string | null;
  declaredAt: string;
  mitigatingAt: string | null;
  resolvedAt: string | null;
  canExport: boolean;
  steps: IncidentStep[];
  timeline: IncidentTimelineItem[];
}

export interface IncidentCurrent {
  canManage: boolean;
  incident: IncidentDetail | null;
}

@Injectable({ providedIn: 'root' })
export class IncidentApiService extends HttpApiClient {
  current(workspaceId: string, channelId: string): Promise<IncidentCurrent> {
    return this.request<IncidentCurrent>(
      `/api/v1/workspaces/${workspaceId}/incidents/current?channelId=${channelId}`,
    );
  }

  create(
    workspaceId: string,
    body: {
      idempotencyKey: string;
      templateKey: 'default';
      title: string;
      severity: IncidentSeverity;
    },
  ): Promise<IncidentDetail> {
    return this.request<IncidentDetail>(`/api/v1/workspaces/${workspaceId}/incidents`, {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  transition(workspaceId: string, incidentId: string, status: IncidentStatus): Promise<IncidentDetail> {
    return this.request<IncidentDetail>(`/api/v1/workspaces/${workspaceId}/incidents/${incidentId}/transitions`, {
      method: 'POST',
      body: JSON.stringify({ status }),
    });
  }

  update(workspaceId: string, incidentId: string, idempotencyKey: string, note: string): Promise<IncidentDetail> {
    return this.request<IncidentDetail>(`/api/v1/workspaces/${workspaceId}/incidents/${incidentId}/updates`, {
      method: 'POST',
      body: JSON.stringify({ idempotencyKey, note }),
    });
  }

  runStep(workspaceId: string, incidentId: string, stepId: string): Promise<IncidentDetail> {
    return this.request<IncidentDetail>(
      `/api/v1/workspaces/${workspaceId}/incidents/${incidentId}/steps/${stepId}/run`,
      { method: 'POST' },
    );
  }

  postmortem(workspaceId: string, incidentId: string): Promise<Record<string, unknown>> {
    return this.request<Record<string, unknown>>(
      `/api/v1/workspaces/${workspaceId}/incidents/${incidentId}/postmortem`,
    );
  }
}
