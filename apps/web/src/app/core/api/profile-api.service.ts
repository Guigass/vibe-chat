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
