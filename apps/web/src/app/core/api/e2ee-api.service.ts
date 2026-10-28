import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface E2eeCapabilities {
  enabled: boolean;
  protocol: string;
  envelopeVersion: number;
  reduced: string[];
  kept: string[];
}

export interface E2eeChannelDevice {
  deviceId: string;
  userId: string;
  publicJwk: JsonWebKey | string;
  fingerprint: string;
  verified: boolean;
}

export interface E2eeChannelState {
  generation: number;
  protocol: string;
  envelopeVersion: number;
  reduced: string[];
  devices: E2eeChannelDevice[];
}

@Injectable({ providedIn: 'root' })
export class E2eeApiService extends HttpApiClient {
  e2eeCapabilities(): Promise<E2eeCapabilities> {
    return this.request('/api/v1/e2ee/capabilities');
  }

  registerE2eeDevice(deviceId: string, publicJwk: JsonWebKey): Promise<{ deviceId: string; fingerprint: string }> {
    return this.request('/api/v1/e2ee/devices', {
      method: 'POST',
      body: JSON.stringify({ deviceId, publicJwk: JSON.stringify(publicJwk) }),
    });
  }

  e2eeChannel(channelId: string): Promise<E2eeChannelState> {
    return this.request(`/api/v1/channels/${channelId}/e2ee`);
  }

  verifyE2eeDevice(deviceId: string, fingerprint: string): Promise<{ verified: boolean }> {
    return this.request(`/api/v1/e2ee/devices/${deviceId}/verify`, {
      method: 'POST',
      body: JSON.stringify({ fingerprint }),
    });
  }

  saveE2eeBackup(blob: string): Promise<{ warning: string }> {
    return this.request('/api/v1/e2ee/backup', {
      method: 'PUT',
      body: JSON.stringify({ blob }),
    });
  }
}
