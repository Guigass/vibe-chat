import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface DesktopShell {
  wrapper: string;
  wrapperVersion: string;
  uiFork: boolean;
  apiBase: string;
  scheme: string;
  schemeVersion: number;
  targets: string[];
  capabilities: string[];
  launchAtLoginDefault: boolean;
  updates: string;
  signedBuilds: boolean;
  signing: string;
  browserFallback: boolean;
}

export interface DesktopDevice {
  id: string;
  platform: string;
  appVersion: string;
  launchAtLogin: boolean;
  notificationsEnabled: boolean;
  trayEnabled: boolean;
  revoked: boolean;
  wipeSecureStorage: boolean;
  createdAt: string;
}

export interface DesktopPreferences {
  launchAtLogin: boolean;
  notifications: boolean;
  tray: boolean;
}

@Injectable({ providedIn: 'root' })
export class DesktopApiService extends HttpApiClient {
  desktopShell(): Promise<DesktopShell> {
    return this.request('/api/v1/desktop/shell');
  }

  validateDesktopLink(url: string): Promise<{ accepted: boolean; host: string; kind: string; id: string }> {
    return this.request('/api/v1/desktop/deep-links/validate', {
      method: 'POST',
      body: JSON.stringify({ url }),
    });
  }

  verifyDesktopUpdate(body: {
    channel: string;
    productVersion: string;
    feedHost: string;
    targets: string[];
    sha256: string;
    signature: string;
  }): Promise<{ accepted: boolean }> {
    return this.request('/api/v1/desktop/updates/verify', {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  listDesktopDevices(): Promise<DesktopDevice[]> {
    return this.request('/api/v1/desktop/devices');
  }

  registerDesktopDevice(platform: string, appVersion: string, idempotencyKey: string): Promise<DesktopDevice> {
    return this.request('/api/v1/desktop/devices', {
      method: 'POST',
      body: JSON.stringify({ platform, appVersion }),
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  saveDesktopPreferences(deviceId: string, body: DesktopPreferences, idempotencyKey: string): Promise<DesktopDevice> {
    return this.request(`/api/v1/desktop/devices/${deviceId}/preferences`, {
      method: 'PUT',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  logoutDesktopDevice(deviceId: string, idempotencyKey: string): Promise<DesktopDevice> {
    return this.request(`/api/v1/desktop/devices/${deviceId}`, {
      method: 'DELETE',
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }
}
