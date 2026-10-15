import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface DigestSettings {
  enabled: boolean;
  cadence: 'daily' | 'weekly';
  localTime: string;
  weekday: number;
  timeZone: string;
  delivery: 'in_app' | 'email' | 'push';
  includeAi: boolean;
  channelIds: string[];
}

export interface DigestItem {
  sourceId: string;
  channelId: string;
  channelName: string;
  messageId?: string | null;
  sequence?: number | null;
}

export interface DigestSection {
  kind: 'unread' | 'mention' | 'announcement' | 'decision' | 'task';
  items: DigestItem[];
}

export interface DigestView {
  id: string;
  status: 'ready' | 'partial' | 'failed';
  aiStatus: 'off' | 'ready' | 'failed';
  summary?: string | null;
  delivery: string;
  deliveryCount: number;
  externalDeferred: boolean;
  sections: DigestSection[];
}

@Injectable({ providedIn: 'root' })
export class DigestApiService extends HttpApiClient {
  getSettings(): Promise<DigestSettings> {
    return this.request<DigestSettings>('/api/v1/notifications/digests/settings');
  }

  saveSettings(body: DigestSettings): Promise<DigestSettings> {
    return this.request<DigestSettings>('/api/v1/notifications/digests/settings', {
      method: 'PUT',
      body: JSON.stringify(body),
    });
  }

  catchUp(workspaceId: string, idempotencyKey: string): Promise<DigestView> {
    return this.request<DigestView>(`/api/v1/workspaces/${workspaceId}/digests/catch-up`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({}),
    });
  }
}
