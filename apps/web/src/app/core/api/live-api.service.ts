import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface LiveHealth {
  enabled: boolean;
  provider: string;
  healthy: boolean;
  videoEnabled: boolean;
  activeSessions: number;
  maxSessions: number;
  maxParticipants: number;
}

export interface LiveParticipant {
  userId: string;
  displayName: string;
  muted: boolean;
  cameraOn: boolean;
  audioConnected: boolean;
  reaction: string | null;
}

export interface LiveState {
  sessionId: string;
  state: string;
  videoAllowed: boolean;
  participants: LiveParticipant[];
}

export interface LiveGrant {
  sessionId: string;
  token: string;
  expiresAt: string;
  room: string;
  signalUrl: string | null;
  canPublishAudio: boolean;
  canPublishVideo: boolean;
}

@Injectable({ providedIn: 'root' })
export class LiveApiService extends HttpApiClient {
  liveHealth(workspaceId: string): Promise<LiveHealth> {
    return this.request(`/api/v1/workspaces/${workspaceId}/live/health`);
  }

  liveState(channelId: string): Promise<LiveState> {
    return this.request(`/api/v1/channels/${channelId}/live`);
  }

  liveStart(channelId: string, idempotencyKey: string): Promise<LiveState> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  liveJoin(channelId: string, sessionId: string): Promise<LiveGrant> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/join`, { method: 'POST' });
  }

  liveConnect(channelId: string, sessionId: string, token: string): Promise<LiveState> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/connect`, {
      method: 'POST',
      body: JSON.stringify({ token }),
    });
  }

  liveLeave(channelId: string, sessionId: string): Promise<LiveState> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/leave`, { method: 'POST' });
  }

  liveMedia(channelId: string, sessionId: string, body: { muted?: boolean; camera?: boolean }): Promise<LiveState> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/media`, {
      method: 'PUT',
      body: JSON.stringify(body),
    });
  }

  liveReact(channelId: string, sessionId: string, emoji: string): Promise<LiveState> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/reactions`, {
      method: 'POST',
      body: JSON.stringify({ emoji }),
    });
  }
}
