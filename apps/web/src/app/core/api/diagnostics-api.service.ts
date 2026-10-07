import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface DiagnosticEvidence {
  latencyMs: number | null;
  pending: number | null;
  stale: number | null;
  configured: boolean | null;
  migration: string | null;
  endpoint: string | null;
}

export interface DiagnosticCheck {
  code: string;
  version: number;
  component: string;
  status: 'Pass' | 'Warn' | 'Fail' | 'Skipped';
  severity: string;
  summary: string;
  evidence: DiagnosticEvidence;
  runbook: string;
  observedAt: string;
  correlationId: string;
}

export interface DiagnosticsReport {
  verdict: 'ready' | 'degraded' | 'action_required';
  audience: 'operator' | 'workspace';
  observedAt: string;
  correlationId: string;
  checks: DiagnosticCheck[];
}

export interface DiagnosticProbe {
  code: string;
  status: string;
  severity: string;
  summary: string;
  cleaned: boolean;
}

export interface SupportBundleJob {
  id: string;
  status: string;
  expiresAt: string;
  checksum: string;
  downloadsRemaining: number;
  idempotent: boolean;
}

export interface SupportRepairJob {
  id: string;
  action: string;
  status: string;
  dryRun: boolean;
  estimated: number;
  written: number;
  idempotent: boolean;
}

@Injectable({ providedIn: 'root' })
export class DiagnosticsApiService extends HttpApiClient {
  getDiagnostics(workspaceId: string): Promise<DiagnosticsReport> {
    return this.request<DiagnosticsReport>(`/api/v1/workspaces/${workspaceId}/diagnostics`);
  }

  probeDiagnostic(workspaceId: string, kind: 'email' | 'push' | 'storage'): Promise<DiagnosticProbe> {
    return this.request<DiagnosticProbe>(`/api/v1/workspaces/${workspaceId}/diagnostics/probes/${kind}`, {
      method: 'POST',
    });
  }

  createSupportBundle(workspaceId: string, idempotencyKey: string): Promise<SupportBundleJob> {
    return this.request<SupportBundleJob>(`/api/v1/workspaces/${workspaceId}/diagnostics/bundles`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({ windowMinutes: 60 }),
    });
  }

  createRepair(workspaceId: string, idempotencyKey: string, dryRun: boolean, confirm: boolean): Promise<SupportRepairJob> {
    return this.request<SupportRepairJob>(`/api/v1/workspaces/${workspaceId}/diagnostics/repairs`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({ action: 'search.reindex', dryRun, confirm }),
    });
  }

  applyRepair(workspaceId: string, repairId: string): Promise<SupportRepairJob> {
    return this.request<SupportRepairJob>(`/api/v1/workspaces/${workspaceId}/diagnostics/repairs/${repairId}/apply`, {
      method: 'POST',
    });
  }

  cancelRepair(workspaceId: string, repairId: string): Promise<SupportRepairJob> {
    return this.request<SupportRepairJob>(`/api/v1/workspaces/${workspaceId}/diagnostics/repairs/${repairId}/cancel`, {
      method: 'POST',
    });
  }
}
