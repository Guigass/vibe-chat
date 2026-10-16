import { Component, inject, signal } from '@angular/core';
import { InboxItem } from '../../../core/api/inbox-api.service';
import { ui } from '../../../core/i18n/strings';
import { InboxStore } from '../../../core/services/inbox.store';
import { IconButton } from '../../../shared/ui';

@Component({
  selector: 'vc-inbox-panel',
  standalone: true,
  imports: [IconButton],
  template: `
    <section class="inbox-panel" [attr.aria-label]="ui.inboxTitle">
      <header class="inbox-panel__header">
        <div>
          <h2>{{ ui.inboxTitle }}</h2>
          <p>{{ ui.inboxHint }}</p>
        </div>
        <vc-icon-button [label]="ui.closePanel" (click)="inbox.closePanel()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      <div class="inbox-panel__filters" role="group" [attr.aria-label]="ui.inboxFilters">
        <select [value]="inbox.typeFilter()" (change)="onType($event)" [attr.aria-label]="ui.inboxFilters">
          <option value="">{{ ui.inboxTypeAll }}</option>
          <option value="announcement">{{ ui.inboxKindAnnouncement }}</option>
          <option value="mention">{{ ui.inboxKindMention }}</option>
          <option value="dm">{{ ui.inboxKindDm }}</option>
          <option value="thread">{{ ui.inboxKindThread }}</option>
          <option value="topic">{{ ui.inboxKindTopic }}</option>
          <option value="saved">{{ ui.inboxKindSaved }}</option>
          <option value="commitment">{{ ui.inboxKindCommitment }}</option>
          <option value="task">{{ ui.inboxKindTask }}</option>
        </select>
        <select [value]="inbox.stateFilter()" (change)="onState($event)" [attr.aria-label]="ui.inboxStateOpen">
          <option value="open">{{ ui.inboxStateOpen }}</option>
          <option value="snoozed">{{ ui.inboxStateSnoozed }}</option>
          <option value="all">{{ ui.inboxStateAll }}</option>
        </select>
      </div>

      @if (inbox.loading() && !inbox.items().length) {
        <p class="inbox-panel__status">{{ ui.loading }}</p>
      } @else if (inbox.error()) {
        <p class="inbox-panel__status" role="alert">{{ inbox.error() }}</p>
      } @else if (!inbox.items().length) {
        <p class="inbox-panel__status">
          {{ inbox.stateFilter() === 'snoozed' ? ui.inboxEmptySnoozed : ui.inboxEmpty }}
        </p>
      } @else {
        <ul
          class="inbox-panel__list"
          role="listbox"
          [attr.aria-label]="ui.inboxTitle"
          (keydown)="onKey($event)"
        >
          @for (item of inbox.items(); track item.itemKey; let i = $index) {
            <li>
              <div class="inbox-panel__item" [class.is-active]="i === active()">
                <button
                  type="button"
                  class="inbox-panel__open"
                  role="option"
                  [attr.aria-selected]="i === active()"
                  [attr.tabindex]="i === active() ? 0 : -1"
                  (click)="inbox.openItem(item)"
                  (focus)="active.set(i)"
                >
                  <strong>{{ reason(item) }}</strong>
                  <span>{{ item.channelName }}</span>
                  @if (item.bodyPreview) {
                    <small>{{ item.bodyPreview }}</small>
                  }
                </button>
                <div class="inbox-panel__actions">
                  @if (canRead(item)) {
                    <button type="button" (click)="inbox.markRead(item)">{{ ui.inboxMarkRead }}</button>
                    <button type="button" (click)="inbox.markUnread(item)">{{ ui.inboxMarkUnread }}</button>
                  }
                  @if (item.snoozedUntil) {
                    <button type="button" (click)="inbox.unsnooze(item)">{{ ui.inboxUnsnooze }}</button>
                  } @else {
                    <button type="button" (click)="inbox.snooze(item)">{{ ui.inboxSnooze }}</button>
                  }
                </div>
              </div>
            </li>
          }
        </ul>
        @if (inbox.hasMore()) {
          <button type="button" class="inbox-panel__more" (click)="inbox.loadMore()">{{ ui.inboxLoadMore }}</button>
        }
      }
    </section>
  `,
  styles: `
    :host { display: flex; min-height: 0; height: 100%; }
    .inbox-panel {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
      min-height: 0;
      width: 100%;
      padding: var(--vc-space-3);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
    }
    .inbox-panel__header, .inbox-panel__filters, .inbox-panel__actions {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }
    .inbox-panel__header h2, .inbox-panel__header p { margin: 0; }
    .inbox-panel__header p, .inbox-panel__open small, .inbox-panel__open span {
      color: var(--vc-ink-muted);
    }
    .inbox-panel__filters select, .inbox-panel__actions button, .inbox-panel__more {
      font: inherit;
      color: var(--vc-ink);
      background: var(--vc-surface-elevated);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      min-height: 2.25rem;
      padding: 0 var(--vc-space-2);
    }
    .inbox-panel__list {
      list-style: none;
      margin: 0;
      padding: 0;
      overflow: auto;
      min-height: 0;
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
    }
    .inbox-panel__item {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
      border-bottom: 1px solid var(--vc-border);
      padding-bottom: var(--vc-space-2);
    }
    .inbox-panel__open {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 0.15rem;
      width: 100%;
      text-align: start;
      font: inherit;
      color: var(--vc-ink);
      background: transparent;
      border: 0;
      border-radius: var(--vc-radius-sm);
      padding: var(--vc-space-2);
      cursor: pointer;
    }
    .inbox-panel__open:focus-visible, .inbox-panel__actions button:focus-visible, .inbox-panel__filters select:focus-visible {
      outline: 2px solid var(--vc-brand);
      outline-offset: 2px;
    }
    .inbox-panel__status { margin: 0; color: var(--vc-ink-muted); }
    .inbox-panel__actions { flex-wrap: wrap; justify-content: flex-start; }
    .inbox-panel__more { align-self: flex-start; }
  `,
})
export class InboxPanel {
  readonly ui = ui;
  readonly inbox = inject(InboxStore);
  readonly active = signal(0);

  reason(item: InboxItem): string {
    switch (item.reason) {
      case 'pending_ack':
        return ui.inboxReasonPendingAck;
      case 'mention':
        return ui.inboxReasonMention;
      case 'unread_dm':
        return ui.inboxReasonUnreadDm;
      case 'followed_thread':
        return ui.inboxReasonFollowedThread;
      case 'followed_topic':
        return ui.inboxReasonFollowedTopic;
      case 'assigned':
        return ui.commitmentInboxReason;
      default:
        return ui.inboxReasonSaved;
    }
  }

  canRead(item: InboxItem): boolean {
    return item.kind === 'mention' || item.kind === 'dm' || item.kind === 'thread' || item.kind === 'topic';
  }

  onType(event: Event): void {
    this.inbox.setType((event.target as HTMLSelectElement).value);
  }

  onState(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (value === 'open' || value === 'snoozed' || value === 'all') {
      this.inbox.setState(value);
    }
  }

  onKey(event: KeyboardEvent): void {
    const items = this.inbox.items();
    if (!items.length) return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const delta = event.key === 'ArrowDown' ? 1 : -1;
      const next = (this.active() + delta + items.length) % items.length;
      this.active.set(next);
      const options = (event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="option"]');
      options.item(next)?.focus();
    }
  }
}
