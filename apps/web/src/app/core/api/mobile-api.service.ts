import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface MobileShell {
  wrapper: string;
  wrapperVersion: string;
  uiFork: boolean;
  apiBase: string;
  scheme: string;
  schemeVersion: number;
  targets: string[];
  capabilities: string[];
  biometricLockDefault: boolean;
  distribution: string;
  store: string;
  pushContent: string;
  browserFallback: boolean;
  sideloadReady: boolean;
}

export interface MobileDevice {
  id: string;
  platform: string;
  appVersion: string;
  biometricLock: boolean;
  notificationsEnabled: boolean;
  cameraEnabled: boolean;
  filePickerEnabled: boolean;
  revoked: boolean;
  wipeSecureStorage: boolean;
  createdAt: string;
}

export interface MobilePreferences {
  biometricLock: boolean;
  notifications: boolean;
  camera: boolean;
  filePicker: boolean;
}

@Injectable({ providedIn: 'root' })
export class MobileApiService extends HttpApiClient {
  mobileShell(): Promise<MobileShell> {
    return this.request('/api/v1/mobile/shell');
  }

  validateMobileLink(url: string): Promise<{ accepted: boolean; host: string; kind: string; id: string }> {
    return this.request('/api/v1/mobile/deep-links/validate', {
      method: 'POST',
      body: JSON.stringify({ url }),
    });
  }

  validateMobileShare(body: {
    host: string;
    kind: string;
    id: string;
    confirm: boolean;
  }): Promise<{ accepted: boolean; sendsMessage: boolean }> {
    return this.request('/api/v1/mobile/share/validate', {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  previewMobilePush(kind: string, conversationId: string): Promise<{ accepted: boolean; containsContent: boolean }> {
    return this.request('/api/v1/mobile/push/preview', {
      method: 'POST',
      body: JSON.stringify({ kind, conversationId }),
    });
  }

  listMobileDevices(): Promise<MobileDevice[]> {
    return this.request('/api/v1/mobile/devices');
  }

  registerMobileDevice(platform: string, appVersion: string, idempotencyKey: string): Promise<MobileDevice> {
    return this.request('/api/v1/mobile/devices', {
      method: 'POST',
      body: JSON.stringify({ platform, appVersion }),
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  saveMobilePreferences(deviceId: string, body: MobilePreferences, idempotencyKey: string): Promise<MobileDevice> {
    return this.request(`/api/v1/mobile/devices/${deviceId}/preferences`, {
      method: 'PUT',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  logoutMobileDevice(deviceId: string, idempotencyKey: string): Promise<MobileDevice> {
    return this.request(`/api/v1/mobile/devices/${deviceId}`, {
      method: 'DELETE',
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }
}
