import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export type BrandAssetKind = 'logo-light' | 'logo-dark' | 'favicon';

export interface BrandView {
  displayName: string | null;
  brandColor: string | null;
  accentColor: string | null;
  logoLightUrl: string | null;
  logoDarkUrl: string | null;
  faviconUrl: string | null;
  assetVersion: number;
  hasRollback: boolean;
  productName: string;
  productVersion: string;
  fallback: boolean;
}

export interface BrandUpdate {
  displayName: string | null;
  brandColor: string | null;
  accentColor: string | null;
}

@Injectable({ providedIn: 'root' })
export class BrandingApiService extends HttpApiClient {
  publicBrand(): Promise<BrandView> {
    return this.request('/api/v1/public/branding');
  }

  tenantBrand(): Promise<BrandView> {
    return this.request('/api/v1/branding');
  }

  adminBrand(): Promise<BrandView> {
    return this.request('/api/v1/admin/branding');
  }

  saveBrand(body: BrandUpdate, idempotencyKey: string): Promise<BrandView> {
    return this.request('/api/v1/admin/branding', {
      method: 'PUT',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  uploadAsset(kind: BrandAssetKind, file: File, idempotencyKey: string): Promise<BrandView> {
    const body = new FormData();
    body.append('file', file);
    return this.request(`/api/v1/admin/branding/assets/${kind}`, {
      method: 'POST',
      body,
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  clearAsset(kind: BrandAssetKind, idempotencyKey: string): Promise<BrandView> {
    return this.request(`/api/v1/admin/branding/assets/${kind}`, {
      method: 'DELETE',
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  rollback(idempotencyKey: string): Promise<BrandView> {
    return this.request('/api/v1/admin/branding/rollback', {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  fetchAsset(path: string): Promise<Blob | null> {
    return this.requestBlob(path);
  }
}
