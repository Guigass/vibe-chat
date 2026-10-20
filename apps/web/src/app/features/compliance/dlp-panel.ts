import { Component, OnInit, inject, signal } from '@angular/core';
import { DlpApiService, DlpMetricsView } from '../../core/api/dlp-api.service';
import { DirectoryApiService } from '../../core/api/directory-api.service';
import { translateErrorCode, ui } from '../../core/i18n/strings';

@Component({
  selector: 'vc-dlp-panel',
  standalone: true,
  template: `
    <section data-testid="dlp-panel">
      <h2>{{ ui.dlpTitle }}</h2>
      @if (loading()) {
        <p role="status">{{ ui.complianceLoading }}</p>
      } @else if (disabled()) {
        <p role="status" data-testid="dlp-disabled">{{ ui.dlpDisabled }}</p>
      } @else if (denied()) {
        <p role="status">{{ ui.complianceDenied }}</p>
      } @else {
        @if (notice()) {
          <p role="status">{{ notice() }}</p>
        }
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        <p>{{ simulation() ? ui.dlpSimulation : ui.dlpEnforce }}</p>
        <p>{{ ui.dlpReview }}</p>
        <button type="button" data-testid="dlp-mode" (click)="toggle()">
          {{ simulation() ? ui.dlpEnforce : ui.dlpSimulation }}
        </button>
        <p data-testid="dlp-metrics">{{ ui.dlpMetrics }}: {{ metrics().simulated }}/{{ metrics().total }}</p>
        <form (submit)="assign($event)">
          <label>{{ ui.dlpChannel }}
            <input data-testid="dlp-channel" [value]="channelId()" (input)="channelId.set(text($event))" />
          </label>
          <label>{{ ui.dlpBadge }}
            <select data-testid="dlp-label-key" (change)="labelKey.set(text($event))">
              <option value="public">{{ ui.dlpPublic }}</option>
              <option value="internal">{{ ui.dlpInternal }}</option>
              <option value="confidential">{{ ui.dlpConfidential }}</option>
              <option value="restricted">{{ ui.dlpRestricted }}</option>
            </select>
          </label>
          <button type="submit">{{ ui.dlpAssign }}</button>
        </form>
      }
    </section>
  `,
})
export class DlpPanel implements OnInit {
  private readonly dlp = inject(DlpApiService);
  private readonly directory = inject(DirectoryApiService);
  protected readonly ui = ui;
  protected readonly loading = signal(true);
  protected readonly disabled = signal(false);
  protected readonly denied = signal(false);
  protected readonly simulation = signal(true);
  protected readonly notice = signal('');
  protected readonly error = signal('');
  protected readonly metrics = signal<DlpMetricsView>({ total: 0, simulated: 0, blocked: 0 });
  protected readonly channelId = signal('');
  protected readonly labelKey = signal('internal');
  private workspaceId = '';

  async ngOnInit(): Promise<void> {
    try {
      const workspaces = await this.directory.getWorkspaces();
      const workspace = workspaces[0];
      if (!workspace) {
        this.denied.set(true);
        return;
      }
      this.workspaceId = workspace.id;
      await this.load();
    } catch {
      this.denied.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  async toggle(): Promise<void> {
    await this.run(async () => {
      const next = await this.dlp.setPolicy(this.workspaceId, !this.simulation());
      this.simulation.set(next.simulation);
      this.notice.set(ui.dlpSaved);
    });
  }

  async assign(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.channelId()) {
      return;
    }
    await this.run(async () => {
      await this.dlp.assign(this.workspaceId, 'channel', this.channelId(), this.labelKey());
      this.notice.set(ui.dlpSaved);
    });
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  private async load(): Promise<void> {
    try {
      const policy = await this.dlp.policy(this.workspaceId);
      this.simulation.set(policy.simulation);
      this.metrics.set(await this.dlp.metrics(this.workspaceId));
    } catch (error) {
      const status = (error as { status?: number }).status;
      if (status === 404) {
        this.disabled.set(true);
        return;
      }
      if (status === 403) {
        this.denied.set(true);
        return;
      }
      this.error.set(ui.complianceError);
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.error.set('');
    try {
      await action();
      this.metrics.set(await this.dlp.metrics(this.workspaceId));
    } catch (error) {
      const raw = error instanceof Error ? error.message : '';
      const code = raw.match(/"error"\s*:\s*"([^"]+)"/)?.[1];
      this.error.set(translateErrorCode(code) || ui.complianceError);
    }
  }
}
