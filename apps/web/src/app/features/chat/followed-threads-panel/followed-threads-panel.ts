import { Component, inject } from '@angular/core';
import { FollowedThreadsStore } from '../../../core/services/followed-threads.store';
import { ui } from '../../../core/i18n/strings';
import { IconButton } from '../../../shared/ui';

@Component({
  selector: 'vc-followed-threads-panel',
  standalone: true,
  imports: [IconButton],
  template: `
    <section class="followed-panel" [attr.aria-label]="ui.followedThreads">
      <header class="followed-panel__header">
        <h2>{{ ui.followedThreads }}</h2>
        <vc-icon-button [label]="ui.closePanel" (click)="followed.closePanel()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      @if (followed.loading()) {
        <p class="followed-panel__status">{{ ui.notifLoading }}</p>
      } @else if (!followed.items().length) {
        <p class="followed-panel__status">{{ ui.noFollowedThreads }}</p>
      } @else {
        <ul class="followed-panel__list">
          @for (item of followed.items(); track item.threadId) {
            <li>
              <button type="button" class="followed-panel__row" (click)="followed.jumpToFollowed(item)">
                <strong>{{ channelLabel(item.channelName) }}</strong>
                <span>{{ item.parentPreview }}</span>
                @if (item.participantNames.length) {
                  <small>{{ item.participantNames.join(', ') }}</small>
                }
              </button>
              @if (item.unreadCount > 0) {
                <span class="followed-panel__unread">{{ item.unreadCount }}</span>
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .followed-panel {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
      height: 100%;
      min-height: 0;
      padding: var(--vc-space-4);
    }
    .followed-panel__header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 0.75rem;
    }
    .followed-panel__header h2 {
      margin: 0;
      font-family: var(--vc-font-display);
      font-size: 1.05rem;
    }
    .followed-panel__status {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.9rem;
    }
    .followed-panel__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.5rem;
      overflow: auto;
    }
    .followed-panel__list li {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.5rem;
      align-items: start;
    }
    .followed-panel__row {
      display: grid;
      gap: 0.15rem;
      min-width: 0;
      padding: 0.45rem 0.55rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: transparent;
      color: inherit;
      text-align: left;
      cursor: pointer;
      font: inherit;
    }
    .followed-panel__row:hover {
      background: color-mix(in srgb, var(--vc-brand) 8%, transparent);
    }
    .followed-panel__row strong {
      font-size: 0.85rem;
    }
    .followed-panel__row span,
    .followed-panel__row small {
      color: var(--vc-ink-muted);
      font-size: 0.78rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .followed-panel__unread {
      min-width: 1.4rem;
      padding: 0.1rem 0.4rem;
      border-radius: 999px;
      background: var(--vc-brand);
      color: var(--vc-ink-inverse, #fff);
      font-size: 0.72rem;
      text-align: center;
    }
  `,
})
export class FollowedThreadsPanel {
  readonly followed = inject(FollowedThreadsStore);
  readonly ui = ui;

  channelLabel(name: string): string {
    const trimmed = name.trim();
    if (!trimmed || trimmed.startsWith('dm:')) return '#canal';
    return trimmed.startsWith('#') ? trimmed : `#${trimmed}`;
  }
}
