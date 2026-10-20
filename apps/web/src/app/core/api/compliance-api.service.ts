import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export interface ComplianceAccess {
  enabled: boolean;
  hold: boolean;
  discovery: boolean;
  siem: boolean;
  canGrant: boolean;
}

export interface ComplianceGrant {
  id: string;
  userId: string;
  capability: 'hold' | 'discovery' | 'dlp' | 'audit.export' | 'policy';
  grantedBy: string;
  createdAt: string;
}

export interface LegalHold {
  id: string;
  scopeKind: string;
  scopeId: string | null;
  reason: string;
  status: string;
  requestedBy: string;
  approvedBy: string | null;
  expiresAt: string | null;
  createdAt: string;
  releasedAt: string | null;
}

export interface CustodyEvent {
  id: string;
  action: string;
  actorUserId: string;
  holdId: string | null;
  occurredAt: string;
  payloadHash: string;
}

export interface HoldList {
  holds: LegalHold[];
  history: CustodyEvent[];
}

export interface DiscoveryCase {
  id: string;
  title: string;
  reason: string;
  status: string;
  createdAt: string;
}

export interface DiscoveryExport {
  id: string;
  caseId: string;
  status: string;
  progress: number;
  checksum: string;
  itemCount: number;
  manifestJson: string;
}

@Injectable({ providedIn: 'root' })
export class ComplianceApiService extends HttpApiClient {
  access(workspaceId: string): Promise<ComplianceAccess> {
    return this.request(`/api/v1/workspaces/${workspaceId}/compliance/access`);
  }

  listGrants(workspaceId: string): Promise<ComplianceGrant[]> {
    return this.request(`/api/v1/workspaces/${workspaceId}/compliance/grants`);
  }

  grant(workspaceId: string, userId: string, capability: 'hold' | 'discovery' | 'dlp' | 'audit.export' | 'policy'): Promise<ComplianceGrant> {
    return this.send(`/api/v1/workspaces/${workspaceId}/compliance/grants`, { userId, capability });
  }

  revokeGrant(workspaceId: string, grantId: string): Promise<{ revoked: boolean }> {
    return this.send(`/api/v1/workspaces/${workspaceId}/compliance/grants/${grantId}`, undefined, 'DELETE');
  }

  listHolds(workspaceId: string): Promise<HoldList> {
    return this.request(`/api/v1/workspaces/${workspaceId}/compliance/holds`);
  }

  requestHold(workspaceId: string, body: HoldWrite): Promise<LegalHold> {
    return this.send(`/api/v1/workspaces/${workspaceId}/compliance/holds`, body);
  }

  approveHold(workspaceId: string, holdId: string): Promise<LegalHold> {
    return this.send(`/api/v1/workspaces/${workspaceId}/compliance/holds/${holdId}/approve`, confirm());
  }

  requestRelease(workspaceId: string, holdId: string, reason: string): Promise<LegalHold> {
    return this.send(`/api/v1/workspaces/${workspaceId}/compliance/holds/${holdId}/release`, { ...confirm(), reason });
  }

  approveRelease(workspaceId: string, holdId: string): Promise<LegalHold> {
    return this.send(`/api/v1/workspaces/${workspaceId}/compliance/holds/${holdId}/release/approve`, confirm());
  }

  listCases(workspaceId: string): Promise<DiscoveryCase[]> {
    return this.request(`/api/v1/workspaces/${workspaceId}/compliance/cases`);
  }

  createCase(workspaceId: string, title: string, reason: string, channelId: string): Promise<DiscoveryCase> {
    return this.send(`/api/v1/workspaces/${workspaceId}/compliance/cases`, {
      title,
      reason,
      channelIds: channelId ? [channelId] : [],
    });
  }

  exportCase(workspaceId: string, caseId: string): Promise<DiscoveryExport> {
    return this.send(`/api/v1/workspaces/${workspaceId}/compliance/cases/${caseId}/exports`, confirm());
  }

  getExport(workspaceId: string, caseId: string, exportId: string): Promise<DiscoveryExport> {
    return this.request(`/api/v1/workspaces/${workspaceId}/compliance/cases/${caseId}/exports/${exportId}`);
  }

  private readonly send = <T>(path: string, body: unknown, method = 'POST'): Promise<T> =>
    this.request<T>(path, {
      method,
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
}

export interface HoldWrite {
  scopeKind: string;
  scopeId?: string | null;
  intervalStart?: string | null;
  intervalEnd?: string | null;
  reason: string;
  expiresAt?: string | null;
  confirmed: boolean;
  acknowledged: boolean;
}

function confirm(): { confirmed: boolean; acknowledged: boolean } {
  return { confirmed: true, acknowledged: true };
}
