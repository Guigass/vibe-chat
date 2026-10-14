import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export type CommitmentKind = 'decision' | 'action_item';
export type CommitmentState = 'open' | 'closed';
export type CommitmentVisibility = 'channel' | 'restricted';

export interface CommitmentItem {
  id: string;
  kind: CommitmentKind;
  title: string;
  description: string | null;
  ownerUserId: string | null;
  ownerName: string | null;
  dueAt: string | null;
  state: CommitmentState;
  channelId: string;
  channelName: string;
  sourceMessageId: string;
  sourceThreadId: string | null;
  sourceSequence: number;
  sourceRedacted: boolean;
  visibility: CommitmentVisibility;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  closedAt: string | null;
}

export interface CommitmentChange {
  action: string;
  actorUserId: string;
  actorName: string;
  at: string;
}

interface CommitmentDto {
  id: string;
  kind: CommitmentKind;
  title: string;
  description?: string | null;
  ownerUserId?: string | null;
  ownerName?: string | null;
  dueAt?: string | null;
  state: CommitmentState;
  channelId: string;
  channelName: string;
  sourceMessageId: string;
  sourceThreadId?: string | null;
  sourceSequence: number;
  sourceRedacted: boolean;
  visibility: CommitmentVisibility;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  closedAt?: string | null;
}

function mapItem(dto: CommitmentDto): CommitmentItem {
  return {
    id: String(dto.id),
    kind: dto.kind,
    title: dto.title,
    description: dto.description ?? null,
    ownerUserId: dto.ownerUserId ? String(dto.ownerUserId) : null,
    ownerName: dto.ownerName ?? null,
    dueAt: dto.dueAt ?? null,
    state: dto.state,
    channelId: String(dto.channelId),
    channelName: dto.channelName,
    sourceMessageId: String(dto.sourceMessageId),
    sourceThreadId: dto.sourceThreadId ? String(dto.sourceThreadId) : null,
    sourceSequence: dto.sourceSequence,
    sourceRedacted: dto.sourceRedacted,
    visibility: dto.visibility,
    createdBy: String(dto.createdBy),
    createdAt: dto.createdAt,
    updatedAt: dto.updatedAt,
    closedAt: dto.closedAt ?? null,
  };
}

@Injectable({ providedIn: 'root' })
export class CommitmentApiService extends HttpApiClient {
  async create(workspaceId: string, input: {
    idempotencyKey: string;
    messageId: string;
    kind: CommitmentKind;
    title: string;
    description?: string | null;
    ownerUserId?: string | null;
    dueAt?: string | null;
    visibility: CommitmentVisibility;
  }): Promise<CommitmentItem> {
    const dto = await this.request<CommitmentDto>(`/api/v1/workspaces/${workspaceId}/commitments`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
    return mapItem(dto);
  }

  async list(workspaceId: string, query: {
    channelId?: string | null;
    ownerId?: string | null;
    kind?: CommitmentKind | null;
    state?: CommitmentState | 'all';
  }): Promise<CommitmentItem[]> {
    const params = new URLSearchParams();
    if (query.channelId) params.set('channelId', query.channelId);
    if (query.ownerId) params.set('ownerId', query.ownerId);
    if (query.kind) params.set('kind', query.kind);
    if (query.state) params.set('state', query.state);
    params.set('limit', '50');
    const dto = await this.request<{ items: CommitmentDto[] }>(
      `/api/v1/workspaces/${workspaceId}/commitments?${params.toString()}`,
    );
    return (dto.items ?? []).map(mapItem);
  }

  async close(workspaceId: string, id: string): Promise<CommitmentItem> {
    return mapItem(await this.request<CommitmentDto>(
      `/api/v1/workspaces/${workspaceId}/commitments/${id}/close`,
      { method: 'POST' },
    ));
  }

  async reopen(workspaceId: string, id: string): Promise<CommitmentItem> {
    return mapItem(await this.request<CommitmentDto>(
      `/api/v1/workspaces/${workspaceId}/commitments/${id}/reopen`,
      { method: 'POST' },
    ));
  }

  async history(workspaceId: string, id: string): Promise<CommitmentChange[]> {
    const dto = await this.request<{ items: CommitmentChange[] }>(
      `/api/v1/workspaces/${workspaceId}/commitments/${id}/history`,
    );
    return dto.items ?? [];
  }
}
