import { Component, inject, OnInit, signal } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { SensitiveSettings } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

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
    const data = new FormData(form);
    const workspaceEnabled = data.get('aiWorkspaceEnabled') === 'on';
    const provider = String(data.get('aiProvider') ?? current.ai.provider).trim();
    const emailEnabled = data.get('emailEnabled') === 'on';
    const smtpHost = String(data.get('smtpHost') ?? '').trim();
    const smtpPort = Number(data.get('smtpPort') ?? current.email.smtpPort);
    const smtpUsername = String(data.get('smtpUsername') ?? '').trim();
    const smtpFrom = String(data.get('smtpFrom') ?? '').trim();
    const useStartTls = data.get('useStartTls') === 'on';
    const webhookEnabled = data.get('webhookEnabled') === 'on';
    const webhookUrl = String(data.get('webhookUrl') ?? '').trim();
    const webhookSecret = String(data.get('webhookSecret') ?? '').trim();
    const retentionEnabled = data.get('retentionEnabled') === 'on';
    const retentionDays = Number(data.get('retentionDays') ?? current.retention.retentionDays);

    this.settingsBusy.set(true);
    this.settingsFeedback.set(null);
    this.settingsErrorMessage.set(null);
    try {
      const updated = await this.api.updateAdminSensitiveSettings({
        workspaceId,
        ai: { workspaceEnabled, provider },
        email: {
          enabled: emailEnabled,
          smtpHost,
          smtpPort: Number.isFinite(smtpPort) ? smtpPort : current.email.smtpPort,
          smtpUsername,
          smtpFrom,
          useStartTls,
        },
        webhooks: {
          enabled: webhookEnabled,
          url: webhookUrl,
          ...(webhookSecret ? { secret: webhookSecret } : {}),
        },
        retention: {
          enabled: retentionEnabled,
          retentionDays: Number.isFinite(retentionDays)
            ? retentionDays
            : current.retention.retentionDays,
        },
      });
      this.settings.set(updated);
      this.settingsFeedback.set(
        'Configurações atualizadas (AI/SMTP secrets só via env; webhook secret só mascarado; retenção exige kill switch no worker).',
      );
      const secretInput = form.elements.namedItem('webhookSecret') as HTMLInputElement | null;
      if (secretInput) {
        secretInput.value = '';
      }
    } catch (err) {
      const status = (err as { status?: number } | null)?.status;
      this.settingsErrorMessage.set(
        status === 403
          ? 'Sem permissão para alterar settings sensíveis.'
          : 'Não foi possível salvar as configurações.',
      );
    } finally {
      this.settingsBusy.set(false);
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
