import { Component, inject, OnInit, signal } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { SensitiveSettings } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

type CredentialKind = 'openrouter' | 'smtp' | 'webhook' | 'vapid';

@Component({
  selector: 'vc-admin-settings',
  standalone: true,
  templateUrl: './admin-settings.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminSettingsPage implements OnInit {
  readonly areaId: AdminAreaId = 'settings';

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly settings = signal<SensitiveSettings | null>(null);
  readonly settingsBusy = signal(false);
  readonly settingsFeedback = signal<string | null>(null);
  readonly settingsErrorMessage = signal<string | null>(null);
  readonly credentialBusy = signal<CredentialKind | null>(null);
  readonly credentialFeedback = signal<string | null>(null);
  readonly credentialError = signal<string | null>(null);
  readonly reencryptBusy = signal(false);
  readonly exportBusy = signal(false);
  readonly exportFeedback = signal<string | null>(null);
  readonly exportError = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    await this.loadSettings();
    this.loading.set(false);
  }

  async exportWorkspace(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id ?? this.settings()?.workspaceId;
    if (!workspaceId || this.exportBusy()) {
      return;
    }

    this.exportBusy.set(true);
    this.exportFeedback.set(null);
    this.exportError.set(null);
    try {
      await this.api.downloadWorkspaceExport(workspaceId);
      this.exportFeedback.set('Export baixado.');
    } catch (error) {
      const status = (error as { status?: number }).status;
      this.exportError.set(
        status === 403 ? 'Falha ao exportar — sem permissão.' : 'Falha ao gerar o export.',
      );
    } finally {
      this.exportBusy.set(false);
    }
  }

  async onSettingsSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const current = this.settings();
    const workspaceId = this.ctx.workspace()?.id ?? current?.workspaceId;
    if (!workspaceId || !current) {
      return;
    }

    const form = event.target as HTMLFormElement;
    // Include controls associated via form="admin-settings-form" (outside the anchor element).
    const data = new FormData(form);
    for (const el of document.querySelectorAll<HTMLInputElement | HTMLSelectElement>(
      '[form="admin-settings-form"]',
    )) {
      if (!el.name || el.disabled) {
        continue;
      }
      if (el instanceof HTMLInputElement && el.type === 'checkbox') {
        if (el.checked) {
          data.set(el.name, 'on');
        } else {
          data.delete(el.name);
        }
        continue;
      }
      data.set(el.name, el.value);
    }
    const workspaceEnabled = data.get('aiWorkspaceEnabled') === 'on';
    const provider = String(data.get('aiProvider') ?? current.ai.provider).trim();
    const aiProcessEnabled = data.get('aiProcessEnabled') === 'on';
    const openRouterBaseUrl = String(data.get('openRouterBaseUrl') ?? current.ai.openRouterBaseUrl ?? '').trim();
    const emailEnabled = data.get('emailEnabled') === 'on';
    const emailProcessEnabled = data.get('emailProcessEnabled') === 'on';
    const smtpHost = String(data.get('smtpHost') ?? '').trim();
    const smtpPort = Number(data.get('smtpPort') ?? current.email.smtpPort);
    const smtpUsername = String(data.get('smtpUsername') ?? '').trim();
    const smtpFrom = String(data.get('smtpFrom') ?? '').trim();
    const useStartTls = data.get('useStartTls') === 'on';
    const webhookEnabled = data.get('webhookEnabled') === 'on';
    const webhookUrl = String(data.get('webhookUrl') ?? '').trim();
    const retentionEnabled = data.get('retentionEnabled') === 'on';
    const retentionProcessEnabled = data.get('retentionProcessEnabled') === 'on';
    const retentionDays = Number(data.get('retentionDays') ?? current.retention.retentionDays);
    const retentionDefaultDays = Number(
      data.get('retentionDefaultDays') ?? current.retention.defaultRetentionDays,
    );
    const retentionBatchSize = Number(data.get('retentionBatchSize') ?? current.retention.batchSize ?? 500);
    const retentionIntervalMinutes = Number(
      data.get('retentionIntervalMinutes') ?? current.retention.intervalMinutes ?? 60,
    );
    const linkPreviewEnabled = data.get('linkPreviewEnabled') === 'on';
    const linkPreviewProcessEnabled = data.get('linkPreviewProcessEnabled') === 'on';
    const linkPreviewTimeoutMs = Number(data.get('linkPreviewTimeoutMs') ?? current.linkPreview.timeoutMs);
    const pushProcessEnabled = data.get('pushProcessEnabled') === 'on';
    const maxSizeBytes = Number(data.get('maxSizeBytes') ?? current.files.maxSizeBytes);
    const maxAttachmentsPerMessage = Number(
      data.get('maxAttachmentsPerMessage') ?? current.files.maxAttachmentsPerMessage,
    );
    const sendPerMinute = Number(data.get('sendPerMinute') ?? current.rateLimit.sendPerMinute);
    const hubPerMinute = Number(data.get('hubPerMinute') ?? current.rateLimit.hubPerMinute);

    this.settingsBusy.set(true);
    this.settingsFeedback.set(null);
    this.settingsErrorMessage.set(null);
    try {
      const updated = await this.api.updateAdminSensitiveSettings({
        workspaceId,
        ai: { workspaceEnabled, provider, processEnabled: aiProcessEnabled, openRouterBaseUrl },
        email: {
          enabled: emailEnabled,
          processEnabled: emailProcessEnabled,
          smtpHost,
          smtpPort: Number.isFinite(smtpPort) ? smtpPort : current.email.smtpPort,
          smtpUsername,
          smtpFrom,
          useStartTls,
        },
        webhooks: {
          enabled: webhookEnabled,
          url: webhookUrl,
        },
        retention: {
          enabled: retentionEnabled,
          processEnabled: retentionProcessEnabled,
          retentionDays: Number.isFinite(retentionDays)
            ? retentionDays
            : current.retention.retentionDays,
          defaultRetentionDays: Number.isFinite(retentionDefaultDays)
            ? retentionDefaultDays
            : current.retention.defaultRetentionDays,
          batchSize: Number.isFinite(retentionBatchSize) ? retentionBatchSize : 500,
          intervalMinutes: Number.isFinite(retentionIntervalMinutes) ? retentionIntervalMinutes : 60,
        },
        linkPreview: {
          enabled: linkPreviewEnabled,
          processEnabled: linkPreviewProcessEnabled,
          timeoutMs: Number.isFinite(linkPreviewTimeoutMs)
            ? linkPreviewTimeoutMs
            : current.linkPreview.timeoutMs,
        },
        push: { processEnabled: pushProcessEnabled },
        files: {
          maxSizeBytes: Number.isFinite(maxSizeBytes) ? maxSizeBytes : current.files.maxSizeBytes,
          maxAttachmentsPerMessage: Number.isFinite(maxAttachmentsPerMessage)
            ? maxAttachmentsPerMessage
            : current.files.maxAttachmentsPerMessage,
        },
        rateLimit: {
          sendPerMinute: Number.isFinite(sendPerMinute)
            ? sendPerMinute
            : current.rateLimit.sendPerMinute,
          hubPerMinute: Number.isFinite(hubPerMinute)
            ? hubPerMinute
            : current.rateLimit.hubPerMinute,
        },
      });
      this.settings.set(updated);
      this.settingsFeedback.set(
        'Configurações atualizadas. Credenciais usam “Substituir” — nunca voltam em claro.',
      );
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.settingsErrorMessage.set(
        status === 403
          ? 'Sem permissão para alterar settings sensíveis.'
          : status === 503
            ? 'Overrides de runtime indisponíveis (flag/keyring).'
            : 'Não foi possível salvar as configurações.',
      );
    } finally {
      this.settingsBusy.set(false);
    }
  }

  async rotateCredential(kind: CredentialKind, event: Event): Promise<void> {
    event.preventDefault();
    const current = this.settings();
    const workspaceId = this.ctx.workspace()?.id ?? current?.workspaceId;
    if (!workspaceId || this.credentialBusy()) {
      return;
    }

    const form = event.target as HTMLFormElement;
    const data = new FormData(form);
    const value = String(data.get('secret') ?? '').trim();
    if (!value) {
      this.credentialError.set('Informe a nova credencial.');
      return;
    }

    this.credentialBusy.set(kind);
    this.credentialFeedback.set(null);
    this.credentialError.set(null);
    try {
      const result =
        kind === 'openrouter'
          ? await this.api.rotateAdminOpenRouterCredential({ workspaceId, value })
          : kind === 'smtp'
            ? await this.api.rotateAdminSmtpCredential({ workspaceId, value })
            : await this.api.rotateAdminWebhookCredential({ workspaceId, value });

      const secretInput = form.elements.namedItem('secret') as HTMLInputElement | null;
      if (secretInput) {
        secretInput.value = '';
      }

      await this.loadSettings();
      this.credentialFeedback.set(
        `Credencial ${kind} substituída${result.mask ? ` (${result.mask})` : ''}.`,
      );
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.credentialError.set(
        status === 403
          ? 'Sem permissão para rotacionar credenciais.'
          : status === 503
            ? 'Criptografia indisponível (RuntimeSettings desligado ou keyring ausente).'
            : 'Falha ao substituir a credencial.',
      );
    } finally {
      this.credentialBusy.set(null);
    }
  }

  async rotateVapid(event: Event): Promise<void> {
    event.preventDefault();
    const current = this.settings();
    const workspaceId = this.ctx.workspace()?.id ?? current?.workspaceId;
    if (!workspaceId || this.credentialBusy()) {
      return;
    }

    const form = event.target as HTMLFormElement;
    const data = new FormData(form);
    const publicKey = String(data.get('publicKey') ?? '').trim();
    const privateKey = String(data.get('privateKey') ?? '').trim();
    const subject = String(data.get('subject') ?? '').trim();
    if (!publicKey || !privateKey) {
      this.credentialError.set('Informe as chaves VAPID pública e privada.');
      return;
    }

    this.credentialBusy.set('vapid');
    this.credentialFeedback.set(null);
    this.credentialError.set(null);
    try {
      const result = await this.api.rotateAdminVapidCredential({
        workspaceId,
        publicKey,
        privateKey,
        subject: subject || undefined,
      });
      const publicInput = form.elements.namedItem('publicKey') as HTMLInputElement | null;
      const privateInput = form.elements.namedItem('privateKey') as HTMLInputElement | null;
      if (publicInput) {
        publicInput.value = '';
      }
      if (privateInput) {
        privateInput.value = '';
      }
      await this.loadSettings();
      this.credentialFeedback.set(
        `VAPID substituído${result.mask ? ` (${result.mask})` : ''}.`,
      );
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.credentialError.set(
        status === 403
          ? 'Sem permissão para rotacionar credenciais.'
          : status === 503
            ? 'Criptografia indisponível (RuntimeSettings desligado ou keyring ausente).'
            : 'Falha ao substituir a credencial.',
      );
    } finally {
      this.credentialBusy.set(null);
    }
  }

  async reencryptCredentials(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id ?? this.settings()?.workspaceId;
    if (!workspaceId || this.reencryptBusy()) {
      return;
    }

    this.reencryptBusy.set(true);
    this.credentialFeedback.set(null);
    this.credentialError.set(null);
    try {
      const result = await this.api.reencryptAdminSettings(workspaceId);
      this.settings.set(result.settings);
      this.credentialFeedback.set(
        result.reencrypted > 0
          ? `${result.reencrypted} credencial(is) re-encriptada(s) na versão ativa.`
          : 'Nenhuma credencial precisava de re-encriptação.',
      );
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.credentialError.set(
        status === 403
          ? 'Sem permissão para re-encriptar.'
          : status === 503
            ? 'Criptografia indisponível (RuntimeSettings desligado ou keyring ausente).'
            : 'Falha ao re-encriptar credenciais.',
      );
    } finally {
      this.reencryptBusy.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    try {
      const settings = await this.api.getAdminSensitiveSettings(workspaceId);
      this.settings.set(settings);
      this.loadError.set(false);
    } catch {
      this.loadError.set(true);
      this.settings.set(null);
    }
  }
}
