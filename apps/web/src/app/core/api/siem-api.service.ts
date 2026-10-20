import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface SiemDestinationView {
  id: string;
  name: string;
  transport: string;
  url: string;
  status: string;
  circuit: string;
  minSeverity: string;
  categories: string[];
  minimize: boolean;
  lastSuccessAt: string | null;
  checkpointAt: string;
  lagSeconds: number;
  secretMask: string | null;
  secret: string | null;
}

export interface SiemTestView {
  eventId: string;
  synthetic: boolean;
  circuit: string;
}

@Injectable({ providedIn: 'root' })
export class SiemApiService extends HttpApiClient {
  list(workspaceId: string): Promise<SiemDestinationView[]> {
    return this.request(`/api/v1/workspaces/${workspaceId}/siem/destinations`);
  }

  create(workspaceId: string, name: string, url: string, transport: string): Promise<SiemDestinationView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/siem/destinations`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify({ name, url, transport, minimize: true }),
    });
  }

  rotate(workspaceId: string, destinationId: string): Promise<SiemDestinationView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/siem/destinations/${destinationId}/rotate`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
    });
  }

  revoke(workspaceId: string, destinationId: string): Promise<SiemDestinationView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/siem/destinations/${destinationId}/revoke`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
    });
  }

  test(workspaceId: string, destinationId: string): Promise<SiemTestView> {
    return this.request(`/api/v1/workspaces/${workspaceId}/siem/destinations/${destinationId}/test`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
    });
  }

  backfill(workspaceId: string, destinationId: string): Promise<{ count: number; limit: number }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/siem/destinations/${destinationId}/backfill`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify({ limit: 20 }),
    });
  }
}
