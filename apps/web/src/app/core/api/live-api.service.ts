import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface LiveHealth {
  enabled: boolean;
  provider: string;
  healthy: boolean;
  videoEnabled: boolean;
  shareEnabled: boolean;
  recordingEnabled: boolean;
  canShare: boolean;
  canRecord: boolean;
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
  sharingScreen: boolean;
  reaction: string | null;
}

export interface LiveRecordingView {
  id: string;
  state: string;
  startedBy: string;
  startedByName: string;
  expiresAt: string | null;
  onHold: boolean;
}

export interface LiveState {
  sessionId: string;
  state: string;
  videoAllowed: boolean;
  participants: LiveParticipant[];
  recording: LiveRecordingView | null;
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

  livePolicy(workspaceId: string, body: { shareEnabled: boolean; recordingEnabled: boolean }): Promise<LiveHealth> {
    return this.request(`/api/v1/workspaces/${workspaceId}/live/policy`, {
      method: 'PUT',
      body: JSON.stringify(body),
    });
  }

  liveShare(channelId: string, sessionId: string): Promise<{ sharing: boolean }> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/share`, { method: 'POST' });
  }

  liveShareStop(channelId: string, sessionId: string): Promise<{ sharing: boolean }> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/share/stop`, { method: 'POST' });
  }

  liveShareRevoke(channelId: string, sessionId: string, userId: string): Promise<{ sharing: boolean }> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/share/revoke`, {
      method: 'POST',
      body: JSON.stringify({ userId }),
    });
  }

  liveRecord(channelId: string, sessionId: string, idempotencyKey: string): Promise<LiveRecordingView> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/recordings`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  liveConsent(channelId: string, sessionId: string, recordingId: string, decision: 'accept' | 'decline'): Promise<LiveRecordingView> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/recordings/${recordingId}/consent`, {
      method: 'POST',
      body: JSON.stringify({ decision }),
    });
  }

  liveStopRecording(channelId: string, sessionId: string, recordingId: string): Promise<LiveRecordingView> {
    return this.request(`/api/v1/channels/${channelId}/live/sessions/${sessionId}/recordings/${recordingId}/stop`, {
      method: 'POST',
    });
  }
}
