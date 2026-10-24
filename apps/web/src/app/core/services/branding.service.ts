import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { BrandView, BrandingApiService } from '../api/branding-api.service';
import { AuthService } from '../auth/auth.service';
import { ThemeService } from './theme.service';

const PRODUCT = 'VibeChat';

@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly api = inject(BrandingApiService);
  private readonly auth = inject(AuthService);
  private readonly theme = inject(ThemeService);
  private readonly view = signal<BrandView | null>(null);
  private logoObjectUrl: string | null = null;
  private faviconHref: string | null = null;

  readonly label = computed(() => this.view()?.displayName?.trim() || PRODUCT);
  readonly loginLogo = computed(() => this.publicUrl(this.pick(this.view(), true)));
  readonly productName = computed(() => this.view()?.productName || PRODUCT);
  readonly productVersion = computed(() => this.view()?.productVersion || '');

  constructor() {
    effect(() => {
      if (!this.auth.ready()) {
        return;
      }
      const authed = this.auth.isAuthenticated();
      this.theme.theme();
      void this.load(authed);
    });
  }

  async reload(): Promise<void> {
    if (!this.auth.ready()) {
      return;
    }
    await this.load(this.auth.isAuthenticated());
  }

  private async load(authed: boolean): Promise<void> {
    try {
      const view = authed ? await this.api.tenantBrand() : await this.api.publicBrand();
      if (view?.productName !== PRODUCT) {
        return;
      }
      this.view.set(view);
      this.apply(view);
    } catch {
      if (!authed) {
        this.view.set(null);
        this.apply(null);
      }
    }
  }

  private apply(view: BrandView | null): void {
    const root = document.documentElement;
    this.paintColor(root, '--vc-brand', view?.brandColor);
    this.paintColor(root, '--vc-accent', view?.accentColor);
    const name = view?.displayName?.trim() || PRODUCT;
    document.querySelectorAll<HTMLElement>('[data-vc-brand-name]').forEach((node) => {
      node.textContent = name;
    });
    const mark = name.charAt(0).toUpperCase() || 'V';
    document.querySelectorAll<HTMLElement>('[data-vc-brand-mark]').forEach((node) => {
      node.textContent = mark;
    });
    void this.paintShellLogo(view);
    this.paintFavicon(view);
  }

  private async paintShellLogo(view: BrandView | null): Promise<void> {
    const path = this.pick(view, false);
    const images = document.querySelectorAll<HTMLImageElement>('[data-vc-brand-logo]');
    if (!path || !this.auth.isAuthenticated()) {
      this.clearLogo(images);
      return;
    }
    try {
      const blob = await this.api.fetchAsset(path);
      if (!blob) {
        this.clearLogo(images);
        return;
      }
      if (this.logoObjectUrl) {
        URL.revokeObjectURL(this.logoObjectUrl);
      }
      this.logoObjectUrl = URL.createObjectURL(blob);
      images.forEach((img) => {
        img.src = this.logoObjectUrl!;
        img.hidden = false;
      });
    } catch {
      this.clearLogo(images);
    }
  }

  private paintFavicon(view: BrandView | null): void {
    const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
    if (!link) {
      return;
    }
    this.faviconHref ??= link.getAttribute('href');
    const path = view?.faviconUrl;
    if (!path) {
      if (this.faviconHref) {
        link.setAttribute('href', this.faviconHref);
      }
      return;
    }
    link.setAttribute('href', `${environment.apiUrl}${path}`);
  }

  private clearLogo(images: NodeListOf<HTMLImageElement>): void {
    if (this.logoObjectUrl) {
      URL.revokeObjectURL(this.logoObjectUrl);
      this.logoObjectUrl = null;
    }
    images.forEach((img) => {
      img.hidden = true;
      img.removeAttribute('src');
    });
  }

  private pick(view: BrandView | null, preferDark: boolean): string | null {
    if (!view) {
      return null;
    }
    const dark = preferDark || this.theme.theme() === 'dark';
    return dark ? view.logoDarkUrl ?? view.logoLightUrl : view.logoLightUrl ?? view.logoDarkUrl;
  }

  private publicUrl(path: string | null): string | null {
    return path ? `${environment.apiUrl}${path}` : null;
  }

  private paintColor(root: HTMLElement, name: string, value: string | null | undefined): void {
    if (value) {
      root.style.setProperty(name, value);
      return;
    }
    root.style.removeProperty(name);
  }
}
