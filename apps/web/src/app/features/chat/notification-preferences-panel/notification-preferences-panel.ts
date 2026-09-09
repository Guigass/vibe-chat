import { Component, ElementRef, WritableSignal, afterRenderEffect, computed, effect, inject, signal, viewChild } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { LocaleService } from '../../../core/i18n/locale.service';
import { ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';
import { idsEqual } from '../../../core/services/message-sync';
import { NotificationPreferencesStore } from '../../../core/services/notification-preferences.store';
import { NotificationLevel, NotificationPreferences, WorkspaceMember } from '../../../shared/models/chat.models';
import { Button, IconButton, Input } from '../../../shared/ui';

const PRIORITY_CONTACT_LIMIT = 50;

function detectTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

function listTimeZones(current: string): string[] {
  let zones: string[] = [];
  try {
    const intl = Intl as typeof Intl & { supportedValuesOf?(key: string): string[] };
    if (typeof intl.supportedValuesOf === 'function') {
      zones = intl.supportedValuesOf('timeZone');
    }
  } catch {
    zones = [];
  }
  if (zones.length === 0) {
    zones = ['UTC', 'America/Sao_Paulo', 'America/New_York', 'Europe/Lisbon', 'Europe/London'];
  }
  return current && !zones.includes(current) ? [current, ...zones] : zones;
}

function timeZoneLabel(zone: string, locale: string): string {
  try {
    const offset = new Intl.DateTimeFormat(locale, { timeZone: zone, timeZoneName: 'shortOffset' })
      .formatToParts(new Date())
      .find((part) => part.type === 'timeZoneName')?.value;
    const name = zone.replaceAll('_', ' ');
    return offset ? `${name} (${offset})` : name;
  } catch {
    return zone;
  }
}

function weekdayLabels(locale: string): Array<{ bit: number; label: string }> {
  const formatter = new Intl.DateTimeFormat(locale, { weekday: 'narrow' });
  return Array.from({ length: 7 }, (_, day) => ({
    bit: 1 << day,
    label: formatter.format(new Date(2024, 0, 7 + day)),
  }));
}

@Component({
  selector: 'vc-notification-preferences-panel',
  standalone: true,
  imports: [Button, IconButton, Input],
  template: `
    <section class="notif-panel" data-testid="notif-panel" [attr.aria-label]="ui.notificationPrefs">
      <header class="notif-panel__header">
        <h2>{{ ui.notifTitle }}</h2>
        <vc-icon-button [label]="ui.closePanel" (click)="store.closePanel()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      @if (store.loading() && !store.preferences()) {
        <p class="notif-panel__status">{{ ui.notifLoading }}</p>
      } @else {
        @if (store.error()) {
          <p class="notif-panel__status" role="alert">{{ store.error() }}</p>
        }

        <fieldset class="notif-panel__group">
          <legend>{{ ui.notifWhen }}</legend>
          @for (option of levelOptions; track option.value) {
            <label class="notif-panel__choice">
              <input
                type="radio"
                name="notif-level"
                [checked]="level() === option.value"
                (change)="level.set(option.value)"
              />
              {{ option.label }}
            </label>
          }
        </fieldset>

        <label class="notif-panel__choice">
          <input type="checkbox" [checked]="hidePreview()" (change)="onChecked($event, hidePreview)" />
          {{ ui.notifHidePreview }}
        </label>

        <fieldset class="notif-panel__group">
          <legend>
            <label class="notif-panel__choice notif-panel__choice--legend">
              <input type="checkbox" [checked]="dndEnabled()" (change)="onChecked($event, dndEnabled)" />
              {{ ui.notifDnd }}
            </label>
          </legend>
          @if (dndEnabled()) {
            <div class="notif-panel__dnd">
              <vc-input [label]="ui.notifFrom" type="time" [(value)]="dndStart" />
              <vc-input [label]="ui.notifTo" type="time" [(value)]="dndEnd" />
            </div>
            <div class="notif-panel__days" role="group" [attr.aria-label]="ui.notifDaysHint">
              @for (day of dayLabels(); track day.bit) {
                <button
                  type="button"
                  class="notif-panel__day"
                  [class.is-active]="isDaySelected(day.bit)"
                  [attr.aria-pressed]="isDaySelected(day.bit)"
                  (click)="toggleDay(day.bit)"
                >
                  {{ day.label }}
                </button>
              }
            </div>
            <p class="notif-panel__hint">{{ ui.notifDaysHint }}</p>
            <label class="notif-panel__field">
              <span>{{ ui.notifTimeZone }}</span>
              <select
                #timeZoneEl
                data-testid="notif-timezone"
                [attr.aria-label]="ui.notifTimeZone"
                (change)="onTimeZoneChange($event)"
              >
                @for (option of timeZoneOptions(); track option.id) {
                  <option [value]="option.id" [selected]="option.id === timeZone()">
                    {{ option.label }}
                  </option>
                }
              </select>
            </label>

            <div class="notif-panel__priority">
              <p class="notif-panel__hint">{{ ui.notifPriority }}</p>
              @if (selectedPriorityContacts().length) {
                <ul class="notif-panel__chips" data-testid="notif-priority">
                  @for (member of selectedPriorityContacts(); track member.userId) {
                    <li>
                      <span>{{ member.displayName }}</span>
                      <button
                        type="button"
                        class="notif-panel__chip-remove"
                        [attr.aria-label]="ui.notifPriorityRemove + ' ' + member.displayName"
                        (click)="togglePriorityContact(member.userId)"
                      >
                        ×
                      </button>
                    </li>
                  }
                </ul>
              }
              <vc-input
                [label]="ui.notifPrioritySearch"
                type="search"
                [placeholder]="ui.notifPrioritySearch"
                [(value)]="contactQuery"
              />
              @if (contactQuery().trim()) {
                @if (prioritySuggestions().length === 0) {
                  <p class="notif-panel__hint" role="status">{{ ui.notifPriorityNone }}</p>
                } @else {
                  <ul class="notif-panel__suggestions" data-testid="notif-priority-suggest">
                    @for (member of prioritySuggestions(); track member.userId) {
                      <li>
                        <button type="button" class="notif-panel__suggest" (click)="addPriorityContact(member.userId)">
                          {{ member.displayName }}
                        </button>
                      </li>
                    }
                  </ul>
                }
              }
            </div>
          }
        </fieldset>

        <label class="notif-panel__choice notif-panel__choice--stack">
          <span>
            <input type="checkbox" [checked]="digestEnabled()" (change)="onChecked($event, digestEnabled)" />
            {{ ui.notifDigest }}
          </span>
          <small class="notif-panel__hint">{{ ui.notifDigestSoon }}</small>
        </label>

        <div class="notif-panel__actions">
          <vc-button type="button" variant="primary" [loading]="saving()" [disabled]="saving()" (click)="save()">
            {{ saving() ? ui.notifSaving : ui.notifSave }}
          </vc-button>
          @if (saved()) {
            <span class="notif-panel__saved" role="status">{{ ui.notifSaved }}</span>
          }
        </div>
      }
    </section>
  `,
  styles: `
    .notif-panel {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-4);
      height: 100%;
      min-height: 0;
      overflow-y: auto;
      padding: var(--vc-space-4);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
    }
    .notif-panel__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }
    .notif-panel__header h2 {
      margin: 0;
      font-family: var(--vc-font-display);
      font-size: 1rem;
      font-weight: 600;
      color: var(--vc-ink);
    }
    .notif-panel__status {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.85rem;
    }
    .notif-panel__group {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      padding: var(--vc-space-3);
      margin: 0;
      background: var(--vc-surface);
    }
    .notif-panel__group legend {
      padding: 0 var(--vc-space-1);
      font-size: 0.8rem;
      font-weight: 600;
      color: var(--vc-ink-muted);
    }
    .notif-panel__choice {
      display: flex;
      align-items: center;
      gap: var(--vc-space-2);
      font-size: 0.85rem;
      color: var(--vc-ink);
    }
    .notif-panel__choice input {
      accent-color: var(--vc-brand);
    }
    .notif-panel__choice input:focus-visible,
    .notif-panel__day:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
    .notif-panel__choice--legend {
      font-weight: 600;
    }
    .notif-panel__choice--stack {
      flex-direction: column;
      align-items: flex-start;
    }
    .notif-panel__choice--stack > span {
      display: flex;
      align-items: center;
      gap: var(--vc-space-2);
    }
    .notif-panel__dnd {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(8rem, 1fr));
      gap: var(--vc-space-3);
    }
    .notif-panel__days {
      display: flex;
      flex-wrap: wrap;
      gap: var(--vc-space-1);
    }
    .notif-panel__day {
      min-width: 2rem;
      min-height: 2rem;
      padding: 0 var(--vc-space-1);
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink-muted);
      cursor: pointer;
      font: inherit;
      font-size: 0.8rem;
      transition:
        background var(--vc-dur-fast) var(--vc-ease-out),
        border-color var(--vc-dur-fast) var(--vc-ease-out),
        color var(--vc-dur-fast) var(--vc-ease-out);
    }
    .notif-panel__day.is-active {
      border-color: var(--vc-brand);
      background: var(--vc-brand-soft);
      color: var(--vc-brand-ink);
      font-weight: 600;
    }
    .notif-panel__hint {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.8rem;
    }
    .notif-panel__field {
      display: grid;
      gap: 0.35rem;
      width: 100%;
    }
    .notif-panel__field > span {
      font-size: 0.85rem;
      color: var(--vc-ink-muted);
      font-weight: 500;
    }
    .notif-panel__field select {
      width: 100%;
      min-height: 2.5rem;
      padding: 0.55rem 0.8rem;
      border-radius: var(--vc-radius-md);
      border: 1px solid var(--vc-border);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
      font: inherit;
      cursor: pointer;
    }
    .notif-panel__priority {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
    }
    .notif-panel__chips {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-wrap: wrap;
      gap: var(--vc-space-1);
    }
    .notif-panel__chips li {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      max-width: 100%;
      padding: 0.2rem 0.35rem 0.2rem 0.55rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: var(--vc-brand-soft);
      color: var(--vc-brand-ink);
      font-size: 0.8rem;
    }
    .notif-panel__chips li span {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .notif-panel__chip-remove {
      flex-shrink: 0;
      width: 1.4rem;
      height: 1.4rem;
      border: 0;
      border-radius: var(--vc-radius-sm);
      background: transparent;
      color: inherit;
      cursor: pointer;
      font: inherit;
      line-height: 1;
    }
    .notif-panel__chip-remove:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
    .notif-panel__suggestions {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      max-height: 10rem;
      overflow-y: auto;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
    }
    .notif-panel__suggest {
      width: 100%;
      padding: 0.45rem 0.7rem;
      border: 0;
      background: transparent;
      color: var(--vc-ink);
      cursor: pointer;
      font: inherit;
      font-size: 0.85rem;
      text-align: left;
    }
    .notif-panel__suggest:hover,
    .notif-panel__suggest:focus-visible {
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
    .notif-panel__suggest:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
    .notif-panel__actions {
      display: flex;
      align-items: center;
      gap: var(--vc-space-3);
    }
    .notif-panel__saved {
      color: var(--vc-success);
      font-size: 0.8rem;
    }
    @media (prefers-reduced-motion: reduce) {
      .notif-panel__day {
        transition: none;
      }
    }
  `,
})
export class NotificationPreferencesPanel {
  readonly ui = ui;
  readonly store = inject(NotificationPreferencesStore);
  private readonly auth = inject(AuthService);
  private readonly channels = inject(ChannelStore);
  private readonly locales = inject(LocaleService);

  readonly dayLabels = computed(() => weekdayLabels(this.locales.locale()));
  readonly timeZoneOptions = computed(() => {
    const locale = this.locales.locale();
    return listTimeZones(this.timeZone()).map((id) => ({
      id,
      label: timeZoneLabel(id, locale),
    }));
  });
  readonly levelOptions: Array<{ value: NotificationLevel; label: string }> = [
    { value: 'All', label: ui.notifAll },
    { value: 'MentionsAndDms', label: ui.notifMentions },
    { value: 'None', label: ui.notifNone },
  ];

  readonly level = signal<NotificationLevel>('MentionsAndDms');
  readonly hidePreview = signal(false);
  readonly dndEnabled = signal(false);
  readonly dndStart = signal('20:00');
  readonly dndEnd = signal('08:00');
  readonly dndDays = signal(0);
  readonly timeZone = signal(detectTimeZone());
  readonly digestEnabled = signal(false);
  readonly priorityContactUserIds = signal<string[]>([]);
  readonly contactQuery = signal('');
  private readonly timeZoneEl = viewChild<ElementRef<HTMLSelectElement>>('timeZoneEl');

  readonly saving = signal(false);
  readonly saved = signal(false);

  readonly selectedPriorityContacts = computed(() => {
    const members = this.channels.peerCandidates();
    return this.priorityContactUserIds()
      .map((id) => members.find((member) => idsEqual(member.userId, id)) ?? fallbackMember(id))
      .slice(0, PRIORITY_CONTACT_LIMIT);
  });

  readonly prioritySuggestions = computed(() => {
    const query = this.contactQuery().trim().toLowerCase();
    if (!query) {
      return [];
    }
    return this.channels
      .peerCandidates()
      .filter(
        (member) =>
          !this.isPriorityContact(member.userId) &&
          (member.displayName.toLowerCase().includes(query) || member.email.toLowerCase().includes(query)),
      )
      .slice(0, 8);
  });

  constructor() {
    effect(() => {
      const prefs = this.store.preferences();
      if (!prefs || this.saving()) {
        return;
      }
      this.applyPrefs(prefs);
    });
    afterRenderEffect(() => {
      const zone = this.timeZone();
      const el = this.timeZoneEl()?.nativeElement;
      if (el && el.value !== zone) {
        el.value = zone;
      }
    });
  }

  isDaySelected(bit: number): boolean {
    return (this.dndDays() & bit) !== 0;
  }

  toggleDay(bit: number): void {
    this.dndDays.update((current) => (current & bit ? current & ~bit : current | bit));
  }

  isPriorityContact(userId: string): boolean {
    return this.priorityContactUserIds().some((id) => idsEqual(id, userId));
  }

  togglePriorityContact(userId: string): void {
    this.priorityContactUserIds.update((current) =>
      current.some((id) => idsEqual(id, userId))
        ? current.filter((id) => !idsEqual(id, userId))
        : current.length >= PRIORITY_CONTACT_LIMIT
          ? current
          : [...current, userId],
    );
  }

  addPriorityContact(userId: string): void {
    this.togglePriorityContact(userId);
    this.contactQuery.set('');
  }

  onChecked(event: Event, target: WritableSignal<boolean>): void {
    target.set((event.target as HTMLInputElement).checked);
  }

  onTimeZoneChange(event: Event): void {
    this.timeZone.set((event.target as HTMLSelectElement).value);
  }

  async save(): Promise<void> {
    this.saving.set(true);
    this.saved.set(false);
    const ok = await this.store.save({
      level: this.level(),
      hidePreview: this.hidePreview(),
      dndEnabled: this.dndEnabled(),
      dndStart: toTimePayload(this.dndStart()),
      dndEnd: toTimePayload(this.dndEnd()),
      dndDays: this.dndDays(),
      timeZone: this.timeZone(),
      digestEnabled: this.digestEnabled(),
      priorityContactUserIds: this.priorityContactUserIds(),
    });
    this.saving.set(false);
    this.saved.set(ok);
  }

  private applyPrefs(prefs: NotificationPreferences): void {
    this.level.set(prefs.level);
    this.hidePreview.set(prefs.hidePreview);
    this.dndEnabled.set(prefs.dndEnabled);
    this.dndStart.set(toTimeInput(prefs.dndStart, '20:00'));
    this.dndEnd.set(toTimeInput(prefs.dndEnd, '08:00'));
    this.dndDays.set(prefs.dndDays);
    this.timeZone.set(prefs.timeZone || detectTimeZone());
    this.digestEnabled.set(prefs.digestEnabled);
    this.priorityContactUserIds.set(
      prefs.priorityContactUserIds.filter((id) => !idsEqual(id, this.auth.profile()?.id)).slice(0, PRIORITY_CONTACT_LIMIT),
    );
  }
}

function toTimeInput(value: string | null | undefined, fallback: string): string {
  if (!value) {
    return fallback;
  }
  return value.length >= 5 ? value.slice(0, 5) : fallback;
}

function toTimePayload(value: string): string {
  const hhmm = value.length >= 5 ? value.slice(0, 5) : '00:00';
  return `${hhmm}:00`;
}

function fallbackMember(userId: string): WorkspaceMember {
  return { userId, displayName: userId, email: '', role: '' };
}
