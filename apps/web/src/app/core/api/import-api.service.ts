import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface ImportReport {
  principals: number;
  spaces: number;
  channels: number;
  threads: number;
  messages: number;
  attachments: number;
  quarantined: number;
  ignored: number;
  redacted: number;
  failed: number;
  blocking: boolean;
  warnings: string[];
  conflicts: string[];
}

export interface ImportJob {
  id: string;
  status: string;
  adapter: string;
  createdAt: string;
  updatedAt: string;
  idempotent: boolean;
  report: ImportReport;
}

@Injectable({ providedIn: 'root' })
export class ImportApiService extends HttpApiClient {
  createImport(workspaceId: string, adapter: string, document: unknown, idempotencyKey: string): Promise<ImportJob> {
    return this.request<ImportJob>(`/api/v1/workspaces/${workspaceId}/imports`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({ adapter, document }),
    });
  }

  getImport(workspaceId: string, importId: string): Promise<ImportJob> {
    return this.request<ImportJob>(`/api/v1/workspaces/${workspaceId}/imports/${importId}`);
  }

  planImport(workspaceId: string, importId: string): Promise<ImportJob> {
    return this.request<ImportJob>(`/api/v1/workspaces/${workspaceId}/imports/${importId}/plan`, { method: 'POST' });
  }

  executeImport(workspaceId: string, importId: string): Promise<ImportJob> {
    return this.request<ImportJob>(`/api/v1/workspaces/${workspaceId}/imports/${importId}/execute`, { method: 'POST' });
  }

  pauseImport(workspaceId: string, importId: string): Promise<ImportJob> {
    return this.request<ImportJob>(`/api/v1/workspaces/${workspaceId}/imports/${importId}/pause`, { method: 'POST' });
  }

  resumeImport(workspaceId: string, importId: string): Promise<ImportJob> {
    return this.request<ImportJob>(`/api/v1/workspaces/${workspaceId}/imports/${importId}/resume`, { method: 'POST' });
  }

  publishImport(workspaceId: string, importId: string): Promise<ImportJob> {
    return this.request<ImportJob>(`/api/v1/workspaces/${workspaceId}/imports/${importId}/publish`, { method: 'POST' });
  }

  rollbackImport(workspaceId: string, importId: string, confirm: boolean): Promise<ImportJob> {
    return this.request<ImportJob>(`/api/v1/workspaces/${workspaceId}/imports/${importId}/rollback`, {
      method: 'POST',
      body: JSON.stringify({ confirm }),
    });
  }
}
