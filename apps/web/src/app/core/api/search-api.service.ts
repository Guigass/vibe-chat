import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import {
  SearchMessagesResult,
  SemanticQueryResult,
} from '../../shared/models/chat.models';

@Injectable({ providedIn: 'root' })
export class SearchApiService extends HttpApiClient {
  async searchMessages(input: {
    workspaceId: string;
    q: string;
    channelId?: string;
    authorId?: string;
    from?: string;
    to?: string;
    hasAttachment?: boolean;
    hasLink?: boolean;
    attachmentKind?: 'image' | 'audio' | 'document';
    sort?: 'relevance' | 'date';
    cursor?: string;
    limit?: number;
  }): Promise<SearchMessagesResult> {
    const params = new URLSearchParams({
      workspaceId: input.workspaceId,
      q: input.q,
    });
    if (input.channelId) {
      params.set('channelId', input.channelId);
    }
    if (input.authorId) {
      params.set('authorId', input.authorId);
    }
    if (input.from) {
      params.set('from', input.from);
    }
    if (input.to) {
      params.set('to', input.to);
    }
    if (input.hasAttachment != null) {
      params.set('hasAttachment', String(input.hasAttachment));
    }
    if (input.hasLink != null) {
      params.set('hasLink', String(input.hasLink));
    }
    if (input.attachmentKind) {
      params.set('attachmentKind', input.attachmentKind);
    }
    if (input.sort) {
      params.set('sort', input.sort);
    }
    if (input.cursor) {
      params.set('cursor', input.cursor);
    }
    if (input.limit) {
      params.set('limit', String(input.limit));
    }
    return this.request<SearchMessagesResult>(`/api/v1/search/messages?${params.toString()}`);
  }

  semanticSettings(workspaceId: string): Promise<{ enabled: boolean }> {
    return this.request<{ enabled: boolean }>(`/api/v1/workspaces/${workspaceId}/semantic/settings`);
  }

  semanticQuery(workspaceId: string, q: string): Promise<SemanticQueryResult> {
    return this.request<SemanticQueryResult>(`/api/v1/workspaces/${workspaceId}/semantic/query`, {
      method: 'POST',
      body: JSON.stringify({ q }),
    });
  }

  reportSemantic(workspaceId: string, citationIds: string[]): Promise<{ reported: boolean }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/semantic/report`, {
      method: 'POST',
      body: JSON.stringify({ citationIds }),
    });
  }
}
