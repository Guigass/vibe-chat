import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { CommitmentStore } from '../../../core/services/commitment.store';
import { IconButton } from '../../../shared/ui';
import { CommitmentKind } from '../../../core/api/commitment-api.service';

@Component({
  selector: 'vc-commitment-panel',
  standalone: true,
  imports: [IconButton, DatePipe],
  template: `
    <section class="commitment-panel" [attr.aria-label]="ui.commitmentPanel" data-testid="commitment-panel">
      <header>
        <h2>{{ ui.commitmentPanel }}</h2>
        <vc-icon-button [label]="ui.closePanel" (click)="commitments.closePanel()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>
      <p class="commitment-panel__hint">{{ ui.commitmentCommentsHint }}</p>
      <div class="commitment-panel__filters" role="group" [attr.aria-label]="ui.commitmentPanel">
        <select [value]="commitments.kindFilter()" (change)="onKind($event)" [attr.aria-label]="ui.commitmentFilterAll">
          <option value="all">{{ ui.commitmentFilterAll }}</option>
          <option value="decision">{{ ui.commitmentFilterDecisions }}</option>
          <option value="action_item">{{ ui.commitmentFilterActions }}</option>
        </select>
        <select [value]="commitments.stateFilter()" (change)="onState($event)" [attr.aria-label]="ui.commitmentFilterOpen">
          <option value="open">{{ ui.commitmentFilterOpen }}</option>
          <option value="closed">{{ ui.commitmentFilterClosed }}</option>
          <option value="all">{{ ui.commitmentFilterAll }}</option>
        </select>
        <select [value]="commitments.scopeFilter()" (change)="onScope($event)" [attr.aria-label]="ui.commitmentFilterWorkspace">
          <option value="workspace">{{ ui.commitmentFilterWorkspace }}</option>
          <option value="channel">{{ ui.commitmentFilterChannel }}</option>
          <option value="mine">{{ ui.commitmentFilterMine }}</option>
        </select>
      </div>
      @if (commitments.loading()) {
        <p>{{ ui.loading }}</p>
      } @else if (commitments.error()) {
        <p role="alert">{{ commitments.error() }}</p>
      } @else if (!commitments.items().length) {
        <p>{{ ui.commitmentEmpty }}</p>
      } @else {
        <ul>
          @for (item of commitments.items(); track item.id) {
            <li>
              <button type="button" (click)="commitments.select(item.id)">
                <strong>{{ item.title }}</strong>
                <span>{{ item.kind === 'decision' ? ui.commitmentFilterDecisions : ui.commitmentFilterActions }}</span>
                @if (item.ownerName) {
                  <span>{{ item.ownerName }}</span>
                }
                @if (item.dueAt) {
                  <span>{{ item.dueAt | date: 'short' }}</span>
                }
              </button>
              @if (commitments.selectedId() === item.id) {
                <div class="commitment-panel__detail">
                  @if (item.description) {
                    <p>{{ item.description }}</p>
                  }
                  @if (item.sourceRedacted) {
                    <p>{{ ui.commitmentSourceRemoved }}</p>
                  } @else {
                    <button type="button" (click)="commitments.openOrigin(item)">{{ ui.commitmentOpenOrigin }}</button>
                  }
                  @if (item.state === 'open') {
                    <button type="button" (click)="commitments.closeItem(item.id)">{{ ui.commitmentCloseItem }}</button>
                  } @else {
                    <button type="button" (click)="commitments.reopenItem(item.id)">{{ ui.commitmentReopenItem }}</button>
                  }
                  <h3>{{ ui.commitmentHistory }}</h3>
                  <ul>
                    @for (change of commitments.history(); track change.at + change.action) {
                      <li>{{ change.actorName || '—' }} · {{ actionLabel(change.action) }}</li>
                    }
                  </ul>
                </div>
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .commitment-panel {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
      height: 100%;
      min-height: 0;
      padding: var(--vc-space-4);
      overflow: auto;
    }
    .commitment-panel header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }
    .commitment-panel h2,
    .commitment-panel h3,
    .commitment-panel p { margin: 0; }
    .commitment-panel h2 { font-size: var(--vc-text-sm); }
    .commitment-panel__hint { color: var(--vc-ink-muted); font-size: var(--vc-text-sm); }
    .commitment-panel__filters { display: flex; flex-wrap: wrap; gap: var(--vc-space-2); }
    .commitment-panel select,
    .commitment-panel button {
      font: inherit;
      color: var(--vc-ink);
      background: transparent;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      padding: 0.3rem 0.5rem;
      cursor: pointer;
    }
    .commitment-panel ul { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--vc-space-2); }
    .commitment-panel li > button { width: 100%; text-align: left; display: grid; gap: 0.15rem; }
    .commitment-panel__detail { display: grid; gap: var(--vc-space-2); padding: var(--vc-space-2) 0; }
  `,
})
export class CommitmentPanel {
  readonly ui = ui;
  readonly commitments = inject(CommitmentStore);

  onKind(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.commitments.setKind(value === 'decision' || value === 'action_item' ? value as CommitmentKind : 'all');
  }

  onState(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.commitments.setState(value === 'open' || value === 'closed' ? value : 'all');
  }

  onScope(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.commitments.setScope(value === 'channel' || value === 'mine' ? value : 'workspace');
  }

  actionLabel(action: string): string {
    switch (action) {
      case 'created':
        return ui.commitmentActionCreated;
      case 'updated':
        return ui.commitmentActionUpdated;
      case 'closed':
        return ui.commitmentActionClosed;
      case 'reopened':
        return ui.commitmentActionReopened;
      default:
        return ui.commitmentHistory;
    }
  }
}
