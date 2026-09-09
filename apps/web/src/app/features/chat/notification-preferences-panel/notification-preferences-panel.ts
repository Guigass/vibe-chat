import { Component, WritableSignal, computed, effect, inject, signal } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { LocaleService } from '../../../core/i18n/locale.service';
import { ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';
import { idsEqual } from '../../../core/services/message-sync';
import { NotificationPreferencesStore } from '../../../core/services/notification-preferences.store';
import { NotificationLevel, NotificationPreferences } from '../../../shared/models/chat.models';
import { Button, IconButton, Input } from '../../../shared/ui';

function detectTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
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
            <vc-input
              [label]="ui.notifTimeZone"
              type="text"
              placeholder="America/Sao_Paulo"
              [(value)]="timeZone"
            />

            @if (priorityCandidates().length > 0) {
              <p class="notif-panel__hint">{{ ui.notifPriority }}</p>
              <ul class="notif-panel__contacts" data-testid="notif-priority">
                @for (member of priorityCandidates(); track member.userId) {
                  <li>
                    <label class="notif-panel__choice">
                      <input
                        type="checkbox"
                        [checked]="isPriorityContact(member.userId)"
                        (change)="togglePriorityContact(member.userId)"
                      />
                      {{ member.displayName }}
                    </label>
                  </li>
                }
              </ul>
            }
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
    .notif-panel__contacts {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-1);
      max-height: 8rem;
      overflow-y: auto;
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

  readonly saving = signal(false);
  readonly saved = signal(false);

  readonly priorityCandidates = computed(() => this.channels.peerCandidates());

  constructor() {
    effect(() => {
      const prefs = this.store.preferences();
      if (!prefs || this.saving()) {
        return;
      }
      this.applyPrefs(prefs);
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
        : [...current, userId],
    );
  }

  onChecked(event: Event, target: WritableSignal<boolean>): void {
    target.set((event.target as HTMLInputElement).checked);
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
    this.priorityContactUserIds.set(prefs.priorityContactUserIds.filter((id) => !idsEqual(id, this.auth.profile()?.id)));
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
