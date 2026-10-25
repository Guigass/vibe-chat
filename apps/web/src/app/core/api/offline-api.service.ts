import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface OfflineSyncItem {
  kind: 'message' | 'task';
  conversationId: string;
  channelId: string;
  id: string;
  seq: number;
  version: number;
  body: string | null;
  reason: string | null;
}

export interface OfflineSyncPageDto {
  protocolVersion: number;
  serverTime: string;
  cursor: string;
  changes: OfflineSyncItem[];
  tombstones: OfflineSyncItem[];
  hasMore: boolean;
  rebuild: { reason: string } | null;
  authorizedConversationIds: string[];
  conversationCoverage: 'complete' | 'partial';
}

@Injectable({ providedIn: 'root' })
export class OfflineApiService extends HttpApiClient {
  sync(deviceId: string, cursor?: string | null): Promise<OfflineSyncPageDto> {
    const params = new URLSearchParams({ deviceId });
    if (cursor) params.set('cursor', cursor);
    return this.request(`/api/v1/sync?${params.toString()}`);
  }
}
