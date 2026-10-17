import { Component, effect, inject, signal } from '@angular/core';
import { IncidentApiService, IncidentDetail, IncidentSeverity } from '../../../core/api/incident-api.service';
import { translateErrorCode, ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';

@Component({
  selector: 'vc-incident-banner',
  standalone: true,
  template: `
    @if (incident(); as room) {
      <section class="incident" [attr.data-severity]="room.severity" data-testid="incident-banner" [attr.aria-label]="ui.incidentBanner">
        <div class="incident__row">
          <p class="incident__meta">
            <strong>{{ room.title }}</strong>
            <span data-testid="incident-severity">{{ severityLabel(room.severity) }}</span>
            <span data-testid="incident-status">{{ statusLabel(room.status) }}</span>
          </p>
          <div class="incident__actions">
            @if (room.status === 'declared') {
              <button type="button" data-testid="incident-mitigate" (click)="transition('mitigating')">{{ ui.incidentMitigate }}</button>
            }
            @if (room.status === 'mitigating' && !confirming()) {
              <button type="button" data-testid="incident-resolve" (click)="confirming.set(true)">{{ ui.incidentResolve }}</button>
            }
            @if (confirming()) {
              <button type="button" data-testid="incident-resolve-confirm" (click)="transition('resolved')">{{ ui.incidentResolveConfirm }}</button>
            }
            <button type="button" data-testid="incident-command" [attr.aria-expanded]="open()" (click)="open.set(!open())">{{ ui.incidentCommand }}</button>
          </div>
        </div>
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        @if (open()) {
          <div class="incident__panel" data-testid="incident-panel">
            <h2>{{ ui.incidentSteps }}</h2>
            <ul>
              @for (step of room.steps; track step.id) {
                <li class="incident__step">
                  <span>{{ stepLabel(step.code) }}</span>
                  <span>{{ step.status }}</span>
                  @if (room.status !== 'resolved') {
                    <button type="button" (click)="run(step.id)">{{ ui.incidentRunStep }}</button>
                  }
                  @if (step.errorCode) {
                    <span role="status">{{ ui.incidentAutomationFailed }}</span>
                  }
                </li>
              }
            </ul>
            <h2>{{ ui.incidentTimeline }}</h2>
            @if (room.timeline.length === 0) {
              <p>{{ ui.incidentEmptyTimeline }}</p>
            } @else {
              <ul>
                @for (item of room.timeline; track item.id) {
                  <li>
                    <span>{{ item.kind }}</span>
                    <span>{{ item.sourceId }}</span>
                    @if (item.note) {
                      <span>{{ item.note }}</span>
                    }
                  </li>
                }
              </ul>
            }
            @if (room.status !== 'resolved') {
              <form (submit)="postUpdate($event)">
                <label>{{ ui.incidentUpdateLabel }}
                  <input data-testid="incident-note" [value]="note()" (input)="note.set(valueOf($event))" />
                </label>
                <button type="submit" data-testid="incident-update">{{ ui.incidentUpdate }}</button>
              </form>
            }
            @if (room.canExport) {
              <button type="button" data-testid="incident-export" (click)="exportPostmortem()">{{ ui.incidentExport }}</button>
              @if (exported()) {
                <p role="status" data-testid="incident-exported">{{ ui.incidentExport }}</p>
              }
            }
          </div>
        }
      </section>
    } @else if (canManage()) {
      <section class="incident incident--declare" [attr.aria-label]="ui.incidentDeclare">
        <button type="button" data-testid="incident-declare" (click)="declaring.set(!declaring())">{{ ui.incidentDeclare }}</button>
        @if (declaring()) {
          <form (submit)="create($event)">
            <label>{{ ui.incidentTitleLabel }}
              <input data-testid="incident-title" [value]="title()" (input)="title.set(valueOf($event))" required />
            </label>
            <label>{{ ui.incidentSeverity }}
              <select data-testid="incident-severity-input" [value]="severity()" (change)="severity.set(severityOf($event))">
                <option value="sev1">{{ ui.incidentSev1 }}</option>
                <option value="sev2">{{ ui.incidentSev2 }}</option>
                <option value="sev3">{{ ui.incidentSev3 }}</option>
                <option value="sev4">{{ ui.incidentSev4 }}</option>
              </select>
            </label>
            <button type="submit" data-testid="incident-create">{{ ui.incidentCreate }}</button>
          </form>
        }
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
      </section>
    }
  `,
  styles: `
    .incident {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      padding: 0.55rem 0.9rem;
      border-bottom: 1px solid var(--vc-border);
      background: color-mix(in srgb, var(--vc-warning) 12%, var(--vc-surface-elevated));
      color: var(--vc-ink);
    }

    .incident[data-severity='sev1'] {
      background: color-mix(in srgb, var(--vc-danger) 14%, var(--vc-surface-elevated));
    }

    .incident--declare {
      background: var(--vc-surface);
    }

    .incident__row, .incident__actions, .incident__meta, .incident__step, form {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }

    .incident__meta, .incident__step {
      margin: 0;
    }

    .incident__row {
      justify-content: space-between;
    }

    button, input, select {
      font: inherit;
      color: inherit;
    }

    button {
      min-height: 2rem;
      padding: 0.2rem 0.7rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm, 0.4rem);
      background: var(--vc-surface-elevated);
    }

    .incident__panel {
      display: flex;
      flex-direction: column;
      gap: 0.4rem;
    }

    h2 {
      margin: 0;
      font-size: 0.85rem;
    }

    ul {
      margin: 0;
      padding-left: 1.1rem;
    }
  `,
})
export class IncidentBanner {
  readonly ui = ui;
  private readonly api = inject(IncidentApiService);
  private readonly channels = inject(ChannelStore);
  readonly incident = signal<IncidentDetail | null>(null);
  readonly canManage = signal(false);
  readonly declaring = signal(false);
  readonly confirming = signal(false);
  readonly open = signal(false);
  readonly exported = signal(false);
  readonly title = signal('');
  readonly note = signal('');
  readonly severity = signal<IncidentSeverity>('sev3');
  readonly error = signal<string | null>(null);
  private createKey = crypto.randomUUID();
  private updateKey = crypto.randomUUID();

  constructor() {
    effect(() => {
      const workspaceId = this.channels.activeWorkspace()?.id;
      const channelId = this.channels.activeChannelId();
      if (!workspaceId || !channelId || this.channels.isDemo()) {
        this.incident.set(null);
        this.canManage.set(false);
        return;
      }
      void this.load(workspaceId, channelId);
    });
  }

  async create(event: Event): Promise<void> {
    event.preventDefault();
    const workspaceId = this.channels.activeWorkspace()?.id;
    const title = this.title().trim();
    if (!workspaceId || !title) return;
    this.error.set(null);
    try {
      const created = await this.api.create(workspaceId, {
        idempotencyKey: this.createKey,
        templateKey: 'default',
        title,
        severity: this.severity(),
      });
      this.createKey = crypto.randomUUID();
      this.declaring.set(false);
      this.title.set('');
      this.incident.set(created);
      await this.channels.selectWorkspace(workspaceId);
      this.channels.selectChannel(created.channelId);
    } catch (err) {
      this.error.set(message(err));
    }
  }

  async transition(status: 'mitigating' | 'resolved'): Promise<void> {
    const room = this.incident();
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!room || !workspaceId) return;
    this.error.set(null);
    try {
      this.incident.set(await this.api.transition(workspaceId, room.id, status));
      this.confirming.set(false);
    } catch (err) {
      this.error.set(message(err));
    }
  }

  async postUpdate(event: Event): Promise<void> {
    event.preventDefault();
    const room = this.incident();
    const workspaceId = this.channels.activeWorkspace()?.id;
    const note = this.note().trim();
    if (!room || !workspaceId || !note) return;
    this.error.set(null);
    try {
      this.incident.set(await this.api.update(workspaceId, room.id, this.updateKey, note));
      this.updateKey = crypto.randomUUID();
      this.note.set('');
    } catch (err) {
      this.error.set(message(err));
    }
  }

  async run(stepId: string): Promise<void> {
    const room = this.incident();
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!room || !workspaceId) return;
    this.error.set(null);
    try {
      this.incident.set(await this.api.runStep(workspaceId, room.id, stepId));
    } catch (err) {
      this.error.set(message(err));
    }
  }

  async exportPostmortem(): Promise<void> {
    const room = this.incident();
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!room || !workspaceId) return;
    this.error.set(null);
    try {
      await this.api.postmortem(workspaceId, room.id);
      this.exported.set(true);
    } catch (err) {
      this.error.set(message(err));
    }
  }

  severityLabel(value: string): string {
    if (value === 'sev1') return ui.incidentSev1;
    if (value === 'sev2') return ui.incidentSev2;
    if (value === 'sev4') return ui.incidentSev4;
    return ui.incidentSev3;
  }

  statusLabel(value: string): string {
    if (value === 'declared') return ui.incidentStatusDeclared;
    if (value === 'mitigating') return ui.incidentStatusMitigating;
    return ui.incidentStatusResolved;
  }

  stepLabel(code: string): string {
    if (code === 'acknowledge') return ui.incidentStepAcknowledge;
    if (code === 'mitigate') return ui.incidentStepMitigate;
    if (code === 'communicate') return ui.incidentStepCommunicate;
    return ui.incidentStepPostmortem;
  }

  valueOf(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  severityOf(event: Event): IncidentSeverity {
    return (event.target as HTMLSelectElement).value as IncidentSeverity;
  }

  private async load(workspaceId: string, channelId: string): Promise<void> {
    try {
      const current = await this.api.current(workspaceId, channelId);
      this.canManage.set(current.canManage);
      this.incident.set(current.incident);
      this.error.set(null);
      if (!current.incident) this.confirming.set(false);
    } catch (err) {
      this.incident.set(null);
      this.canManage.set(false);
      const text = err instanceof Error ? err.message : '';
      if (!text.includes('IncidentDisabled') && !text.includes('404')) {
        this.error.set(message(err));
      }
    }
  }
}

function message(error: unknown): string {
  if (!(error instanceof Error) || !error.message) return ui.incidentSaveError;
  try {
    const parsed = JSON.parse(error.message) as { error?: string };
    return parsed.error ? translateErrorCode(parsed.error) : ui.incidentSaveError;
  } catch {
    return ui.incidentSaveError;
  }
}
