import { Component, OnInit, inject, signal } from '@angular/core';
import { SiemApiService, SiemDestinationView } from '../../core/api/siem-api.service';
import { DirectoryApiService } from '../../core/api/directory-api.service';
import { translateErrorCode, ui } from '../../core/i18n/strings';

@Component({
  selector: 'vc-siem-panel',
  standalone: true,
  template: `
    <section data-testid="siem-panel">
      <h2>{{ ui.siemTitle }}</h2>
      @if (loading()) {
        <p role="status">{{ ui.complianceLoading }}</p>
      } @else if (disabled()) {
        <p role="status" data-testid="siem-disabled">{{ ui.siemDisabled }}</p>
      } @else if (denied()) {
        <p role="status">{{ ui.complianceDenied }}</p>
      } @else {
        @if (notice()) {
          <p role="status">{{ notice() }}</p>
        }
        @if (secret()) {
          <p role="status" data-testid="siem-secret">{{ ui.siemSecretOnce }} {{ secret() }}</p>
        }
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        @if (rows().length === 0) {
          <p>{{ ui.siemEmpty }}</p>
        }
        @for (row of rows(); track row.id) {
          <article [attr.data-testid]="'siem-' + row.id">
            <p>{{ row.name }} · {{ row.transport }} · {{ row.circuit }}</p>
            <p>{{ ui.siemLag }}: {{ row.lagSeconds }} · {{ ui.siemHealth }}: {{ row.lastSuccessAt ?? ui.siemEmpty }}</p>
            <p>{{ row.secretMask }}</p>
            <button type="button" (click)="test(row)">{{ ui.siemTest }}</button>
            <button type="button" (click)="backfill(row)">{{ ui.siemBackfill }}</button>
            <button type="button" (click)="rotate(row)">{{ ui.siemRotate }}</button>
            <button type="button" (click)="revoke(row)">{{ ui.siemRevoke }}</button>
          </article>
        }
        <form (submit)="create($event)">
          <label>{{ ui.siemDestination }}
            <input data-testid="siem-name" [value]="name()" (input)="name.set(text($event))" />
          </label>
          <label>{{ ui.siemUrl }}
            <input data-testid="siem-url" [value]="url()" (input)="url.set(text($event))" />
          </label>
          <label>{{ ui.siemTransport }}
            <select data-testid="siem-transport" (change)="transport.set(text($event))">
              <option value="webhook-json">{{ ui.siemWebhook }}</option>
              <option value="syslog-cef">{{ ui.siemCef }}</option>
            </select>
          </label>
          <button type="submit">{{ ui.siemCreate }}</button>
        </form>
      }
    </section>
  `,
})
export class SiemPanel implements OnInit {
  private readonly siem = inject(SiemApiService);
  private readonly directory = inject(DirectoryApiService);
  protected readonly ui = ui;
  protected readonly loading = signal(true);
  protected readonly disabled = signal(false);
  protected readonly denied = signal(false);
  protected readonly notice = signal('');
  protected readonly error = signal('');
  protected readonly secret = signal('');
  protected readonly rows = signal<SiemDestinationView[]>([]);
  protected readonly name = signal('');
  protected readonly url = signal('');
  protected readonly transport = signal('webhook-json');
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

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set('');
    this.secret.set('');
    try {
      const row = await this.siem.create(this.workspaceId, this.name(), this.url(), this.transport());
      this.secret.set(row.secret ?? '');
      this.notice.set(ui.siemSaved);
      await this.load();
    } catch (err) {
      this.error.set(this.message(err));
    }
  }

  protected async test(row: SiemDestinationView): Promise<void> {
    await this.run(() => this.siem.test(this.workspaceId, row.id));
  }

  protected async backfill(row: SiemDestinationView): Promise<void> {
    await this.run(() => this.siem.backfill(this.workspaceId, row.id));
  }

  protected async rotate(row: SiemDestinationView): Promise<void> {
    this.secret.set('');
    try {
      const updated = await this.siem.rotate(this.workspaceId, row.id);
      this.secret.set(updated.secret ?? '');
      this.notice.set(ui.siemSaved);
      await this.load();
    } catch (err) {
      this.error.set(this.message(err));
    }
  }

  protected async revoke(row: SiemDestinationView): Promise<void> {
    await this.run(() => this.siem.revoke(this.workspaceId, row.id));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.error.set('');
    try {
      await action();
      this.notice.set(ui.siemSaved);
      await this.load();
    } catch (err) {
      this.error.set(this.message(err));
    }
  }

  private async load(): Promise<void> {
    try {
      this.rows.set(await this.siem.list(this.workspaceId));
      this.disabled.set(false);
      this.denied.set(false);
    } catch (error) {
      const status = (error as { status?: number }).status;
      if (status === 404) {
        this.disabled.set(true);
        return;
      }
      this.denied.set(true);
    }
  }

  private message(err: unknown): string {
    const raw = err instanceof Error ? err.message : '';
    const code = raw.match(/"error"\s*:\s*"([^"]+)"/)?.[1];
    return translateErrorCode(code) || ui.siemError;
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
