import { inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

export abstract class HttpApiClient {
  protected readonly auth = inject(AuthService);
  protected readonly baseUrl = environment.apiUrl;

  protected async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const headers = new Headers(init.headers ?? {});
    headers.set('Accept', 'application/json');
    if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json');
    }

    await this.applyAuth(headers);

    const response = await fetch(`${this.baseUrl}${path}`, {
      ...init,
      headers,
    });

    if (!response.ok) {
      const text = await response.text().catch(() => '');
      const error = new Error(text || `HTTP ${response.status}`) as Error & { status: number };
      error.status = response.status;
      throw error;
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return (await response.json()) as T;
  }

  protected async requestBlob(path: string): Promise<Blob | null> {
    const headers = new Headers({ Accept: 'image/*' });
    await this.applyAuth(headers);
    const response = await fetch(`${this.baseUrl}${path}`, { headers });
    if (response.status === 404) return null;
    if (!response.ok) {
      const text = await response.text().catch(() => '');
      const error = new Error(text || `HTTP ${response.status}`) as Error & { status: number };
      error.status = response.status;
      throw error;
    }
    return response.blob();
  }

  private async applyAuth(headers: Headers): Promise<void> {
    const devUser = this.auth.devUser();
    if (devUser) {
      headers.set('X-Dev-User', devUser);
      return;
    }
    const token = await this.auth.getAccessToken();
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }
  }
}
