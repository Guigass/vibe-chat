import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export type TaskPriority = 'low' | 'normal' | 'high' | 'urgent';
export type TaskState = 'open' | 'in_progress' | 'completed';
export type TaskVisibility = 'channel' | 'restricted';
export type TaskView = 'workspace' | 'channel' | 'mine';
export type TaskListState = 'active' | 'open' | 'in_progress' | 'completed' | 'all';

export interface TaskAssignee {
  userId: string;
  displayName: string;
}

export interface TaskChecklistItem {
  id: string;
  position: number;
  text: string;
  done: boolean;
}

export interface TaskItem {
  id: string;
  title: string;
  description: string | null;
  priority: TaskPriority;
  state: TaskState;
  version: number;
  dueAt: string | null;
  overdue: boolean;
  assignees: TaskAssignee[];
  checklist: TaskChecklistItem[];
  channelId: string;
  channelName: string;
  sourceMessageId: string;
  sourceThreadId: string | null;
  sourceSequence: number;
  sourceRedacted: boolean;
  visibility: TaskVisibility;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
}

interface TaskDto {
  id: string;
  title: string;
  description?: string | null;
  priority: TaskPriority;
  state: TaskState;
  version: number;
  dueAt?: string | null;
  overdue: boolean;
  assignees?: { userId: string; displayName?: string | null }[];
  checklist?: { id: string; position: number; text: string; done: boolean }[];
  channelId: string;
  channelName: string;
  sourceMessageId: string;
  sourceThreadId?: string | null;
  sourceSequence: number;
  sourceRedacted: boolean;
  visibility: TaskVisibility;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
}

function mapItem(dto: TaskDto): TaskItem {
  return {
    id: String(dto.id),
    title: dto.title,
    description: dto.description ?? null,
    priority: dto.priority,
    state: dto.state,
    version: dto.version,
    dueAt: dto.dueAt ?? null,
    overdue: dto.overdue,
    assignees: (dto.assignees ?? []).map((row) => ({
      userId: String(row.userId),
      displayName: row.displayName ?? '',
    })),
    checklist: (dto.checklist ?? []).map((row) => ({
      id: String(row.id),
      position: row.position,
      text: row.text,
      done: row.done,
    })),
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
    completedAt: dto.completedAt ?? null,
  };
}

@Injectable({ providedIn: 'root' })
export class TaskApiService extends HttpApiClient {
  async create(workspaceId: string, input: {
    idempotencyKey: string;
    messageId: string;
    title: string;
    description?: string | null;
    priority: TaskPriority;
    assigneeUserIds: string[];
    dueAt?: string | null;
    visibility: TaskVisibility;
    checklist: { text: string; done: boolean }[];
  }): Promise<TaskItem> {
    const dto = await this.request<TaskDto>(`/api/v1/workspaces/${workspaceId}/tasks`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
    return mapItem(dto);
  }

  async list(workspaceId: string, query: {
    view: TaskView;
    state: TaskListState;
    channelId?: string | null;
  }): Promise<TaskItem[]> {
    const params = new URLSearchParams();
    params.set('view', query.view);
    params.set('state', query.state);
    if (query.channelId) params.set('channelId', query.channelId);
    const page = await this.request<{ items: TaskDto[] }>(
      `/api/v1/workspaces/${workspaceId}/tasks?${params.toString()}`,
    );
    return (page.items ?? []).map(mapItem);
  }

  complete(workspaceId: string, id: string, version: number): Promise<TaskItem> {
    return this.transition(workspaceId, id, 'complete', version);
  }

  reopen(workspaceId: string, id: string, version: number): Promise<TaskItem> {
    return this.transition(workspaceId, id, 'reopen', version);
  }

  async patch(workspaceId: string, id: string, body: {
    version: number;
    checklist?: { text: string; done: boolean }[];
    replaceChecklist?: boolean;
  }): Promise<TaskItem> {
    const dto = await this.request<TaskDto>(`/api/v1/workspaces/${workspaceId}/tasks/${id}`, {
      method: 'PATCH',
      body: JSON.stringify({
        version: body.version,
        replaceAssignees: false,
        clearDue: false,
        replaceChecklist: body.replaceChecklist === true,
        checklist: body.checklist ?? null,
      }),
    });
    return mapItem(dto);
  }

  private async transition(workspaceId: string, id: string, action: 'complete' | 'reopen', version: number): Promise<TaskItem> {
    const dto = await this.request<TaskDto>(`/api/v1/workspaces/${workspaceId}/tasks/${id}/${action}`, {
      method: 'POST',
      body: JSON.stringify({ version }),
    });
    return mapItem(dto);
  }
}
