import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface QuarantineItem {
  id: string;
  channelId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  scanStatus: string;
  scanReason: string | null;
  scannedAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class QuarantineApiService extends HttpApiClient {
  async list(workspaceId: string): Promise<QuarantineItem[]> {
    const body = await this.request<{ items: QuarantineItem[] }>(
      `/api/v1/workspaces/${workspaceId}/quarantine`,
    );
    return body.items ?? [];
  }

  release(workspaceId: string, attachmentId: string, justification: string): Promise<void> {
    return this.request(`/api/v1/workspaces/${workspaceId}/quarantine/${attachmentId}/release`, {
      method: 'POST',
      body: JSON.stringify({ justification }),
    });
  }

  delete(workspaceId: string, attachmentId: string, justification: string): Promise<void> {
    return this.request(`/api/v1/workspaces/${workspaceId}/quarantine/${attachmentId}/delete`, {
      method: 'POST',
      body: JSON.stringify({ justification }),
    });
  }
}
