import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ScimApiService } from '../../core/api/scim-api.service';
import { ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';
import { AdminScimPage } from './admin-scim.page';

describe('Admin SCIM (B-128)', () => {
  let fixture: ComponentFixture<AdminScimPage>;
  const load = vi.fn();
  const createToken = vi.fn();

  beforeEach(async () => {
    load.mockReset();
    createToken.mockReset();
    load.mockResolvedValue({ keycloakAdapter: false, tokens: [], groups: [], mappings: [] });
    createToken.mockResolvedValue({
      id: 't-1',
      name: 'IdP',
      last4: 'abcd',
      createdAt: '2026-10-18T00:00:00Z',
      lastUsedAt: null,
      secret: 'vc_scim_secret',
    });

    await TestBed.configureTestingModule({
      imports: [AdminScimPage],
      providers: [
        {
          provide: AdminContextService,
          useValue: {
            ensureReady: vi.fn().mockResolvedValue(undefined),
            workspace: () => ({ id: 'ws-1' }),
          },
        },
        {
          provide: ScimApiService,
          useValue: { load, createToken, rotateToken: vi.fn(), revokeToken: vi.fn(), dryRun: vi.fn(), applyMapping: vi.fn() },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminScimPage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  });

  it('shows the secret only after creation and reloads the list without it', async () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain(ui.adminScimEmpty);

    load.mockResolvedValue({
      keycloakAdapter: false,
      tokens: [{ id: 't-1', name: 'IdP', last4: 'abcd', createdAt: '2026-10-18T00:00:00Z', lastUsedAt: null }],
      groups: [],
      mappings: [],
    });
    fixture.componentInstance.tokenName.set('IdP');
    await fixture.componentInstance.createToken();
    fixture.detectChanges();

    expect(host.textContent).toContain('vc_scim_secret');
    expect(host.textContent).toContain('abcd');
    expect(host.textContent).not.toContain('vc_scim_secretvc_scim_secret');
    const table = host.querySelector('table')?.textContent ?? '';
    expect(table).not.toContain('vc_scim_secret');
  });

  it('explains when SCIM is switched off', async () => {
    load.mockRejectedValue(Object.assign(new Error('{"error":"ScimDisabled"}'), { status: 404 }));
    await fixture.componentInstance.reload();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(ui.adminScimDisabled);
  });
});
