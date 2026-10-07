import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api/api.service';
import { ImportJob } from '../../core/api/import-api.service';
import { fillTemplate, translateErrorCode, ui } from '../../core/i18n/strings';
import { AdminAreaId } from './admin-permissions';
import { AdminContextService } from './admin-context.service';

const storageKey = 'vibechat.import.job';

@Component({
  selector: 'vc-admin-import',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './admin-import.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminImportPage implements OnInit {
  readonly areaId: AdminAreaId = 'import';
  readonly ui = ui;
  private readonly api = inject(ApiService);
  private readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly adapter = signal('vibechat');
  readonly fileName = signal('');
  readonly job = signal<ImportJob | null>(null);
  readonly confirm = signal(false);
  private document: unknown = null;
  private idempotencyKey = crypto.randomUUID();

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    const saved = sessionStorage.getItem(storageKey);
    const workspaceId = this.ctx.workspace()?.id;
    if (saved && workspaceId) {
      try {
        this.job.set(await this.api.getImport(workspaceId, saved));
      } catch {
        sessionStorage.removeItem(storageKey);
      }
    }
    this.loading.set(false);
  }

  async onFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    this.error.set('');
    this.document = null;
    this.fileName.set(file?.name ?? '');
    if (!file) {
      return;
    }
    try {
      this.document = JSON.parse(await file.text());
      this.idempotencyKey = crypto.randomUUID();
    } catch {
      this.error.set(ui.errorImportRejected);
    }
  }

  counts(job: ImportJob): string {
    return fillTemplate(ui.adminImportCounts, {
      principals: job.report.principals,
      channels: job.report.channels,
      messages: job.report.messages,
      quarantined: job.report.quarantined,
      ignored: job.report.ignored,
    });
  }

  validate(): Promise<void> {
    return this.run(async (workspaceId) => {
      if (!this.document) {
        this.error.set(ui.adminImportEmpty);
        return;
      }
      const job = await this.api.createImport(workspaceId, this.adapter(), this.document, this.idempotencyKey);
      this.remember(job);
    });
  }

  plan(): Promise<void> {
    return this.act((id, job) => this.api.planImport(id, job));
  }

  execute(): Promise<void> {
    return this.act((id, job) => this.api.executeImport(id, job));
  }

  pause(): Promise<void> {
    return this.act((id, job) => this.api.pauseImport(id, job));
  }

  resume(): Promise<void> {
    return this.act((id, job) => this.api.resumeImport(id, job));
  }

  publish(): Promise<void> {
    return this.act((id, job) => this.api.publishImport(id, job));
  }

  rollback(): Promise<void> {
    return this.act((id, job) => this.api.rollbackImport(id, job, this.confirm()));
  }

  private act(call: (workspaceId: string, importId: string) => Promise<ImportJob>): Promise<void> {
    return this.run(async (workspaceId) => {
      const current = this.job();
      if (!current) {
        return;
      }
      this.remember(await call(workspaceId, current.id));
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

  private remember(job: ImportJob): void {
    this.job.set(job);
    sessionStorage.setItem(storageKey, job.id);
  }

  private readError(error: unknown): string {
    const text = error instanceof Error ? error.message : '';
    try {
      const parsed = JSON.parse(text) as { error?: string };
      const message = translateErrorCode(parsed.error);
      return parsed.error === 'ImportDisabled' ? ui.adminImportDisabled : message || ui.errorImportRejected;
    } catch {
      return ui.errorImportRejected;
    }
  }
}
