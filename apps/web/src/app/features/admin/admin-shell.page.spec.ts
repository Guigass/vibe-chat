import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { AdminShellPage } from './admin-shell.page';

describe('Admin shell (B-106)', () => {
  let shellFixture: ComponentFixture<AdminShellPage>;

  beforeEach(async () => {
    if (typeof window !== 'undefined') {
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

    await TestBed.configureTestingModule({
      imports: [AdminShellPage],
      providers: [
        provideRouter([]),
        {
          provide: ApiService,
          useValue: {
            getWorkspaces: vi.fn().mockResolvedValue([
              { id: 'ws-1', name: 'Acme', slug: 'acme', role: 'Admin' },
            ]),
          },
        },
        {
          provide: AuthService,
          useValue: {
            profile: () => ({ id: 'u-1', displayName: 'Alice' }),
          },
        },
      ],
    }).compileComponents();

    shellFixture = TestBed.createComponent(AdminShellPage);
    await shellFixture.componentInstance.ngOnInit();
    shellFixture.detectChanges();
  });

  it('renders lateral nav for Admin without permission warnings', () => {
    const host = shellFixture.nativeElement as HTMLElement;
    expect(host.querySelector('.admin-shell__nav-link')).toBeTruthy();
    expect(host.textContent).toContain('Settings');
    expect(host.textContent).not.toContain('Sem permissão');
    expect(host.textContent).not.toContain('Sem acesso ao admin');
  });

  it('shows clear denial feedback for Member instead of a silent redirect', async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [AdminShellPage],
      providers: [
        provideRouter([]),
        {
          provide: ApiService,
          useValue: {
            getWorkspaces: vi.fn().mockResolvedValue([
              { id: 'ws-1', name: 'Acme', slug: 'acme', role: 'Member' },
            ]),
          },
        },
        {
          provide: AuthService,
          useValue: {
            profile: () => ({ id: 'u-alice', displayName: 'Alice' }),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(AdminShellPage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Sem acesso ao admin');
    expect(host.textContent).toContain('DevAuth Demo');
    expect(host.querySelector('.admin-shell__nav-link')).toBeFalsy();
    expect(fixture.componentInstance.activeTitle()).toBe('Sem acesso');
  });
});
