import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface TopicItem {
  id: string;
  channelId: string;
  title: string;
  slug: string;
  state: 'Open' | 'Closed' | string;
  tags: string[];
  participantNames: string[];
  following: boolean;
  unreadCount: number;
  lastActivityAt: string;
  canPost: boolean;
}

export interface TopicPage {
  items: TopicItem[];
  nextCursor?: string | null;
  canManage: boolean;
}

export interface TopicMessage {
  id: string;
  topicId: string;
  channelId: string;
  sequence: number;
  authorId: string;
  authorName: string;
  body: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class TopicApiService extends HttpApiClient {
  list(channelId: string, filter: string): Promise<TopicPage> {
    return this.request<TopicPage>(`/api/v1/channels/${channelId}/topics?filter=${encodeURIComponent(filter)}&limit=50`);
  }

  create(channelId: string, title: string, tags: string[]): Promise<TopicItem> {
    return this.request<TopicItem>(`/api/v1/channels/${channelId}/topics`, {
      method: 'POST',
      body: JSON.stringify({ title, tags, idempotencyKey: `topic-${crypto.randomUUID()}` }),
    });
  }

  follow(topicId: string, following: boolean): Promise<void> {
    return this.request<void>(`/api/v1/topics/${topicId}/subscription`, { method: following ? 'POST' : 'DELETE' });
  }

  close(topicId: string, closed: boolean): Promise<TopicItem> {
    return this.request<TopicItem>(`/api/v1/topics/${topicId}/${closed ? 'close' : 'reopen'}`, {
      method: 'POST',
      body: JSON.stringify({ idempotencyKey: `state-${crypto.randomUUID()}` }),
    });
  }

  messages(topicId: string): Promise<TopicMessage[]> {
    return this.request<TopicMessage[]>(`/api/v1/topics/${topicId}/messages?limit=80`);
  }

  send(topicId: string, body: string): Promise<TopicMessage> {
    return this.request<TopicMessage>(`/api/v1/topics/${topicId}/messages`, {
      method: 'POST',
      body: JSON.stringify({
        messageId: crypto.randomUUID(),
        idempotencyKey: `msg-${crypto.randomUUID()}`,
        body,
      }),
    });
  }

  read(topicId: string, lastReadSequence: number): Promise<void> {
    return this.request<void>(`/api/v1/topics/${topicId}/read-cursor`, {
      method: 'PUT',
      body: JSON.stringify({ lastReadSequence }),
    });
  }
}
