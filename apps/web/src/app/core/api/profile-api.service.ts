import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface MeProfile {
  userId: string;
  subject: string;
  email: string;
  displayName: string;
  roles: string[];
  locale: string | null;
  chatWallpaperId?: string | null;
  accentColorId?: string | null;
}

export interface AppearancePreference {
  chatWallpaperId: string | null;
  accentColorId: string | null;
}

export interface MemberPublicProfile {
  userId: string;
  displayName: string;
  email: string | null;
  jobTitle: string | null;
  about: string | null;
  highlightMessage: string | null;
  avatarUrl: string | null;
}

export interface UpdateMemberProfileInput {
  displayName: string;
  jobTitle?: string | null;
  about?: string | null;
  highlightMessage?: string | null;
}

@Injectable({ providedIn: 'root' })
export class ProfileApiService extends HttpApiClient {
  async getMe(): Promise<MeProfile> {
    return this.request<MeProfile>('/api/v1/me');
  }

  async updateMe(input: { locale: string }): Promise<MeProfile> {
    return this.request<MeProfile>('/api/v1/me', {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  }

  async getMemberProfile(workspaceId: string, userId: string): Promise<MemberPublicProfile> {
    return this.request<MemberPublicProfile>(
      `/api/v1/workspaces/${workspaceId}/members/${userId}/profile`,
    );
  }

  async updateMemberProfile(
    input: UpdateMemberProfileInput,
    idempotencyKey: string,
  ): Promise<MemberPublicProfile> {
    return this.request<MemberPublicProfile>('/api/v1/me/profile', {
      method: 'PUT',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(input),
    });
  }

  async uploadAvatar(file: File): Promise<MemberPublicProfile> {
    const body = new FormData();
    body.append('file', file);
    return this.request<MemberPublicProfile>('/api/v1/me/profile/avatar', {
      method: 'POST',
      body,
    });
  }

  async deleteAvatar(): Promise<MemberPublicProfile> {
    return this.request<MemberPublicProfile>('/api/v1/me/profile/avatar', { method: 'DELETE' });
  }

  fetchAvatar(path: string): Promise<Blob | null> {
    return this.requestBlob(path);
  }

  async updateAppearance(
    userId: string,
    input: { chatWallpaperId: string | null; accentColorId: string | null },
  ): Promise<AppearancePreference> {
    return this.request<AppearancePreference>(`/api/v1/users/${userId}/appearance`, {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  }
}
