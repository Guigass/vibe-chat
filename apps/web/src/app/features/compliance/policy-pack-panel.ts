import { Component, OnInit, inject, signal } from '@angular/core';
import { PolicyControlView, PolicyPackApiService, PolicyPackCatalogItem } from '../../core/api/policy-pack-api.service';
import { DirectoryApiService } from '../../core/api/directory-api.service';
import { translateErrorCode, ui } from '../../core/i18n/strings';

@Component({
  selector: 'vc-policy-pack-panel',
  standalone: true,
  template: `
    <section data-testid="policy-pack-panel">
      <h2>{{ ui.policyPackTitle }}</h2>
      <p role="note">{{ ui.policyPackBaseline }}</p>
      @if (loading()) {
        <p role="status">{{ ui.complianceLoading }}</p>
      } @else if (disabled()) {
        <p role="status" data-testid="policy-pack-disabled">{{ ui.policyPackDisabled }}</p>
      } @else if (denied()) {
        <p role="status">{{ ui.complianceDenied }}</p>
      } @else {
        @if (notice()) {
          <p role="status">{{ notice() }}</p>
        }
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        @if (drifted()) {
          <p role="status" data-testid="policy-pack-drift">{{ ui.policyPackDrift }}</p>
        }
        @if (upgrade()) {
          <p role="status">{{ ui.policyPackUpgrade }}</p>
        }
        <label>{{ ui.policyPackPack }}
          <select data-testid="policy-pack-id" (change)="packId.set(text($event))">
            @for (pack of packs(); track pack.id) {
              <option [value]="pack.id" [selected]="pack.id === packId()">{{ pack.label }}</option>
            }
          </select>
        </label>
        @if (controls().length === 0) {
          <p>{{ ui.policyPackEmpty }}</p>
        }
        <ul>
          @for (control of controls(); track control.key) {
            <li>
              {{ control.key }} · {{ control.value }}
              @if (control.locked) {
                <span>{{ ui.policyPackLocked }}</span>
              }
            </li>
          }
        </ul>
        <button type="button" (click)="preview()">{{ ui.policyPackPreview }}</button>
        <button type="button" data-testid="policy-pack-apply" (click)="apply()">{{ ui.policyPackApply }}</button>
        <button type="button" (click)="rollback()">{{ ui.policyPackRollback }}</button>
        <button type="button" (click)="download()">{{ ui.policyPackExport }}</button>
      }
    </section>
  `,
})
export class PolicyPackPanel implements OnInit {
  private readonly packsApi = inject(PolicyPackApiService);
  private readonly directory = inject(DirectoryApiService);
  protected readonly ui = ui;
  protected readonly loading = signal(true);
  protected readonly disabled = signal(false);
  protected readonly denied = signal(false);
  protected readonly notice = signal('');
  protected readonly error = signal('');
  protected readonly drifted = signal(false);
  protected readonly upgrade = signal(false);
  protected readonly packs = signal<PolicyPackCatalogItem[]>([]);
  protected readonly packId = signal('');
  protected readonly controls = signal<PolicyControlView[]>([]);
  private workspaceId = '';
  private version = 1;

  async ngOnInit(): Promise<void> {
    try {
      const workspaces = await this.directory.getWorkspaces();
      const workspace = workspaces[0];
      if (!workspace) {
        this.denied.set(true);
        return;
      }
      this.workspaceId = workspace.id;
      const catalog = await this.packsApi.catalog(workspace.id);
      this.packs.set(catalog.packs);
      this.packId.set(catalog.packs[0]?.id ?? '');
      this.version = catalog.packs[0]?.version ?? 1;
      const drift = await this.packsApi.drift(workspace.id);
      this.drifted.set(drift.drifted);
      this.upgrade.set(drift.upgradeAvailable);
      if (drift.assigned) {
        this.controls.set(drift.controls);
      }
    } catch (error) {
      if (statusOf(error) === 404) {
        this.disabled.set(true);
        return;
      }
      this.denied.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected async preview(): Promise<void> {
    await this.run(async () => {
      const document = await this.packsApi.preview(this.workspaceId, this.packId(), this.versionFor());
      this.controls.set(document.controls ?? []);
    });
  }

  protected async apply(): Promise<void> {
    await this.run(async () => {
      const document = await this.packsApi.apply(this.workspaceId, this.packId(), this.versionFor());
      this.controls.set(document.controls ?? []);
      this.notice.set(ui.policyPackSaved);
      this.drifted.set(false);
    });
  }

  protected async rollback(): Promise<void> {
    await this.run(async () => {
      const document = await this.packsApi.rollback(this.workspaceId);
      this.controls.set(document.controls ?? []);
      this.notice.set(ui.policyPackSaved);
    });
  }

  protected async download(): Promise<void> {
    await this.run(async () => {
      await this.packsApi.exportPack(this.workspaceId);
      this.notice.set(ui.policyPackExport);
    });
  }

  protected text(event: Event): string {
    return (event.target as HTMLSelectElement).value;
  }

  private versionFor(): number {
    return this.packs().find((pack) => pack.id === this.packId())?.version ?? this.version;
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.error.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(translateErrorCode(codeOf(error)) || ui.policyPackError);
    }
  }
}

function statusOf(error: unknown): number {
  return typeof error === 'object' && error !== null && 'status' in error ? Number((error as { status: number }).status) : 0;
}

function codeOf(error: unknown): string {
  if (!(error instanceof Error)) {
    return '';
  }
  try {
    const parsed = JSON.parse(error.message) as { error?: string };
    return parsed.error ?? '';
  } catch {
    return '';
  }
}
