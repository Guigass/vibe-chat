import { Component, inject, signal } from '@angular/core';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import { ScheduleStore } from '../../../core/services/schedule.store';
import { ScheduleItem } from '../../../shared/models/chat.models';
import { Button } from '../../../shared/ui';

function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

function defaultLocalInput(): string {
  const date = new Date(Date.now() + 60 * 60 * 1000);
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

@Component({
  selector: 'vc-schedule-panel',
  standalone: true,
  imports: [Button],
  template: `
    <section class="schedule-panel" [attr.aria-label]="ui.composerScheduleList">
      <header class="schedule-panel__header">
        <h2>{{ ui.composerScheduleList }}</h2>
        <button type="button" class="ghost" (click)="schedule.closePanel()">{{ ui.scheduleCloseList }}</button>
      </header>

      @if (schedule.notice(); as notice) {
        <p class="schedule-panel__notice" role="status">
          {{ notice }}
          <button type="button" class="ghost" (click)="schedule.clearNotice()">{{ ui.cancel }}</button>
        </p>
      }

      @if (schedule.remindTarget(); as target) {
        <form class="schedule-panel__remind" (submit)="saveReminder($event)">
          <p>{{ ui.scheduleRemindTitle }}</p>
          <p class="schedule-panel__preview">{{ target.preview }}</p>
          <label>
            {{ ui.composerScheduleAt }}
            <input type="datetime-local" [value]="remindAt()" (input)="remindAt.set(localValue($event))" required />
          </label>
          <label>
            {{ ui.composerTimeZone }}
            <input [value]="zone()" (input)="zone.set(textValue($event))" [attr.aria-label]="ui.composerTimeZone" />
          </label>
          <label>
            {{ ui.scheduleRemindNote }}
            <input
              [value]="note()"
              (input)="note.set(textValue($event))"
              [placeholder]="ui.scheduleRemindNotePh"
              [attr.aria-label]="ui.scheduleRemindNote"
            />
          </label>
          <vc-button type="submit">{{ ui.scheduleRemindSave }}</vc-button>
        </form>
      }

      @if (schedule.loading()) {
        <p>{{ ui.loading }}</p>
      } @else if (schedule.error(); as error) {
        <p role="alert">{{ error }}</p>
      } @else if (!schedule.items().length) {
        <p>{{ ui.composerScheduleEmpty }}</p>
      } @else {
        <ul class="schedule-panel__list">
          @for (item of schedule.items(); track item.id) {
            <li class="schedule-panel__item">
              <div>
                <strong>{{ item.kind === 'reminder' ? ui.scheduleKindReminder : ui.scheduleKindMessage }}</strong>
                <span>{{ statusLabel(item.status) }}</span>
                <p>{{ whenLabel(item) }}</p>
                @if (item.body) {
                  <p>{{ item.body }}</p>
                }
                @if (item.note) {
                  <p>{{ item.note }}</p>
                }
                @if (item.channelName) {
                  <small>{{ item.channelName }}</small>
                }
                @if (item.status === 'Failed' || item.status === 'MembershipRevoked') {
                  <p role="status">{{ item.status === 'MembershipRevoked' ? ui.scheduleDueRevoked : ui.scheduleDueFailed }}</p>
                }
              </div>
              @if (item.status === 'Pending') {
                <div class="schedule-panel__actions">
                  @if (editingId() === item.id) {
                    <label>
                      {{ ui.composerScheduleAt }}
                      <input type="datetime-local" [value]="editAt()" (input)="editAt.set(localValue($event))" />
                    </label>
                    @if (item.kind === 'scheduled_message') {
                      <label>
                        {{ ui.composerScheduleEdit }}
                        <input [value]="editBody()" (input)="editBody.set(textValue($event))" />
                      </label>
                    }
                    <button type="button" (click)="saveEdit(item)">{{ ui.composerScheduleEdit }}</button>
                  } @else {
                    <button type="button" (click)="startEdit(item)">{{ ui.menuEdit }}</button>
                  }
                  <button type="button" (click)="cancel(item)">{{ ui.composerScheduleCancel }}</button>
                </div>
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .schedule-panel {
      display: grid;
      gap: var(--vc-space-2);
      margin-bottom: var(--vc-space-2);
      padding: var(--vc-space-3);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
    }
    .schedule-panel__header,
    .schedule-panel__actions,
    .schedule-panel__remind {
      display: flex;
      flex-wrap: wrap;
      gap: var(--vc-space-2);
      align-items: center;
    }
    .schedule-panel__header h2 {
      margin: 0;
      font-size: 0.95rem;
    }
    .schedule-panel__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: var(--vc-space-2);
      max-height: 16rem;
      overflow: auto;
    }
    .schedule-panel__item {
      display: grid;
      gap: 0.35rem;
      padding-bottom: var(--vc-space-2);
      border-bottom: 1px solid var(--vc-border);
    }
    .schedule-panel__preview,
    .schedule-panel__notice {
      margin: 0;
      color: var(--vc-ink-muted);
    }
    label {
      display: grid;
      gap: 0.2rem;
      font-size: 0.8rem;
      color: var(--vc-ink-muted);
    }
    input {
      min-height: 2.25rem;
      padding: 0.4rem 0.6rem;
      border-radius: var(--vc-radius-md);
      border: 1px solid var(--vc-border);
      background: var(--vc-surface);
      color: var(--vc-ink);
    }
  `,
})
export class SchedulePanel {
  readonly schedule = inject(ScheduleStore);
  readonly ui = ui;
  readonly remindAt = signal(defaultLocalInput());
  readonly zone = signal(browserTimeZone());
  readonly note = signal('');
  readonly editingId = signal<string | null>(null);
  readonly editAt = signal('');
  readonly editBody = signal('');

  statusLabel(status: string): string {
    switch (status) {
      case 'Pending':
        return ui.scheduleStatusPending;
      case 'Sent':
        return ui.scheduleStatusSent;
      case 'Delivered':
        return ui.scheduleStatusDelivered;
      case 'Cancelled':
        return ui.scheduleStatusCancelled;
      case 'Failed':
        return ui.scheduleStatusFailed;
      case 'MembershipRevoked':
        return ui.scheduleStatusRevoked;
      case 'Claimed':
        return ui.scheduleStatusPending;
      default:
        return status;
    }
  }

  whenLabel(item: ScheduleItem): string {
    let when = item.dueAtUtc;
    try {
      when = new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short',
        timeZone: item.timeZone,
      }).format(new Date(item.dueAtUtc));
    } catch {
      when = item.dueAtUtc;
    }
    return fillTemplate(ui.composerScheduleDelay, { when, zone: item.timeZone });
  }

  localValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  textValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  startEdit(item: ScheduleItem): void {
    this.editingId.set(item.id);
    this.editBody.set(item.body ?? '');
    this.editAt.set(toLocalInput(item.dueAtUtc, item.timeZone));
    this.zone.set(item.timeZone);
  }

  async saveEdit(item: ScheduleItem): Promise<void> {
    const ok = await this.schedule.updateItem(item, {
      body: item.kind === 'scheduled_message' ? this.editBody() : undefined,
      sendAtLocal: this.editAt() ? `${this.editAt()}:00` : undefined,
      timeZone: this.zone(),
    });
    if (ok) {
      this.editingId.set(null);
    }
  }

  async cancel(item: ScheduleItem): Promise<void> {
    if (!globalThis.confirm(ui.composerCancelScheduleConfirm)) return;
    await this.schedule.cancelItem(item);
  }

  async saveReminder(event: Event): Promise<void> {
    event.preventDefault();
    const target = this.schedule.remindTarget();
    if (!target || !this.remindAt()) return;
    const local = this.remindAt().length === 16 ? `${this.remindAt()}:00` : this.remindAt();
    await this.schedule.createReminder({
      targetKind: 'Message',
      messageId: target.messageId,
      remindAtLocal: local,
      timeZone: this.zone(),
      note: this.note(),
      idempotencyKey: crypto.randomUUID(),
    });
  }
}

function toLocalInput(dueAtUtc: string, timeZone: string): string {
  try {
    const parts = new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    }).formatToParts(new Date(dueAtUtc));
    const pick = (type: string) => parts.find((part) => part.type === type)?.value ?? '';
    return `${pick('year')}-${pick('month')}-${pick('day')}T${pick('hour')}:${pick('minute')}`;
  } catch {
    return '';
  }
}
