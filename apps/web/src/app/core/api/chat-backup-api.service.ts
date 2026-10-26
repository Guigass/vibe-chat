import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface ChatBackupDestination {
  id: string;
  name: string;
  kind: string;
  host: string;
  port: number;
  remotePath: string;
  username: string;
  bucket: string;
  useSsl: boolean;
  configured: boolean;
  secretMask: string | null;
  circuit: string;
}

export interface ChatBackupRun {
  id: string;
  jobId: string | null;
  destinationId: string | null;
  status: string;
  sizeBytes: number;
  checksumSha256: string;
  remoteKey: string;
  error: string;
  startedAt: string;
  finishedAt: string | null;
}

export interface ChatBackupJob {
  id: string;
  name: string;
  destinationId: string;
  paused: boolean;
  includeAttachments: boolean;
  intervalMinutes: number;
  cron: string;
  retentionCount: number;
  retentionDays: number;
  nextRunAt: string | null;
  lastRun: ChatBackupRun | null;
}

export interface ChatBackupList {
  destinations: ChatBackupDestination[];
  jobs: ChatBackupJob[];
  runs: ChatBackupRun[];
  failed: boolean;
}

export interface ChatBackupDestinationInput {
  name: string;
  kind: string;
  host: string;
  port: number;
  remotePath: string;
  username: string;
  bucket: string;
  useSsl: boolean;
  secret: string;
}

export interface ChatBackupImportPlan {
  ok: boolean;
  packageId: string;
  insert: number;
  skip: number;
  attachments: number;
}

@Injectable({ providedIn: 'root' })
export class ChatBackupApiService extends HttpApiClient {
  list(workspaceId: string): Promise<ChatBackupList> {
    return this.request(`/api/v1/workspaces/${workspaceId}/chat-backup`);
  }

  createDestination(workspaceId: string, idempotencyKey: string, body: ChatBackupDestinationInput): Promise<ChatBackupDestination> {
    return this.request(`/api/v1/workspaces/${workspaceId}/chat-backup/destinations`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(body),
    });
  }

  testDestination(workspaceId: string, destinationId: string): Promise<{ ok: boolean }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/chat-backup/destinations/${destinationId}/test`, {
      method: 'POST',
      body: '{}',
    });
  }

  createJob(workspaceId: string, idempotencyKey: string, destinationId: string, name: string, includeAttachments: boolean): Promise<ChatBackupJob> {
    return this.request(`/api/v1/workspaces/${workspaceId}/chat-backup/jobs`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({
        name,
        destinationId,
        includeAttachments,
        intervalMinutes: 1440,
        retentionCount: 7,
        retentionDays: 30,
      }),
    });
  }

  setPaused(workspaceId: string, jobId: string, paused: boolean): Promise<ChatBackupJob> {
    return this.request(`/api/v1/workspaces/${workspaceId}/chat-backup/jobs/${jobId}`, {
      method: 'PATCH',
      body: JSON.stringify({ paused }),
    });
  }

  run(workspaceId: string, jobId: string, idempotencyKey: string): Promise<ChatBackupRun> {
    return this.request(`/api/v1/workspaces/${workspaceId}/chat-backup/jobs/${jobId}/run`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: '{}',
    });
  }

  async download(workspaceId: string, includeAttachments: boolean): Promise<void> {
    const headers = new Headers({ Accept: 'application/zip' });
    const devUser = this.auth.devUser();
    if (devUser) {
      headers.set('X-Dev-User', devUser);
    } else {
      const token = await this.auth.getAccessToken();
      if (token) {
        headers.set('Authorization', `Bearer ${token}`);
      }
    }
    const response = await fetch(
      `${this.baseUrl}/api/v1/workspaces/${workspaceId}/chat-backup/export?includeAttachments=${includeAttachments}`,
      { headers },
    );
    if (!response.ok) {
      const text = await response.text().catch(() => '');
      const error = new Error(text || `HTTP ${response.status}`) as Error & { status: number };
      error.status = response.status;
      throw error;
    }

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = 'vibechat-chat-backup.zip';
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  validateImport(workspaceId: string, file: Blob): Promise<ChatBackupImportPlan> {
    return this.request(`/api/v1/workspaces/${workspaceId}/chat-backup/import/validate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/zip' },
      body: file,
    });
  }

  executeImport(workspaceId: string, idempotencyKey: string, file: Blob): Promise<{ inserted: number }> {
    return this.request(`/api/v1/workspaces/${workspaceId}/chat-backup/import/execute?confirm=true`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/zip', 'Idempotency-Key': idempotencyKey },
      body: file,
    });
  }
}
