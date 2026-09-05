import { Component, WritableSignal, computed, effect, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { NotificationPreferencesStore } from '../../../core/services/notification-preferences.store';
import { NotificationLevel, WorkspaceMember } from '../../../shared/models/chat.models';
import { IconButton } from '../../../shared/ui';
import { ui } from '../../../core/i18n/strings';

const DAY_LABELS: Array<{ bit: number; label: string }> = [
  { bit: 1 << 0, label: 'D' },
  { bit: 1 << 1, label: 'S' },
  { bit: 1 << 2, label: 'T' },
  { bit: 1 << 3, label: 'Q' },
  { bit: 1 << 4, label: 'Q' },
  { bit: 1 << 5, label: 'S' },
  { bit: 1 << 6, label: 'S' },
];

function detectTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

@Component({
  selector: 'vc-notification-preferences-panel',
  standalone: true,
  imports: [IconButton],
  template: `
    <section class="notif-panel" [attr.aria-label]="ui.notificationPrefs">
      <header class="notif-panel__header">
        <h2>{{ ui.notifTitle }}</h2>
        <vc-icon-button [label]="ui.closePanel" (click)="store.closePanel()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      @if (store.loading()) {
        <p class="notif-panel__status">{{ ui.notifLoading }}</p>
      } @else {
        @if (store.error()) {
          <p class="notif-panel__status" role="alert">{{ store.error() }}</p>
        }

        <fieldset class="notif-panel__group">
          <legend>{{ ui.notifWhen }}</legend>
          @for (option of levelOptions; track option.value) {
            <label class="notif-panel__radio">
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

        <label class="notif-panel__checkbox">
          <input type="checkbox" [checked]="hidePreview()" (change)="hidePreview.set(!hidePreview())" />
          {{ ui.notifHidePreview }}
        </label>

        <fieldset class="notif-panel__group">
          <legend>
            <label class="notif-panel__checkbox notif-panel__checkbox--legend">
              <input type="checkbox" [checked]="dndEnabled()" (change)="dndEnabled.set(!dndEnabled())" />
              {{ ui.notifDnd }}
            </label>
          </legend>
          @if (dndEnabled()) {
            <div class="notif-panel__dnd">
              <label>
                {{ ui.notifFrom }}
                <input type="time" [value]="dndStart()" (change)="onTimeInput($event, dndStart)" />
              </label>
              <label>
                {{ ui.notifTo }}
                <input type="time" [value]="dndEnd()" (change)="onTimeInput($event, dndEnd)" />
              </label>
            </div>
            <div class="notif-panel__days">
              @for (day of dayLabels; track day.bit) {
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
            <label class="notif-panel__timezone">
              {{ ui.notifTimeZone }}
              <input type="text" [value]="timeZone()" (change)="onTimeZoneInput($event)" placeholder="America/Sao_Paulo" />
            </label>

            @if (priorityCandidates().length > 0) {
              <p class="notif-panel__hint">{{ ui.notifPriority }}</p>
              <ul class="notif-panel__contacts">
                @for (member of priorityCandidates(); track member.userId) {
                  <li>
                    <label class="notif-panel__checkbox">
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

        <label class="notif-panel__checkbox">
          <input type="checkbox" [checked]="digestEnabled()" (change)="digestEnabled.set(!digestEnabled())" />
          {{ ui.notifDigest }}
          <small class="notif-panel__hint">{{ ui.notifDigestSoon }}</small>
        </label>

        <div class="notif-panel__actions">
          <button type="button" class="notif-panel__save" [disabled]="saving()" (click)="save()">
            {{ saving() ? ui.notifSaving : ui.notifSave }}
          </button>
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
    }
    .notif-panel__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }
    .notif-panel__header h2 {
      margin: 0;
      font-size: var(--vc-text-sm);
      font-weight: 600;
    }
    .notif-panel__status {
      margin: 0;
      color: var(--vc-text-muted);
      font-size: var(--vc-text-sm);
    }
    .notif-panel__group {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
      border: 1px solid var(--vc-border-subtle);
      border-radius: var(--vc-radius-md);
      padding: var(--vc-space-3);
      margin: 0;
    }
    .notif-panel__group legend {
      padding: 0 var(--vc-space-1);
      font-size: var(--vc-text-xs);
      font-weight: 600;
      color: var(--vc-text-muted);
    }
    .notif-panel__radio,
    .notif-panel__checkbox {
      display: flex;
      align-items: center;
      gap: var(--vc-space-2);
      font-size: var(--vc-text-sm);
    }
    .notif-panel__checkbox--legend {
      font-weight: 600;
      color: var(--vc-text);
    }
    .notif-panel__dnd {
      display: flex;
      flex-wrap: wrap;
      gap: var(--vc-space-3);
      font-size: var(--vc-text-sm);
    }
    .notif-panel__dnd label {
      display: flex;
      align-items: center;
      gap: var(--vc-space-1);
    }
    .notif-panel__days {
      display: flex;
      gap: var(--vc-space-1);
    }
    .notif-panel__day {
      width: 1.8rem;
      height: 1.8rem;
      border-radius: 999px;
      border: 1px solid var(--vc-border-subtle);
      background: transparent;
      color: var(--vc-text-muted);
      cursor: pointer;
      font-size: var(--vc-text-xs);
    }
    .notif-panel__day.is-active {
      border-color: var(--vc-border);
      background: var(--vc-surface-raised);
      color: var(--vc-text);
      font-weight: 600;
    }
    .notif-panel__timezone {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-1);
      font-size: var(--vc-text-sm);
    }
    .notif-panel__timezone input {
      font: inherit;
      padding: var(--vc-space-1) var(--vc-space-2);
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border-subtle);
      background: var(--vc-surface);
      color: var(--vc-text);
    }
    .notif-panel__hint {
      margin: 0;
      color: var(--vc-text-muted);
      font-size: var(--vc-text-xs);
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
    .notif-panel__save {
      font: inherit;
      font-size: var(--vc-text-sm);
      font-weight: 600;
      padding: var(--vc-space-2) var(--vc-space-3);
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border);
      background: var(--vc-brand);
      color: var(--vc-on-brand, #fff);
      cursor: pointer;
    }
    .notif-panel__save:disabled {
      opacity: 0.6;
      cursor: default;
    }
    .notif-panel__saved {
      color: var(--vc-text-muted);
      font-size: var(--vc-text-xs);
    }
  `,
})
export class NotificationPreferencesPanel {
  readonly ui = ui;
  readonly store = inject(NotificationPreferencesStore);
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);

  readonly dayLabels = DAY_LABELS;
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

  private readonly membersSignal = signal<WorkspaceMember[]>([]);
  readonly priorityCandidates = computed(() => this.membersSignal());

  private loadedFromServer = false;

  constructor() {
    effect(() => {
      const prefs = this.store.preferences();
      if (!prefs || this.loadedFromServer) {
        return;
      }
      this.loadedFromServer = true;
      this.level.set(prefs.level);
      this.hidePreview.set(prefs.hidePreview);
      this.dndEnabled.set(prefs.dndEnabled);
      this.dndStart.set((prefs.dndStart ?? '20:00:00').slice(0, 5));
      this.dndEnd.set((prefs.dndEnd ?? '08:00:00').slice(0, 5));
      this.dndDays.set(prefs.dndDays);
      this.timeZone.set(prefs.timeZone || detectTimeZone());
      this.digestEnabled.set(prefs.digestEnabled);
      this.priorityContactUserIds.set(prefs.priorityContactUserIds);
    });

    const workspaceId = this.channels.activeWorkspace()?.id;
    if (workspaceId) {
      void this.api.getMembers(workspaceId).then((members) => this.membersSignal.set(members));
    }
  }

  isDaySelected(bit: number): boolean {
    return (this.dndDays() & bit) !== 0;
  }

  toggleDay(bit: number): void {
    this.dndDays.update((current) => (current & bit ? current & ~bit : current | bit));
  }

  isPriorityContact(userId: string): boolean {
    return this.priorityContactUserIds().includes(userId);
  }

  togglePriorityContact(userId: string): void {
    this.priorityContactUserIds.update((current) =>
      current.includes(userId) ? current.filter((id) => id !== userId) : [...current, userId],
    );
  }

  onTimeInput(event: Event, target: WritableSignal<string>): void {
    target.set((event.target as HTMLInputElement).value || '00:00');
  }

  onTimeZoneInput(event: Event): void {
    this.timeZone.set((event.target as HTMLInputElement).value.trim());
  }

  async save(): Promise<void> {
    this.saving.set(true);
    this.saved.set(false);
    const ok = await this.store.save({
      level: this.level(),
      hidePreview: this.hidePreview(),
      dndEnabled: this.dndEnabled(),
      dndStart: `${this.dndStart()}:00`,
      dndEnd: `${this.dndEnd()}:00`,
      dndDays: this.dndDays(),
      timeZone: this.timeZone(),
      digestEnabled: this.digestEnabled(),
      priorityContactUserIds: this.priorityContactUserIds(),
    });
    this.saving.set(false);
    this.saved.set(ok);
  }
}
