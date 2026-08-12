import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
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

describe('LoginPage DevAuth contrast (UX-001)', () => {
  beforeEach(async () => {
    stubMatchMedia();
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
            login: vi.fn(),
            enterDevUser: vi.fn(),
            enterOfflineDemo: vi.fn(),
          },
        },
      ],
    }).compileComponents();
  });

  it('exposes light ink tokens on the dark hero and labeled DevAuth actions', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.componentInstance.enableDevAuth = true;
    const host = fixture.nativeElement as HTMLElement;
    // Global light-theme tokens that previously made ghost ink invisible on the hero.
    document.documentElement.style.setProperty('--vc-ink', '#1c1917');
    document.documentElement.style.setProperty('--vc-ink-muted', '#57534e');
    document.documentElement.style.setProperty('--vc-border', '#e7e5e4');
    fixture.detectChanges();
    await fixture.whenStable();

    const hero = host.querySelector('.login-hero') as HTMLElement | null;
    expect(hero).toBeTruthy();
    expect(getComputedStyle(hero!).getPropertyValue('--vc-ink').trim().toLowerCase()).toBe(
      '#e8eef4',
    );
    expect(getComputedStyle(hero!).getPropertyValue('--vc-ink-muted').trim().toLowerCase()).toBe(
      '#8b9cb3',
    );
    expect(getComputedStyle(hero!).getPropertyValue('--vc-border').trim().toLowerCase()).toBe(
      'rgba(232, 238, 244, 0.35)',
    );

    const labels = [...host.querySelectorAll('.login-hero__dev-actions .vc-btn--ghost')].map(
      (el) => el.textContent?.trim(),
    );
    expect(labels).toEqual(['Alice', 'Bob', 'Demo']);
  });

  it('hides DevAuth actions when enableDevAuth is false', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.componentInstance.enableDevAuth = false;
    fixture.detectChanges();
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.login-hero__dev')).toBeNull();
    expect(host.textContent).toContain('Entrar com Keycloak');
  });
});
