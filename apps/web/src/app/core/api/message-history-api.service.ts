import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import {
  MessageHistoryPayload,
  MessageMoveLinks,
} from '../../shared/messaging/message-history';

@Injectable({ providedIn: 'root' })
export class MessageHistoryApi extends HttpApiClient {
  history(channelId: string, messageId: string): Promise<MessageHistoryPayload> {
    return this.request<MessageHistoryPayload>(
      `/api/v1/channels/${channelId}/messages/${messageId}/history`,
    );
  }

  links(channelId: string, messageId: string): Promise<MessageMoveLinks> {
    return this.request<MessageMoveLinks>(
      `/api/v1/channels/${channelId}/messages/${messageId}/move`,
    );
  }

  move(
    channelId: string,
    messageId: string,
    targetChannelId: string,
    scope: 'message' | 'thread',
  ): Promise<void> {
    return this.request(
      `/api/v1/channels/${channelId}/messages/${messageId}/move`,
      {
        method: 'POST',
        body: JSON.stringify({
          idempotencyKey: `move-${messageId}-${targetChannelId}-${scope}`,
          targetChannelId,
          scope,
        }),
      },
    );
  }
}
