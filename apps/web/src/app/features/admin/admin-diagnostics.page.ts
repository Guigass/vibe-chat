import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { DiagnosticsReport, DiagnosticProbe, SupportBundleJob, SupportRepairJob } from '../../core/api/diagnostics-api.service';
import { ApiService } from '../../core/api/api.service';
import { formatLocaleDate } from '../../core/i18n/format';
import { fillTemplate, translateErrorCode, ui } from '../../core/i18n/strings';
import { AdminAreaId, hasWorkspaceAdmin } from './admin-permissions';
import { AdminContextService } from './admin-context.service';

@Component({
  selector: 'vc-admin-diagnostics',
  standalone: true,
  templateUrl: './admin-diagnostics.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminDiagnosticsPage implements OnInit {
  readonly areaId: AdminAreaId = 'diagnostics';
  readonly ui = ui;
  private readonly api = inject(ApiService);
  private readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly report = signal<DiagnosticsReport | null>(null);
  readonly probe = signal<DiagnosticProbe | null>(null);
  readonly bundle = signal<SupportBundleJob | null>(null);
  readonly repair = signal<SupportRepairJob | null>(null);
  readonly confirm = signal(false);
  readonly canRepair = computed(() => hasWorkspaceAdmin(this.ctx.role()));

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    await this.refresh();
    this.loading.set(false);
  }

  verdictLabel(verdict: DiagnosticsReport['verdict']): string {
    if (verdict === 'degraded') {
      return ui.adminDiagnosticsDegraded;
    }
    if (verdict === 'action_required') {
      return ui.adminDiagnosticsAction;
    }
    return ui.adminDiagnosticsReady;
  }

  statusLabel(status: string): string {
    if (status === 'Warn') {
      return ui.adminDiagnosticsWarn;
    }
    if (status === 'Fail') {
      return ui.adminDiagnosticsFail;
    }
    if (status === 'Skipped') {
      return ui.adminDiagnosticsSkipped;
    }
    return ui.adminDiagnosticsPass;
  }

  expires(job: SupportBundleJob): string {
    return fillTemplate(ui.adminDiagnosticsExpires, { time: formatLocaleDate(job.expiresAt) });
  }

  refresh(): Promise<void> {
    return this.run(async (workspaceId) => {
      this.report.set(await this.api.getDiagnostics(workspaceId));
    });
  }

  probeEmail(): Promise<void> {
    return this.runProbe('email');
  }

  probePush(): Promise<void> {
    return this.runProbe('push');
  }

  probeStorage(): Promise<void> {
    return this.runProbe('storage');
  }

  bundleCreate(): Promise<void> {
    return this.run(async (workspaceId) => {
      this.bundle.set(await this.api.createSupportBundle(workspaceId, crypto.randomUUID()));
    });
  }

  dryRun(): Promise<void> {
    return this.run(async (workspaceId) => {
      this.repair.set(await this.api.createRepair(workspaceId, crypto.randomUUID(), true, false));
    });
  }

  apply(): Promise<void> {
    return this.run(async (workspaceId) => {
      const current = this.repair();
      if (current?.status === 'running') {
        this.repair.set(await this.api.applyRepair(workspaceId, current.id));
        return;
      }
      if (!this.confirm()) {
        this.error.set(ui.errorRepairConflict);
        return;
      }
      const created = await this.api.createRepair(workspaceId, crypto.randomUUID(), false, true);
      this.repair.set(await this.api.applyRepair(workspaceId, created.id));
    });
  }

  cancel(): Promise<void> {
    return this.run(async (workspaceId) => {
      const current = this.repair();
      if (!current) {
        return;
      }
      this.repair.set(await this.api.cancelRepair(workspaceId, current.id));
    });
  }

  private runProbe(kind: 'email' | 'push' | 'storage'): Promise<void> {
    return this.run(async (workspaceId) => {
      this.probe.set(await this.api.probeDiagnostic(workspaceId, kind));
    });
  }

  private async run(action: (workspaceId: string) => Promise<void>): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await action(workspaceId);
    } catch (error) {
      this.error.set(this.readError(error));
    } finally {
      this.busy.set(false);
    }
  }

  private readError(error: unknown): string {
    const text = error instanceof Error ? error.message : '';
    try {
      const parsed = JSON.parse(text) as { error?: string };
      return translateErrorCode(parsed.error) || ui.errorSupportRejected;
    } catch {
      return ui.errorSupportRejected;
    }
  }
}
