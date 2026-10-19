import { Component, inject, OnInit, signal } from '@angular/core';
import { QuarantineApiService, QuarantineItem } from '../../core/api/quarantine-api.service';
import { ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-quarantine',
  standalone: true,
  templateUrl: './admin-quarantine.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminQuarantinePage implements OnInit {
  readonly areaId: AdminAreaId = 'quarantine';
  readonly ui = ui;

  private readonly api = inject(QuarantineApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly loadError = signal(false);
  readonly busy = signal(false);
  readonly items = signal<QuarantineItem[]>([]);
  readonly justification = signal('');

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  async reload(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.loadError.set(true);
      return;
    }

    try {
      this.items.set(await this.api.list(workspaceId));
      this.disabled.set(false);
      this.loadError.set(false);
    } catch (error) {
      const status = (error as { status?: number }).status;
      const message = error instanceof Error ? error.message : '';
      if (status === 404 && message.includes('MalwareScanDisabled')) {
        this.disabled.set(true);
      } else {
        this.loadError.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }

  async release(item: QuarantineItem): Promise<void> {
    await this.decide(item, 'release');
  }

  async remove(item: QuarantineItem): Promise<void> {
    await this.decide(item, 'delete');
  }

  private async decide(item: QuarantineItem, action: 'release' | 'delete'): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const justification = this.justification().trim();
    if (!workspaceId || justification.length < 3 || this.busy()) return;
    this.busy.set(true);
    try {
      if (action === 'release') {
        await this.api.release(workspaceId, item.id, justification);
      } else {
        await this.api.delete(workspaceId, item.id, justification);
      }
      this.items.update((rows) => rows.filter((row) => row.id !== item.id));
    } catch {
      this.loadError.set(true);
    } finally {
      this.busy.set(false);
    }
  }
}
