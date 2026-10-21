import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api/api.service';
import { DeveloperApiService, DeveloperCatalog, DeveloperCredentialSecret } from '../../core/api/developer-api.service';
import { AdminContextService } from './admin-context.service';
import { AdminDeveloperPage } from './admin-developer.page';

const catalog: DeveloperCatalog = {
  schemaVersion: 1,
  sandbox: true,
  liveSendEnabled: true,
  secretInUrl: false,
  capabilities: ['messages.send'],
  rateLimit: { sendPerMinute: 60, window: '1m', scope: 'credential' },
  changelog: [{ id: 'integration-http-v1', status: 'current', retiresOn: null }],
  events: [{ eventType: 'messaging.message.created', schemaVersion: 1, status: 'deprecated' }],
  examples: [{ operation: 'messages.send', synthetic: true }],
};

const created: DeveloperCredentialSecret = {
  id: 'cred-1',
  name: 'CI',
  enabled: true,
  allowDms: false,
  channelIds: [],
  tokenConfigured: true,
  last4: 'ab12',
  lastUsedAt: null,
  revoked: false,
  createdAt: '2026-10-21T00:00:00Z',
  token: 'vc_int_secret',
  idempotent: false,
};

describe('AdminDeveloperPage (B-135)', () => {
  let fixture: ComponentFixture<AdminDeveloperPage>;

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  async function setup(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [AdminDeveloperPage],
      providers: [
        provideRouter([]),
        {
          provide: DeveloperApiService,
          useValue: {
            catalog: vi.fn().mockResolvedValue(catalog),
            credentials: vi.fn().mockResolvedValue([]),
            usage: vi.fn().mockResolvedValue({ sendPerMinute: 60, window: '1m', scope: 'credential', errors: 0, calls: [] }),
            createCredential: vi.fn().mockResolvedValue(created),
          },
        },
        {
          provide: ApiService,
          useValue: { getChannels: vi.fn().mockResolvedValue([]) },
        },
        {
          provide: AdminContextService,
          useValue: {
            ensureReady: vi.fn().mockResolvedValue(undefined),
            workspace: () => ({ id: 'ws-1', name: 'Acme', slug: 'acme', role: 'Admin' }),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminDeveloperPage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  }

  it('keeps the one-time secret out of web storage and the url', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    await setup();
    await fixture.componentInstance.create();
    fixture.detectChanges();
    expect(fixture.componentInstance.secret()).toBe('vc_int_secret');
    expect(window.location.href).not.toContain('vc_int_');
    expect(setItem).not.toHaveBeenCalled();

    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });
    await fixture.componentInstance.copySecret();
    expect(writeText).toHaveBeenCalledWith('vc_int_secret');
    expect(fixture.componentInstance.secret()).toBeNull();
    expect(setItem).not.toHaveBeenCalled();
    setItem.mockRestore();
  });
});
