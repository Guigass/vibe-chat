import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { ui } from '../../../core/i18n/strings';
import { LoginPage } from './login.page';

function stubMatchMedia(): void {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
}

describe('LoginPage SSO labels (B-164)', () => {
  const login = vi.fn();

  beforeEach(async () => {
    login.mockReset();
    stubMatchMedia();
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({
          items: [
            { alias: 'hub-oidc', displayName: 'Logar com HUB', domains: ['hub.example'] },
            { alias: 'hub-saml', displayName: '   ', domains: ['saml.example'] },
          ],
        }),
      }),
    );
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            error: () => null,
            ready: () => true,
            init: vi.fn(),
            isAuthenticated: () => false,
            isOfflineDemo: () => false,
            login,
            enterDevUser: vi.fn(),
            enterOfflineDemo: vi.fn(),
          },
        },
      ],
    }).compileComponents();
  });

  it('renders the configured display name and a generic fallback', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.componentInstance.enableDevAuth = false;
    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      const count = (fixture.nativeElement as HTMLElement).querySelectorAll(
        '.login-hero__sso-actions .vc-btn',
      ).length;
      expect(count).toBe(2);
    });

    const host = fixture.nativeElement as HTMLElement;
    const labels = [...host.querySelectorAll('.login-hero__sso-actions .vc-btn')].map((el) =>
      el.textContent?.trim(),
    );
    expect(labels).toEqual(['Logar com HUB', ui.loginSsoFallback]);
  });

  it('starts Keycloak with the provider alias', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Logar com HUB');
    });

    const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((el) =>
      el.textContent?.includes('Logar com HUB'),
    );
    button?.click();
    expect(login).toHaveBeenCalledWith('hub-oidc');
  });
});
