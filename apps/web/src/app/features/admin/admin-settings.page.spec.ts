import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api/api.service';
import { SensitiveSettings } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminSettingsPage } from './admin-settings.page';

const baseSettings: SensitiveSettings = {
  workspaceId: 'ws-1',
  ai: {
    processEnabled: true,
    processSource: 'env',
    workspaceEnabled: true,
    provider: 'Mock',
    apiKeyConfigured: true,
    apiKeyMask: '••••ey99',
    apiKeySource: 'env',
    secretsWritable: true,
  },
  email: {
    enabled: false,
    source: 'env',
    smtpHost: 'smtp.example.test',
    smtpPort: 587,
    smtpUsername: 'mailer',
    smtpUsernameConfigured: true,
    smtpPasswordConfigured: true,
    smtpPasswordMask: '••••rd42',
    smtpFrom: 'noreply@example.test',
    useStartTls: true,
    secretsWritable: true,
  },
  webhooks: {
    status: 'unconfigured',
    enabled: false,
    url: '',
    urlConfigured: false,
    secretConfigured: false,
    secretMask: null,
    secretsWritable: true,
    message: 'Configure URL and secret',
  },
  retention: {
    processEnabled: true,
    processSource: 'env',
    enabled: false,
    retentionDays: 90,
    defaultRetentionDays: 90,
    message: 'off',
  },
  files: {
    source: 'env',
    maxSizeBytes: 10_485_760,
    maxAttachmentsPerMessage: 5,
    presignUploadTtlSeconds: 600,
    presignDownloadTtlSeconds: 300,
    allowedContentTypes: ['image/png'],
    audioMaxSizeBytes: 5_000_000,
    audioMaxDurationMs: 120_000,
    ceilingMaxSizeBytes: 10_485_760,
    ceilingMaxAttachmentsPerMessage: 5,
  },
  rateLimit: {
    source: 'env',
    sendPerMinute: 60,
    hubPerMinute: 120,
    ceilingSendPerMinute: 60,
    ceilingHubPerMinute: 120,
  },
  encryption: {
    databaseOverridesEnabled: true,
    encryptionAvailable: true,
    activeKeyVersion: 1,
    credentialsUsingActiveKey: 0,
  },
};

describe('AdminSettingsPage', () => {
  let fixture: ComponentFixture<AdminSettingsPage>;
  let api: {
    getAdminSensitiveSettings: ReturnType<typeof vi.fn>;
    updateAdminSensitiveSettings: ReturnType<typeof vi.fn>;
    rotateAdminOpenRouterCredential: ReturnType<typeof vi.fn>;
    rotateAdminSmtpCredential: ReturnType<typeof vi.fn>;
    rotateAdminWebhookCredential: ReturnType<typeof vi.fn>;
    reencryptAdminSettings: ReturnType<typeof vi.fn>;
    downloadWorkspaceExport: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    api = {
      getAdminSensitiveSettings: vi.fn().mockResolvedValue(structuredClone(baseSettings)),
      updateAdminSensitiveSettings: vi.fn().mockResolvedValue(structuredClone(baseSettings)),
      rotateAdminOpenRouterCredential: vi.fn().mockResolvedValue({
        configured: true,
        mask: '••••ey88',
        keyVersion: 1,
        rotatedAt: new Date().toISOString(),
      }),
      rotateAdminSmtpCredential: vi.fn(),
      rotateAdminWebhookCredential: vi.fn(),
      reencryptAdminSettings: vi.fn(),
      downloadWorkspaceExport: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [AdminSettingsPage],
      providers: [
        { provide: ApiService, useValue: api },
        {
          provide: AdminContextService,
          useValue: {
            ensureReady: vi.fn().mockResolvedValue(undefined),
            workspace: () => ({ id: 'ws-1', name: 'Acme', slug: 'acme', role: 'Admin' }),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminSettingsPage);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
  });

  it('loads masked settings without revealing secrets', () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('••••ey99');
    expect(host.textContent).toContain('••••rd42');
    expect(host.textContent).not.toContain('sk-test');
    expect(host.textContent).toContain('Salvar configurações');
    expect(host.textContent).toContain('Substituir chave AI');
  });

  it('saves non-secret settings and keeps credential inputs separate', async () => {
    const form = (fixture.nativeElement as HTMLElement).querySelector(
      'form.settings__form',
    ) as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();

    expect(api.updateAdminSensitiveSettings).toHaveBeenCalledWith(
      expect.objectContaining({
        workspaceId: 'ws-1',
        ai: expect.objectContaining({ provider: 'Mock' }),
        webhooks: expect.not.objectContaining({ secret: expect.anything() }),
      }),
    );
  });

  it('rotates openrouter credential and clears the password input', async () => {
    const form = (fixture.nativeElement as HTMLElement).querySelector(
      'form.settings__cred-form',
    ) as HTMLFormElement;
    const input = form.querySelector('input[name="secret"]') as HTMLInputElement;
    input.value = 'sk-rotated-openrouter-key88';
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.rotateAdminOpenRouterCredential).toHaveBeenCalledWith({
      workspaceId: 'ws-1',
      value: 'sk-rotated-openrouter-key88',
    });
    expect(input.value).toBe('');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('••••ey88');
  });

  it('surfaces 503 when encryption is unavailable', async () => {
    api.rotateAdminOpenRouterCredential.mockRejectedValueOnce({ status: 503 });
    const form = (fixture.nativeElement as HTMLElement).querySelector(
      'form.settings__cred-form',
    ) as HTMLFormElement;
    const input = form.querySelector('input[name="secret"]') as HTMLInputElement;
    input.value = 'sk-rotated-openrouter-key88';
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Criptografia indisponível',
    );
  });
});
