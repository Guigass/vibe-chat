import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface DlpLabelView {
  labelKey: string | null;
  inspectable: boolean;
  notInspectable: boolean;
  simulation: boolean;
}

export interface DlpPolicyView {
  simulation: boolean;
  version: number;
}

export interface DlpMetricsView {
  total: number;
  simulated: number;
  blocked: number;
}

@Injectable({ providedIn: 'root' })
export class DlpApiService extends HttpApiClient {
  label(workspaceId: string, channelId: string): Promise<DlpLabelView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/dlp/label?channelId=${channelId}`);
  }

  policy(workspaceId: string): Promise<DlpPolicyView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/dlp/policy`);
  }

  setPolicy(workspaceId: string, simulation: boolean): Promise<DlpPolicyView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/dlp/policy`, {
      method: 'PUT',
      body: JSON.stringify({ simulation }),
    });
  }

  metrics(workspaceId: string): Promise<DlpMetricsView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/dlp/metrics`);
  }

  assign(workspaceId: string, scopeKind: string, scopeId: string, labelKey: string): Promise<{ labelKey: string }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/dlp/assignments`, {
      method: 'PUT',
      body: JSON.stringify({ scopeKind, scopeId: scopeId || null, labelKey }),
    });
  }
}
