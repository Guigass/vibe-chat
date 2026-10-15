import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DigestApiService, DigestSection, DigestSettings, DigestView } from '../../../core/api/digest-api.service';
import { DirectoryApiService } from '../../../core/api/directory-api.service';
import { ui } from '../../../core/i18n/strings';
import { Workspace } from '../../../shared/models/chat.models';

@Component({
  selector: 'vc-catch-up-page',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="catch" data-testid="catch-up-page">
      <header class="catch__bar">
        <a routerLink="/app">{{ ui.catchUpBack }}</a>
        <h1>{{ ui.catchUpTitle }}</h1>
      </header>
      <p class="catch__lead">{{ ui.catchUpLead }}</p>

      @if (disabled()) {
        <p role="status" data-testid="catch-up-disabled">{{ ui.catchUpDisabled }}</p>
      } @else {
        <form class="catch__settings" (submit)="save($event)">
          <h2>{{ ui.catchUpSettings }}</h2>
          <label><input type="checkbox" [checked]="enabled()" (change)="enabled.set(checked($event))" /> {{ ui.catchUpEnabled }}</label>
          <label>
            {{ ui.catchUpDaily }} / {{ ui.catchUpWeekly }}
            <select [value]="cadence()" (change)="setCadence($event)" data-testid="catch-up-cadence">
              <option value="daily">{{ ui.catchUpDaily }}</option>
              <option value="weekly">{{ ui.catchUpWeekly }}</option>
            </select>
          </label>
          <label>
            {{ ui.catchUpTime }}
            <input type="time" [value]="localTime()" (input)="localTime.set(value($event))" />
          </label>
          @if (cadence() === 'weekly') {
            <label>
              {{ ui.catchUpWeekday }}
              <select [value]="weekday()" (change)="weekday.set(+value($event))">
                @for (day of days; track day.value) {
                  <option [value]="day.value">{{ day.label }}</option>
                }
              </select>
            </label>
          }
          <label>
            {{ ui.catchUpDelivery }}
            <select [value]="delivery()" (change)="setDelivery($event)">
              <option value="in_app">{{ ui.catchUpInApp }}</option>
              <option value="email">{{ ui.catchUpEmail }}</option>
              <option value="push">{{ ui.catchUpPush }}</option>
            </select>
          </label>
          <label><input type="checkbox" [checked]="includeAi()" (change)="includeAi.set(checked($event))" /> {{ ui.catchUpIncludeAi }}</label>
          <button type="submit" class="vc-btn" data-testid="catch-up-save" [disabled]="saving()">{{ ui.catchUpSave }}</button>
          @if (saved()) {
            <p role="status">{{ ui.catchUpSaved }}</p>
          }
        </form>

        @if (workspaces().length > 1) {
          <label>
            {{ ui.workspace }}
            <select [value]="workspaceId()" (change)="workspaceId.set(value($event))">
              @for (workspace of workspaces(); track workspace.id) {
                <option [value]="workspace.id">{{ workspace.name }}</option>
              }
            </select>
          </label>
        }

        <button type="button" class="vc-btn" data-testid="catch-up-generate" (click)="generate()" [disabled]="loading() || !workspaceId()">
          {{ loading() ? ui.catchUpLoading : ui.catchUpGenerate }}
        </button>

        @if (error()) {
          <p role="alert">{{ ui.catchUpError }}</p>
        }
        @if (digest(); as view) {
          <p data-testid="catch-up-ai" role="status">{{ aiLabel(view) }}</p>
          @if (view.summary) {
            <p>{{ view.summary }}</p>
          }
          @if (view.externalDeferred || view.status === 'partial') {
            <p>{{ ui.catchUpPartial }}</p>
          }
          @if (itemCount(view) === 0) {
            <p data-testid="catch-up-empty">{{ ui.catchUpEmpty }}</p>
          }
          @for (section of view.sections; track section.kind) {
            @if (section.items.length) {
              <section>
                <h2>{{ sectionLabel(section.kind) }}</h2>
                <ul>
                  @for (item of section.items; track item.sourceId) {
                    <li>
                      <span>{{ item.channelName }}</span>
                      <a
                        data-testid="catch-up-item"
                        [href]="openHref(item.channelId, item.messageId, item.sequence)"
                      >{{ ui.catchUpOpen }}</a>
                    </li>
                  }
                </ul>
              </section>
            }
          }
        }
      }
    </main>
  `,
  styles: `
    .catch {
      max-width: 40rem;
      margin: 0 auto;
      padding: var(--vc-space-6);
      color: var(--vc-ink);
      background: var(--vc-surface);
      min-height: 100dvh;
      font-family: var(--vc-font-body);
    }
    h1, h2 { font-family: var(--vc-font-display); font-weight: 560; }
    .catch__lead, .catch__bar { color: var(--vc-ink-muted); }
    .catch__settings, section { display: grid; gap: var(--vc-space-3); margin: var(--vc-space-5) 0; }
    label { display: grid; gap: var(--vc-space-1); }
    a { color: var(--vc-brand); }
    ul { list-style: none; padding: 0; margin: 0; display: grid; gap: var(--vc-space-2); }
    li { display: flex; justify-content: space-between; gap: var(--vc-space-3); padding-block: var(--vc-space-2); border-bottom: 1px solid var(--vc-border); }
  `,
})
export class CatchUpPage implements OnInit {
  readonly ui = ui;
  private readonly digests = inject(DigestApiService);
  private readonly directory = inject(DirectoryApiService);
  readonly disabled = signal(false);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly saved = signal(false);
  readonly error = signal(false);
  readonly workspaces = signal<Workspace[]>([]);
  readonly workspaceId = signal('');
  readonly digest = signal<DigestView | null>(null);
  readonly enabled = signal(false);
  readonly cadence = signal<'daily' | 'weekly'>('daily');
  readonly localTime = signal('08:00');
  readonly weekday = signal(1);
  readonly delivery = signal<'in_app' | 'email' | 'push'>('in_app');
  readonly includeAi = signal(false);
  readonly days = [
    { value: 0, label: ui.catchUpSun },
    { value: 1, label: ui.catchUpMon },
    { value: 2, label: ui.catchUpTue },
    { value: 3, label: ui.catchUpWed },
    { value: 4, label: ui.catchUpThu },
    { value: 5, label: ui.catchUpFri },
    { value: 6, label: ui.catchUpSat },
  ];

  async ngOnInit(): Promise<void> {
    try {
      const settings = await this.digests.getSettings();
      this.apply(settings);
      const workspaces = await this.directory.getWorkspaces();
      this.workspaces.set(workspaces);
      this.workspaceId.set(workspaces[0]?.id ?? '');
    } catch (error) {
      if (statusOf(error) === 404) {
        this.disabled.set(true);
        return;
      }
      this.error.set(true);
    }
  }

  async save(event: Event): Promise<void> {
    event.preventDefault();
    this.saving.set(true);
    this.saved.set(false);
    this.error.set(false);
    try {
      const saved = await this.digests.saveSettings({
        enabled: this.enabled(),
        cadence: this.cadence(),
        localTime: this.localTime(),
        weekday: this.weekday(),
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
        delivery: this.delivery(),
        includeAi: this.includeAi(),
        channelIds: [],
      });
      this.apply(saved);
      this.saved.set(true);
    } catch {
      this.error.set(true);
    } finally {
      this.saving.set(false);
    }
  }

  async generate(): Promise<void> {
    const workspaceId = this.workspaceId();
    if (!workspaceId) {
      this.error.set(true);
      return;
    }
    this.loading.set(true);
    this.error.set(false);
    try {
      const view = await this.digests.catchUp(workspaceId, `catch-${crypto.randomUUID()}`);
      this.digest.set(view);
    } catch (error) {
      if (statusOf(error) === 404) {
        this.disabled.set(true);
        return;
      }
      this.error.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  sectionLabel(kind: DigestSection['kind']): string {
    switch (kind) {
      case 'unread':
        return ui.catchUpUnread;
      case 'mention':
        return ui.catchUpMention;
      case 'announcement':
        return ui.catchUpAnnouncement;
      case 'decision':
        return ui.catchUpDecision;
      default:
        return ui.catchUpTask;
    }
  }

  aiLabel(view: DigestView): string {
    if (view.aiStatus === 'failed') {
      return ui.catchUpAiFailed;
    }
    if (view.aiStatus === 'ready') {
      return ui.catchUpAiReady;
    }
    return ui.catchUpAiOff;
  }

  itemCount(view: DigestView): number {
    return view.sections.reduce((total, section) => total + section.items.length, 0);
  }

  openHref(channelId: string, messageId?: string | null, sequence?: number | null): string {
    const params = new URLSearchParams({ channel: channelId });
    if (messageId) {
      params.set('message', messageId);
    }
    if (sequence) {
      params.set('seq', String(sequence));
    }
    return `/app?${params.toString()}`;
  }

  checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  setCadence(event: Event): void {
    this.cadence.set(this.value(event) === 'weekly' ? 'weekly' : 'daily');
  }

  setDelivery(event: Event): void {
    const value = this.value(event);
    this.delivery.set(value === 'email' || value === 'push' ? value : 'in_app');
  }

  private apply(settings: DigestSettings): void {
    this.enabled.set(settings.enabled);
    this.cadence.set(settings.cadence === 'weekly' ? 'weekly' : 'daily');
    this.localTime.set(settings.localTime?.slice(0, 5) || '08:00');
    this.weekday.set(settings.weekday ?? 1);
    this.delivery.set(settings.delivery === 'email' || settings.delivery === 'push' ? settings.delivery : 'in_app');
    this.includeAi.set(!!settings.includeAi);
  }
}

function statusOf(error: unknown): number {
  return typeof error === 'object' && error !== null && 'status' in error ? Number((error as { status: number }).status) : 0;
}
