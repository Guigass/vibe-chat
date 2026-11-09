import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface CanvasPeer {
  userId: string;
  label: string;
  cursor: number;
}

export interface CanvasSnapshot {
  id: string;
  seq: number;
  epoch: number;
  createdAt: string;
}

export interface CanvasState {
  wireVersion: number;
  epoch: number;
  seq: number;
  compactedThroughSeq: number;
  stateBase64: string;
  body: string;
  peers: CanvasPeer[];
  snapshots: CanvasSnapshot[];
}

export interface CanvasUpdateItem {
  seq: number;
  epoch: number;
  authorUserId: string;
  updateBase64: string;
}

export interface CanvasUpdateList {
  epoch: number;
  compactedThroughSeq: number;
  items: CanvasUpdateItem[];
}

export interface CanvasWrite {
  epoch: number;
  seq: number;
  body: string;
  stateBase64: string;
  replayed: boolean;
}

export interface CanvasPublish {
  wireVersion: number;
  idempotencyKey: string;
  epoch: number;
  updateBase64: string;
}

@Injectable({ providedIn: 'root' })
export class CanvasApiService extends HttpApiClient {
  open(workspaceId: string, pageId: string): Promise<CanvasState> {
    return this.request<CanvasState>(`/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}/canvas`);
  }

  updates(workspaceId: string, pageId: string, afterSeq: number, epoch: number): Promise<CanvasUpdateList> {
    return this.request<CanvasUpdateList>(
      `/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}/canvas/updates?afterSeq=${afterSeq}&epoch=${epoch}`,
    );
  }

  publish(workspaceId: string, pageId: string, body: CanvasPublish): Promise<CanvasWrite> {
    return this.request<CanvasWrite>(`/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}/canvas/updates`, {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  presence(workspaceId: string, pageId: string, cursor: number): Promise<void> {
    return this.request<void>(`/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}/canvas/presence`, {
      method: 'POST',
      body: JSON.stringify({ cursor }),
    });
  }

  snapshot(workspaceId: string, pageId: string): Promise<CanvasSnapshot> {
    return this.request<CanvasSnapshot>(`/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}/canvas/snapshots`, {
      method: 'POST',
    });
  }

  restore(workspaceId: string, pageId: string, snapshotId: string): Promise<CanvasWrite> {
    return this.request<CanvasWrite>(`/api/v1/workspaces/${workspaceId}/knowledge/pages/${pageId}/canvas/restore`, {
      method: 'POST',
      body: JSON.stringify({ snapshotId }),
    });
  }
}
