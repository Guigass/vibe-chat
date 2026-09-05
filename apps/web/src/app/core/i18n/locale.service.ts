import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import {
  type AppLocale,
  isAppLocale,
  persistLocale,
  resolveBootstrapLocale,
} from './locale';

@Injectable({ providedIn: 'root' })
export class LocaleService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  readonly locale = signal<AppLocale>(resolveBootstrapLocale());

  async apply(next: AppLocale): Promise<void> {
    if (next === this.locale()) {
      return;
    }
    persistLocale(next);
    if (this.canPersistRemote()) {
      try {
        await this.api.updateMe({ locale: next });
      } catch {
        // Keep the local choice; reload still applies the catalog.
      }
    }
    window.location.reload();
  }

  async syncFromProfile(): Promise<void> {
    if (!this.canPersistRemote()) {
      return;
    }
    try {
      const me = await this.api.getMe();
      if (isAppLocale(me.locale)) {
        if (me.locale !== this.locale()) {
          persistLocale(me.locale);
          window.location.reload();
        }
        return;
      }
      await this.api.updateMe({ locale: this.locale() });
    } catch {
      // Offline or first paint — keep the bootstrap locale.
    }
  }

  private canPersistRemote(): boolean {
    return this.auth.isAuthenticated() && !this.auth.isOfflineDemo();
  }
}
