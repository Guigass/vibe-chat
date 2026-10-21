import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface CapacityProfile {
  id: string;
  users: number;
  concurrency: number;
  connectionsPerNode: number;
  messagesPerSecond: number;
  largestChannel: number;
  fanOut: number;
  storageGb: number;
  retentionDays: number;
  tenants: number;
}

export interface CapacityRecovery {
  id: string;
  availability: number | null;
  rpoMinutes: number;
  rtoMinutes: number;
  bestEffort: boolean;
  current: boolean;
  availableAfter: string;
}

export interface CapacitySli {
  id: string;
  kind: string;
  objective: number | null;
  threshold: number;
  unit: string;
  observed: number | null;
  burnRate: number | null;
  burnState: string;
}

export interface CapacityTrigger {
  id: string;
  adr: string;
  metric: string;
  threshold: number;
  windowMinutes: number;
  comparator: string;
  minWorkerReplicas: number | null;
  minIndexedMessages: number | null;
  requiresRollingRestartGap: boolean;
  met: boolean;
  measured: boolean;
}

export interface CapacityHardware {
  nodeClass: string;
  vcpu: number;
  memoryGb: number;
  disk: string;
  kind: string;
  capturedAt: string;
  errorMargin: string;
}

export interface CapacityDashboard {
  schemaVersion: number;
  isSla: boolean;
  sampleKind: string;
  resultFormat: string;
  workloads: string[];
  hardware: CapacityHardware;
  profiles: CapacityProfile[];
  recovery: CapacityRecovery[];
  slis: CapacitySli[];
  triggers: CapacityTrigger[];
}

@Injectable({ providedIn: 'root' })
export class CapacityApiService extends HttpApiClient {
  dashboard(workspaceId: string): Promise<CapacityDashboard> {
    return this.request(`/api/v1/workspaces/${workspaceId}/capacity`);
  }
}
