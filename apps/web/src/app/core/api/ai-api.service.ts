import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import {
  AiSuggestReplyResult,
  AiSummaryResult,
} from '../../shared/models/chat.models';

@Injectable({ providedIn: 'root' })
export class AiApiService extends HttpApiClient {
  async summarizeChannel(workspaceId: string, channelId: string): Promise<AiSummaryResult> {
    const result = await this.request<{ summary: string }>(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/ai/summarize`,
      {
        method: 'POST',
        body: JSON.stringify({}),
      },
    );
    return {
      channelId,
      summary: result.summary,
      messageCount: 0,
      generatedAt: new Date().toISOString(),
    };
  }

  async suggestChannelReply(workspaceId: string, channelId: string): Promise<AiSuggestReplyResult> {
    const result = await this.request<{ suggestion: string }>(
      `/api/v1/workspaces/${workspaceId}/channels/${channelId}/ai/suggest-reply`,
      {
        method: 'POST',
        body: JSON.stringify({}),
      },
    );
    return {
      channelId,
      suggestion: result.suggestion,
      generatedAt: new Date().toISOString(),
    };
  }
}
