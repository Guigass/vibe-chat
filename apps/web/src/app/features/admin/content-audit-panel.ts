import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { translateErrorCode, ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';

type AuditMode = 'compliance' | 'confidential';

@Component({
  selector: 'vc-content-audit-panel',
  standalone: true,
  templateUrl: './content-audit-panel.html',
  styleUrls: ['./admin-shared.scss', './content-audit-panel.scss'],
})
export class ContentAuditPanel implements OnInit {
  readonly ui = ui;

  private readonly api = inject(ApiService);
  private readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly busy = signal(false);
  readonly feedback = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);
  readonly savedEnabled = signal(true);
  readonly mode = signal<AuditMode>('compliance');
  readonly confirm = signal(false);

  readonly needsConfirm = computed(
    () => this.savedEnabled() && this.mode() === 'confidential',
  );

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    await this.load();
  }

  setMode(mode: AuditMode): void {
    this.mode.set(mode);
    if (mode === 'compliance') {
      this.confirm.set(false);
    }
  }

  onConfirm(event: Event): void {
    this.confirm.set((event.target as HTMLInputElement).checked);
  }

  async save(event: Event): Promise<void> {
    event.preventDefault();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    if (this.needsConfirm() && !this.confirm()) {
      this.errorMessage.set(translateErrorCode('ContentAuditConfirmationRequired'));
      this.feedback.set(null);
      return;
    }

    this.busy.set(true);
    this.feedback.set(null);
    this.errorMessage.set(null);
    try {
      const updated = await this.api.updateAdminSensitiveSettings({
        workspaceId,
        contentAudit: {
          enabled: this.mode() === 'compliance',
          confirmDisable: this.confirm(),
        },
      });
      this.apply(updated.contentAudit?.enabled !== false);
      this.feedback.set(ui.adminContentAuditSaved);
    } catch (error) {
      this.errorMessage.set(this.readError(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    try {
      const settings = await this.api.getAdminSensitiveSettings(workspaceId);
      this.apply(settings.contentAudit?.enabled !== false);
      this.loadError.set(false);
    } catch {
      this.loadError.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  private apply(enabled: boolean): void {
    this.savedEnabled.set(enabled);
    this.mode.set(enabled ? 'compliance' : 'confidential');
    this.confirm.set(false);
  }

  private readError(error: unknown): string {
    const message = error instanceof Error ? error.message : '';
    try {
      const parsed = JSON.parse(message) as { error?: string };
      const translated = translateErrorCode(parsed.error);
      if (parsed.error && translated !== parsed.error) {
        return translated;
      }
    } catch {
      // Response body is not the API error envelope.
    }

    const status = (error as { status?: number } | null)?.status;
    if (status === 403) {
      return ui.adminSettingsForbidden;
    }

    return ui.adminSettingsSaveError;
  }
}
