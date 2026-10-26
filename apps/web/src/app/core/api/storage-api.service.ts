import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface StorageKindUsage {
  kind: string;
  reservedBytes: number;
  usedBytes: number;
  previousUsedBytes: number;
  softLimit: number;
  hardLimit: number;
  trend: string;
  action: string;
}

export interface StorageUsage {
  kinds: StorageKindUsage[];
  largest: string | null;
}

@Injectable({ providedIn: 'root' })
export class StorageApiService extends HttpApiClient {
  usage(workspaceId: string): Promise<StorageUsage> {
    return this.request(`/api/v1/workspaces/${workspaceId}/storage`);
  }

  reconcile(workspaceId: string, idempotencyKey: string): Promise<{ corrected: unknown[] }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/storage/reconcile`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: '{}',
    });
  }

  collect(workspaceId: string, idempotencyKey: string): Promise<{ dryRun: boolean; candidates: string[]; deleted: string[] }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/storage/gc`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({ dryRun: true }),
    });
  }
}
