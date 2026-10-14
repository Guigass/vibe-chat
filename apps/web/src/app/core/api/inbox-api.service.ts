import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface InboxNavigation {
  channelId: string;
  messageId?: string | null;
  threadId?: string | null;
  sequence?: number | null;
  topicId?: string | null;
}

export interface InboxItem {
  itemKey: string;
  kind: 'announcement' | 'mention' | 'dm' | 'thread' | 'saved' | 'topic';
  priority: number;
  reason: 'pending_ack' | 'mention' | 'unread_dm' | 'followed_thread' | 'saved' | 'followed_topic';
  channelId: string;
  channelName: string;
  messageId?: string | null;
  threadId?: string | null;
  topicId?: string | null;
  sequence?: number | null;
  bodyPreview: string;
  messageRemoved: boolean;
  sortAt: string;
  snoozedUntil?: string | null;
  navigation: InboxNavigation;
}

export interface InboxCounts {
  announcement: number;
  mention: number;
  direct: number;
  thread: number;
  saved: number;
  total: number;
}

export interface InboxPage {
  items: InboxItem[];
  nextCursor?: string | null;
  counts: InboxCounts;
}

export interface InboxQuery {
  type?: string;
  state?: 'open' | 'snoozed' | 'all';
  cursor?: string | null;
  limit?: number;
}

@Injectable({ providedIn: 'root' })
export class InboxApiService extends HttpApiClient {
  list(workspaceId: string, query: InboxQuery = {}): Promise<InboxPage> {
    const params = new URLSearchParams();
    if (query.type) params.set('type', query.type);
    if (query.state) params.set('state', query.state);
    if (query.cursor) params.set('cursor', query.cursor);
    if (query.limit) params.set('limit', String(query.limit));
    const queryString = params.toString();
    const suffix = queryString ? `?${queryString}` : '';
    return this.request<InboxPage>(`/api/v1/workspaces/${workspaceId}/inbox${suffix}`);
  }

  markRead(workspaceId: string, itemKey: string): Promise<void> {
    return this.request(`/api/v1/workspaces/${workspaceId}/inbox/read`, {
      method: 'POST',
      body: JSON.stringify({ itemKey }),
    });
  }

  markUnread(workspaceId: string, itemKey: string): Promise<void> {
    return this.request(`/api/v1/workspaces/${workspaceId}/inbox/unread`, {
      method: 'POST',
      body: JSON.stringify({ itemKey }),
    });
  }

  snooze(workspaceId: string, itemKey: string, until: string): Promise<void> {
    return this.request(`/api/v1/workspaces/${workspaceId}/inbox/snooze`, {
      method: 'PUT',
      body: JSON.stringify({ itemKey, until }),
    });
  }

  unsnooze(workspaceId: string, itemKey: string): Promise<void> {
    const params = new URLSearchParams({ itemKey });
    return this.request(`/api/v1/workspaces/${workspaceId}/inbox/snooze?${params.toString()}`, {
      method: 'DELETE',
    });
  }
}
