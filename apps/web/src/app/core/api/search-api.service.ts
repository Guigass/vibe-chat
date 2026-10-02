import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import {
  SearchMessagesResult,
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
}
