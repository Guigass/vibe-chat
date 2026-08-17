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
    processSource: 'database',
    workspaceEnabled: true,
    provider: 'Mock',
    openRouterBaseUrl: 'https://openrouter.ai/api/v1',
    apiKeyConfigured: true,
    apiKeyMask: '••••ey99',
    apiKeySource: 'env',
    secretsWritable: true,
  },
  email: {
    processEnabled: false,
    processSource: 'database',
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
    processSource: 'database',
    enabled: false,
    retentionDays: 90,
    defaultRetentionDays: 90,
    batchSize: 500,
    intervalMinutes: 60,
    message: 'off',
  },
  linkPreview: {
    processEnabled: true,
    processSource: 'database',
    enabled: true,
    timeoutMs: 4000,
    message: 'Worker busca Open Graph da primeira URL (guarda SSRF).',
  },
  push: {
    processEnabled: true,
    processSource: 'database',
    vapidPublicKey: 'Bpublic',
    vapidConfigured: true,
    vapidMask: '••••key1',
    vapidSource: 'database',
    vapidSubject: 'mailto:ops@localhost',
    secretsWritable: true,
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
    rotateAdminVapidCredential: ReturnType<typeof vi.fn>;
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
      rotateAdminVapidCredential: vi.fn().mockResolvedValue({
        configured: true,
        mask: '••••key1',
        keyVersion: 1,
        rotatedAt: new Date().toISOString(),
      }),
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
    expect(host.textContent).toContain('Substituir chave');
    expect(host.textContent).toContain('Kill switch da instância (IA)');
    expect(host.textContent).toContain('Web Push / VAPID');
  });

  it('saves non-secret settings and keeps credential inputs separate', async () => {
    const form = (fixture.nativeElement as HTMLElement).querySelector(
      '#admin-settings-form',
    ) as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();

    expect(api.updateAdminSensitiveSettings).toHaveBeenCalledWith(
      expect.objectContaining({
        workspaceId: 'ws-1',
        ai: expect.objectContaining({
          provider: 'Mock',
          processEnabled: true,
          openRouterBaseUrl: 'https://openrouter.ai/api/v1',
        }),
        email: expect.objectContaining({ processEnabled: false }),
        retention: expect.objectContaining({ processEnabled: true }),
        linkPreview: expect.objectContaining({ processEnabled: true }),
        push: expect.objectContaining({ processEnabled: true }),
        webhooks: expect.not.objectContaining({ secret: expect.anything() }),
      }),
    );
  });

  it('rotates openrouter credential and clears the password input', async () => {
    const form = (fixture.nativeElement as HTMLElement).querySelector(
      'form.settings__cred',
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

  it('rotates vapid keys from the push form', async () => {
    const form = (fixture.nativeElement as HTMLElement).querySelector(
      '#admin-vapid-form',
    ) as HTMLFormElement;
    const publicInput = form.querySelector('input[name="publicKey"]') as HTMLInputElement;
    const privateInput = form.querySelector('input[name="privateKey"]') as HTMLInputElement;
    publicInput.value = 'Bpublic-rotated';
    privateInput.value = 'private-rotated-key';
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.rotateAdminVapidCredential).toHaveBeenCalledWith({
      workspaceId: 'ws-1',
      publicKey: 'Bpublic-rotated',
      privateKey: 'private-rotated-key',
      subject: 'mailto:ops@localhost',
    });
    expect(privateInput.value).toBe('');
  });

  it('surfaces 503 when encryption is unavailable', async () => {
    api.rotateAdminOpenRouterCredential.mockRejectedValueOnce({ status: 503 });
    const form = (fixture.nativeElement as HTMLElement).querySelector(
      'form.settings__cred',
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

  it('groups sections in a sensible order', () => {
    const titles = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.settings__section-title'),
    ).map((el) => el.textContent?.trim());
    expect(titles).toEqual([
      'Integrações',
      'Limites operacionais',
      'Retenção e exportação',
      'Criptografia',
    ]);
  });
});
