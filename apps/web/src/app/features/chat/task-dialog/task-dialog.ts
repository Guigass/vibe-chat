import { Component, effect, inject, signal } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { TaskStore } from '../../../core/services/task.store';
import { ChannelStore } from '../../../core/services/channel.store';
import { MessageStore } from '../../../core/services/message.store';
import { TaskPriority, TaskVisibility } from '../../../core/api/task-api.service';

@Component({
  selector: 'vc-task-dialog',
  standalone: true,
  template: `
    @if (tasks.draft(); as draft) {
      <div class="task-dialog__backdrop" (click)="tasks.cancelDraft()"></div>
      <form
        class="task-dialog"
        role="dialog"
        aria-modal="true"
        [attr.aria-labelledby]="titleId"
        data-testid="task-dialog"
        (submit)="onSubmit($event)"
      >
        <header>
          <h2 [id]="titleId">{{ ui.taskDialogTitle }}</h2>
          <button type="button" (click)="tasks.cancelDraft()">{{ ui.closePanel }}</button>
        </header>
        @if (!draft.messageId) {
          <label>
            {{ ui.taskOriginLabel }}
            <select
              data-testid="task-origin"
              [attr.aria-label]="ui.taskOriginLabel"
              (change)="messageId.set(inputValue($event))"
            >
              <option value="">{{ ui.taskOriginEmpty }}</option>
              @for (message of origins(); track message.id) {
                <option [value]="message.id">{{ message.label }}</option>
              }
            </select>
          </label>
        }
        <label>
          {{ ui.taskTitleLabel }}
          <input
            data-testid="task-title"
            [attr.aria-label]="ui.taskTitleLabel"
            [value]="title()"
            (input)="title.set(inputValue($event))"
            required
          />
        </label>
        <label>
          {{ ui.taskDescriptionLabel }}
          <textarea
            rows="2"
            [attr.aria-label]="ui.taskDescriptionLabel"
            [value]="description()"
            (input)="description.set(inputValue($event))"
          ></textarea>
        </label>
        <label>
          {{ ui.taskAssigneesLabel }}
          <select
            multiple
            [attr.aria-label]="ui.taskAssigneesLabel"
            (change)="onAssignees($event)"
          >
            @for (member of channels.members(); track member.userId) {
              <option [value]="member.userId">{{ member.displayName }}</option>
            }
          </select>
        </label>
        <label>
          {{ ui.taskPriorityLabel }}
          <select [attr.aria-label]="ui.taskPriorityLabel" (change)="onPriority($event)">
            <option value="low">{{ ui.taskPriorityLow }}</option>
            <option value="normal" selected>{{ ui.taskPriorityNormal }}</option>
            <option value="high">{{ ui.taskPriorityHigh }}</option>
            <option value="urgent">{{ ui.taskPriorityUrgent }}</option>
          </select>
        </label>
        <label>
          {{ ui.taskDueLabel }}
          <input
            type="datetime-local"
            [attr.aria-label]="ui.taskDueLabel"
            [value]="dueAt()"
            (input)="dueAt.set(inputValue($event))"
          />
        </label>
        <label>
          {{ ui.taskChecklistLabel }}
          <textarea
            rows="3"
            [attr.aria-label]="ui.taskChecklistLabel"
            [placeholder]="ui.taskChecklistHint"
            [value]="checklist()"
            (input)="checklist.set(inputValue($event))"
          ></textarea>
        </label>
        <label>
          {{ ui.taskVisibilityLabel }}
          <select [attr.aria-label]="ui.taskVisibilityLabel" (change)="onVisibility($event)">
            <option value="channel">{{ ui.taskVisibilityChannel }}</option>
            <option value="restricted">{{ ui.taskVisibilityRestricted }}</option>
          </select>
        </label>
        @if (tasks.saveError()) {
          <p role="alert">{{ tasks.saveError() }}</p>
        }
        <div class="task-dialog__actions">
          <button type="button" (click)="tasks.cancelDraft()">{{ ui.closePanel }}</button>
          <button type="submit" [disabled]="tasks.saving()">{{ ui.taskCreate }}</button>
        </div>
      </form>
    }
  `,
  styles: `
    .task-dialog__backdrop {
      position: fixed;
      inset: 0;
      background: color-mix(in srgb, var(--vc-ink) 35%, transparent);
      z-index: 30;
    }
    .task-dialog {
      position: fixed;
      z-index: 31;
      top: 12vh;
      left: 50%;
      transform: translateX(-50%);
      width: min(28rem, calc(100vw - 2rem));
      max-height: min(80vh, 40rem);
      overflow: auto;
      display: grid;
      gap: var(--vc-space-3);
      padding: var(--vc-space-4);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
      border: 1px solid var(--vc-line);
      border-radius: var(--vc-radius-md);
    }
    .task-dialog header,
    .task-dialog__actions {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }
    .task-dialog h2 { margin: 0; font-size: var(--vc-text-sm); }
    .task-dialog label { display: grid; gap: 0.25rem; font-size: var(--vc-text-sm); }
    .task-dialog input,
    .task-dialog textarea,
    .task-dialog select {
      font: inherit;
      color: var(--vc-ink);
      background: var(--vc-surface);
      border: 1px solid var(--vc-line);
      border-radius: var(--vc-radius-sm);
      padding: 0.35rem 0.5rem;
    }
    .task-dialog button {
      font: inherit;
      color: var(--vc-ink);
      background: transparent;
      border: 1px solid var(--vc-line);
      border-radius: var(--vc-radius-sm);
      padding: 0.35rem 0.7rem;
    }
    .task-dialog button[type='submit'] {
      background: var(--vc-brand);
      color: var(--vc-on-brand, #fff);
      border-color: transparent;
    }
  `,
})
export class TaskDialog {
  readonly ui = ui;
  readonly titleId = 'task-dialog-title';
  readonly tasks = inject(TaskStore);
  readonly channels = inject(ChannelStore);
  private readonly messages = inject(MessageStore);
  readonly title = signal('');
  readonly description = signal('');
  readonly priority = signal<TaskPriority>('normal');
  readonly assigneeUserIds = signal<string[]>([]);
  readonly dueAt = signal('');
  readonly checklist = signal('');
  readonly visibility = signal<TaskVisibility>('channel');
  readonly messageId = signal('');

  constructor() {
    effect(() => {
      if (!this.tasks.draft()) return;
      this.title.set('');
      this.description.set('');
      this.priority.set('normal');
      this.assigneeUserIds.set([]);
      this.dueAt.set('');
      this.checklist.set('');
      this.visibility.set('channel');
      this.messageId.set(this.tasks.draft()?.messageId ?? '');
    });
  }

  origins(): { id: string; label: string }[] {
    const channelId = this.tasks.draft()?.channelId;
    if (!channelId) return [];
    return this.messages.messages()
      .filter((message) => message.channelId === channelId && !message.deletedAt && !message.body.startsWith('<system:'))
      .slice(-8)
      .reverse()
      .map((message) => ({
        id: message.id,
        label: message.body.replace(/\s+/g, ' ').slice(0, 80),
      }));
  }

  inputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  onAssignees(event: Event): void {
    const select = event.target as HTMLSelectElement;
    this.assigneeUserIds.set(Array.from(select.selectedOptions).map((option) => option.value));
  }

  onPriority(event: Event): void {
    const value = this.inputValue(event);
    this.priority.set(value === 'low' || value === 'high' || value === 'urgent' ? value : 'normal');
  }

  onVisibility(event: Event): void {
    this.visibility.set(this.inputValue(event) === 'restricted' ? 'restricted' : 'channel');
  }

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    const draft = this.tasks.draft();
    const messageId = draft?.messageId || this.messageId();
    if (!this.title().trim() || !messageId) return;
    await this.tasks.submit({
      messageId,
      title: this.title(),
      description: this.description(),
      priority: this.priority(),
      assigneeUserIds: this.assigneeUserIds(),
      dueAt: this.dueAt(),
      visibility: this.visibility(),
      checklistText: this.checklist(),
    });
  }
}
