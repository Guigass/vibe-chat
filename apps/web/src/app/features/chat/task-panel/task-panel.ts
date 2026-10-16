import { Component, inject } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { TaskStore } from '../../../core/services/task.store';
import { TaskItem, TaskListState, TaskView } from '../../../core/api/task-api.service';
import { IconButton } from '../../../shared/ui/icon-button/icon-button';

@Component({
  selector: 'vc-task-panel',
  standalone: true,
  imports: [IconButton],
  template: `
    <section class="task-panel" [attr.aria-label]="ui.taskPanel" data-testid="task-panel">
      <header>
        <h2>{{ ui.taskPanel }}</h2>
        <vc-icon-button [label]="ui.closePanel" (click)="tasks.closePanel()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>
      <p class="task-panel__hint">{{ ui.taskCommentsHint }}</p>
      <div class="task-panel__filters" role="group" [attr.aria-label]="ui.taskPanel">
        <select [value]="tasks.viewFilter()" (change)="onView($event)" [attr.aria-label]="ui.taskFilterWorkspace">
          <option value="workspace">{{ ui.taskFilterWorkspace }}</option>
          <option value="channel">{{ ui.taskFilterChannel }}</option>
          <option value="mine">{{ ui.taskFilterMine }}</option>
        </select>
        <select [value]="tasks.stateFilter()" (change)="onState($event)" [attr.aria-label]="ui.taskFilterActive">
          <option value="active">{{ ui.taskFilterActive }}</option>
          <option value="completed">{{ ui.taskFilterCompleted }}</option>
          <option value="all">{{ ui.taskFilterAll }}</option>
        </select>
        <button type="button" data-testid="task-new" (click)="tasks.beginGlobal()">{{ ui.taskNew }}</button>
      </div>
      @if (tasks.loading()) {
        <p class="task-panel__hint">{{ ui.loading }}</p>
      } @else if (tasks.error()) {
        <p role="alert">{{ tasks.error() }}</p>
      } @else if (!tasks.items().length) {
        <p>{{ ui.taskEmpty }}</p>
      } @else {
        <ul>
          @for (item of tasks.items(); track item.id) {
            <li>
              <p>
                <strong>{{ item.title }}</strong>
                <span>{{ priorityLabel(item) }}</span>
                <span>{{ stateLabel(item) }}</span>
                @if (item.overdue) {
                  <span data-testid="task-overdue">{{ ui.taskOverdue }}</span>
                }
              </p>
              @if (item.checklist.length) {
                <ul>
                  @for (row of item.checklist; track row.position) {
                    <li>
                      <label>
                        <input
                          type="checkbox"
                          [checked]="row.done"
                          [attr.aria-label]="row.text"
                          (change)="tasks.toggleChecklist(item, row.position)"
                        />
                        {{ row.text }}
                      </label>
                    </li>
                  }
                </ul>
              }
              <div class="task-panel__actions">
                @if (!item.sourceRedacted) {
                  <button type="button" (click)="tasks.openOrigin(item)">{{ ui.taskOpenOrigin }}</button>
                } @else {
                  <p>{{ ui.taskSourceRemoved }}</p>
                }
                @if (item.state === 'completed') {
                  <button type="button" (click)="tasks.reopen(item)">{{ ui.taskReopen }}</button>
                } @else {
                  <button type="button" data-testid="task-complete" (click)="tasks.complete(item)">{{ ui.taskComplete }}</button>
                }
              </div>
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .task-panel {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
      min-height: 0;
      padding: var(--vc-space-3);
    }
    .task-panel header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }
    .task-panel h2,
    .task-panel p { margin: 0; }
    .task-panel h2 { font-size: var(--vc-text-sm); }
    .task-panel__hint { color: var(--vc-ink-muted); font-size: var(--vc-text-sm); }
    .task-panel__filters,
    .task-panel__actions { display: flex; flex-wrap: wrap; gap: var(--vc-space-2); }
    .task-panel select,
    .task-panel button {
      font: inherit;
      color: var(--vc-ink);
      background: var(--vc-surface);
      border: 1px solid var(--vc-line);
      border-radius: var(--vc-radius-sm);
      padding: 0.25rem 0.5rem;
    }
    .task-panel ul { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--vc-space-2); }
    .task-panel li { display: grid; gap: 0.25rem; }
    .task-panel label { display: flex; gap: 0.4rem; align-items: flex-start; font-size: var(--vc-text-sm); }
  `,
})
export class TaskPanel {
  readonly ui = ui;
  readonly tasks = inject(TaskStore);

  onView(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.tasks.setView(value === 'channel' || value === 'mine' ? value as TaskView : 'workspace');
  }

  onState(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const state: TaskListState = value === 'completed' || value === 'all' ? value : 'active';
    this.tasks.setState(state);
  }

  priorityLabel(item: TaskItem): string {
    switch (item.priority) {
      case 'low':
        return ui.taskPriorityLow;
      case 'high':
        return ui.taskPriorityHigh;
      case 'urgent':
        return ui.taskPriorityUrgent;
      default:
        return ui.taskPriorityNormal;
    }
  }

  stateLabel(item: TaskItem): string {
    switch (item.state) {
      case 'in_progress':
        return ui.taskStateInProgress;
      case 'completed':
        return ui.taskStateCompleted;
      default:
        return ui.taskStateOpen;
    }
  }
}
