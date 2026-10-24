import { Component, inject, OnInit, signal } from '@angular/core';
import { BrandAssetKind, BrandingApiService, BrandView } from '../../core/api/branding-api.service';
import { fillTemplate, translateErrorCode, ui } from '../../core/i18n/strings';
import { BrandingService } from '../../core/services/branding.service';
import { Button } from '../../shared/ui';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-branding',
  standalone: true,
  imports: [Button],
  templateUrl: './admin-branding.page.html',
  styleUrls: ['./admin-shared.scss', './admin-branding.page.scss'],
})
export class AdminBrandingPage implements OnInit {
  readonly areaId: AdminAreaId = 'branding';
  readonly ui = ui;
  readonly fillTemplate = fillTemplate;

  private readonly api = inject(BrandingApiService);
  private readonly branding = inject(BrandingService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly denied = signal(false);
  readonly loadError = signal(false);
  readonly busy = signal(false);
  readonly feedback = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);
  readonly name = signal('');
  readonly brandColor = signal('');
  readonly accentColor = signal('');
  readonly view = signal<BrandView | null>(null);

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    if (!this.ctx.workspace()?.id) {
      this.loading.set(false);
      this.denied.set(true);
      return;
    }
    await this.load();
  }

  async save(event: Event): Promise<void> {
    event.preventDefault();
    await this.run(async () => {
      const saved = await this.api.saveBrand(
        {
          displayName: this.name().trim() || null,
          brandColor: this.brandColor().trim() || null,
          accentColor: this.accentColor().trim() || null,
        },
        crypto.randomUUID(),
      );
      this.apply(saved);
      this.feedback.set(ui.adminBrandSaved);
      await this.branding.reload();
    });
  }

  async rollback(): Promise<void> {
    await this.run(async () => {
      const saved = await this.api.rollback(crypto.randomUUID());
      this.apply(saved);
      this.feedback.set(ui.adminBrandRolledBack);
      await this.branding.reload();
    });
  }

  async onFile(kind: BrandAssetKind, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    await this.run(async () => {
      const saved = await this.api.uploadAsset(kind, file, crypto.randomUUID());
      this.apply(saved);
      this.feedback.set(ui.adminBrandSaved);
      await this.branding.reload();
    });
  }

  async clear(kind: BrandAssetKind): Promise<void> {
    await this.run(async () => {
      const saved = await this.api.clearAsset(kind, crypto.randomUUID());
      this.apply(saved);
      this.feedback.set(ui.adminBrandSaved);
      await this.branding.reload();
    });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(false);
    try {
      this.apply(await this.api.adminBrand());
    } catch (error) {
      const status = (error as { status?: number }).status;
      this.denied.set(status === 403);
      this.loadError.set(status !== 403);
    } finally {
      this.loading.set(false);
    }
  }

  read(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  private apply(view: BrandView): void {
    this.view.set(view);
    this.name.set(view.displayName ?? '');
    this.brandColor.set(view.brandColor ?? '');
    this.accentColor.set(view.accentColor ?? '');
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.feedback.set(null);
    this.errorMessage.set(null);
    try {
      await action();
    } catch (error) {
      const message = error instanceof Error ? error.message : '';
      const code = readError(message);
      this.errorMessage.set(code ? translateErrorCode(code) : ui.adminBrandLoadError);
    } finally {
      this.busy.set(false);
    }
  }
}

function readError(message: string): string {
  try {
    const body = JSON.parse(message) as { error?: string };
    return body.error ?? '';
  } catch {
    return '';
  }
}
