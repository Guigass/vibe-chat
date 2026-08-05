import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { LoginPage } from './login.page';

function parseRgb(color: string): { r: number; g: number; b: number } | null {
  const m = color.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
  if (!m) {
    return null;
  }
  return { r: Number(m[1]), g: Number(m[2]), b: Number(m[3]) };
}

describe('LoginPage DevAuth contrast (UX-001)', () => {
  beforeEach(async () => {
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

  it('keeps DevAuth ghost labels light on the dark hero under light theme tokens', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    const host = fixture.nativeElement as HTMLElement;
    // Simulate global light-theme tokens that previously made ghost ink invisible.
    host.style.setProperty('--vc-ink', '#1c1917');
    host.style.setProperty('--vc-ink-muted', '#57534e');
    host.style.setProperty('--vc-border', '#e7e5e4');
    fixture.detectChanges();
    await fixture.whenStable();

    const alice = host.querySelector(
      '.login-hero__dev-actions .vc-btn--ghost',
    ) as HTMLButtonElement | null;
    expect(alice).toBeTruthy();
    expect(alice!.textContent?.trim()).toBe('Alice');

    const color = getComputedStyle(alice!).color;
    const rgb = parseRgb(color);
    expect(rgb).toBeTruthy();
    // Light ink on dark hero — channel average well above mid-gray.
    const avg = (rgb!.r + rgb!.g + rgb!.b) / 3;
    expect(avg).toBeGreaterThan(160);
  });
});
