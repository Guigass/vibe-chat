import { Component, inject, OnInit, signal } from '@angular/core';
import { StorageApiService, StorageUsage } from '../../core/api/storage-api.service';
import { fillTemplate, ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-storage',
  standalone: true,
  templateUrl: './admin-storage.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminStoragePage implements OnInit {
  readonly areaId: AdminAreaId = 'storage';
  readonly ui = ui;

  private readonly api = inject(StorageApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly denied = signal(false);
  readonly loadError = signal(false);
  readonly notice = signal('');
  readonly usage = signal<StorageUsage | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  kindLabel(kind: string): string {
    switch (kind) {
      case 'attachment':
        return ui.storageKindAttachment;
      case 'preview':
        return ui.storageKindPreview;
      case 'export':
        return ui.storageKindExport;
      case 'recording':
        return ui.storageKindRecording;
      default:
        return ui.storageKind;
    }
  }

  trendLabel(trend: string): string {
    switch (trend) {
      case 'up':
        return ui.storageTrendUp;
      case 'down':
        return ui.storageTrendDown;
      default:
        return ui.storageTrendFlat;
    }
  }

  actionLabel(action: string): string {
    switch (action) {
      case 'warning':
        return ui.storageActionWarning;
      case 'blocked':
        return ui.storageActionBlocked;
      default:
        return ui.storageActionNone;
    }
  }

  largestLabel(kind: string): string {
    return fillTemplate(ui.storageLargest, { kind: this.kindLabel(kind) });
  }

  async reconcile(): Promise<void> {
    await this.run((workspaceId) => this.api.reconcile(workspaceId, crypto.randomUUID()));
  }

  async collect(): Promise<void> {
    await this.run((workspaceId) => this.api.collect(workspaceId, crypto.randomUUID()));
  }

  private async run(action: (workspaceId: string) => Promise<unknown>): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      return;
    }

    this.notice.set('');
    try {
      await action(workspaceId);
      this.notice.set(ui.storageSaved);
      await this.load();
    } catch {
      this.loadError.set(true);
    }
  }

  private async load(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.denied.set(true);
      return;
    }

    this.loading.set(true);
    this.loadError.set(false);
    try {
      this.usage.set(await this.api.usage(workspaceId));
    } catch (error) {
      const status = (error as { status?: number }).status;
      const message = error instanceof Error ? error.message : '';
      if (status === 404 && message.includes('StorageLifecycleDisabled')) {
        this.disabled.set(true);
      } else if (status === 403) {
        this.denied.set(true);
      } else {
        this.loadError.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }
}
