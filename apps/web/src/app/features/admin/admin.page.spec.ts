import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { AdminPage } from './admin.page';

describe('AdminPage (B-104)', () => {
  let fixture: ComponentFixture<AdminPage>;

  beforeEach(async () => {
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

    await TestBed.configureTestingModule({
      imports: [AdminPage],
      providers: [
        provideRouter([]),
        {
          provide: ApiService,
          useValue: {
            getAdminStats: vi.fn().mockResolvedValue({
              users: 2,
              onlineUsers: 1,
              workspaces: 1,
              channels: 1,
              messages: 10,
              realtimeConnections: 1,
              outboxPending: 0,
              processingFailures: 0,
              health: { postgres: 'up', redis: 'up', storage: 'up' },
              appVersion: 'test',
              grafanaUrl: 'http://localhost',
            }),
            getAdminAuditEvents: vi.fn().mockResolvedValue([]),
            getWorkspaces: vi.fn().mockResolvedValue([
              { id: 'ws-1', name: 'Acme', role: 'Admin' },
            ]),
            getMembers: vi.fn().mockResolvedValue([
              {
                userId: 'u-1',
                displayName: 'Alice',
                email: 'alice@example.com',
                role: 'Admin',
              },
              {
                userId: 'u-2',
                displayName: 'Bob',
                email: 'bob@example.com',
                role: 'Member',
              },
            ]),
            getAssignableRoles: vi
              .fn()
              .mockResolvedValue(['Member', 'Moderator', 'Auditor', 'Admin']),
            getSensitiveSettings: vi.fn().mockRejectedValue({ status: 403 }),
            getAdminConversations: vi.fn().mockResolvedValue([
              { id: 'ch-1', name: 'geral', type: 'Public' },
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

    fixture = TestBed.createComponent(AdminPage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  });

  it('renders members table and spartan select without PrimeNG tags', () => {
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('table.roles__table')).toBeTruthy();
    expect(host.querySelector('hlm-select')).toBeTruthy();
    expect(host.querySelector('p-table')).toBeNull();
    expect(host.querySelector('p-select')).toBeNull();
    expect(host.querySelector('p-tag')).toBeNull();
    expect(host.textContent).toContain('Alice');
    expect(host.textContent).toContain('Bob');
  });
});
