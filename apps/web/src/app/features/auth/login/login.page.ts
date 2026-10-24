import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService, DevUserName } from '../../../core/auth/auth.service';
import { BrandingService } from '../../../core/services/branding.service';
import { environment } from '../../../../environments/environment';
import { Button, LocaleControl, ThemeToggle } from '../../../shared/ui';
import { ui } from '../../../core/i18n/strings';

interface SsoProviderView {
  alias: string;
  label: string;
  domains: string[];
}

@Component({
  selector: 'vc-login-page',
  standalone: true,
  imports: [Button, ThemeToggle, LocaleControl],
  templateUrl: './login.page.html',
  styleUrl: './login.page.scss',
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly brand = inject(BrandingService);

  readonly error = this.auth.error;
  readonly ui = ui;
  /** Overridable in unit tests; production builds keep this false. */
  enableDevAuth = environment.enableDevAuth;
  loading = false;
  readonly providers = signal<SsoProviderView[]>([]);
  readonly domain = signal('');
  readonly ssoUnavailable = signal(false);
  readonly matchedAlias = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    void this.loadSsoProviders();
    if (!this.auth.ready()) {
      await this.auth.init();
    }
    if (this.auth.isAuthenticated()) {
      await this.router.navigateByUrl(this.consumeReturnUrl());
    }
  }

  onDomainInput(event: Event): void {
    const value = event.target instanceof HTMLInputElement ? event.target.value : '';
    this.onDomain(value);
  }

  onDomain(value: string): void {
    const domain = domainOf(value);
    this.domain.set(domain);
    const hits = this.providers().filter((item) => item.domains.includes(domain));
    this.matchedAlias.set(hits.length === 1 ? hits[0].alias : null);
  }

  async loginWith(alias: string): Promise<void> {
    this.loading = true;
    try {
      await this.auth.login(alias);
    } catch (err) {
      this.loading = false;
      console.error(err);
    }
  }

  private async loadSsoProviders(): Promise<void> {
    try {
      const response = await fetch(`${environment.apiUrl}/api/v1/auth/sso-providers`);
      if (!response.ok) {
        this.ssoUnavailable.set(response.status !== 404);
        return;
      }
      const body = (await response.json()) as {
        items?: { alias?: string; displayName?: string; domains?: string[] }[];
      };
      const items = (body.items ?? [])
        .filter((item) => typeof item.alias === 'string' && item.alias.length > 0)
        .map((item) => ({
          alias: item.alias as string,
          label: item.displayName?.trim() || ui.loginSsoFallback,
          domains: (item.domains ?? []).map((domain) => domain.toLowerCase()),
        }));
      this.providers.set(items);
      this.ssoUnavailable.set(false);
    } catch {
      this.ssoUnavailable.set(true);
    }
  }

  private consumeReturnUrl(): string {
    if (typeof sessionStorage === 'undefined') {
      return '/app';
    }
    const next = sessionStorage.getItem('vc.returnUrl');
    sessionStorage.removeItem('vc.returnUrl');
    return next && next.startsWith('/') ? next : '/app';
  }

  async login(): Promise<void> {
    this.loading = true;
    try {
      await this.auth.login();
    } catch (err) {
      this.loading = false;
      console.error(err);
    }
  }

  async enterDev(name: DevUserName): Promise<void> {
    this.auth.enterDevUser(name);
    await this.router.navigateByUrl(this.consumeReturnUrl());
  }

  async enterOfflineDemo(): Promise<void> {
    this.auth.enterOfflineDemo();
    await this.router.navigateByUrl(this.consumeReturnUrl());
  }
}

function domainOf(value: string): string {
  const trimmed = value.trim().toLowerCase();
  const at = trimmed.lastIndexOf('@');
  return at >= 0 ? trimmed.slice(at + 1) : '';
}
