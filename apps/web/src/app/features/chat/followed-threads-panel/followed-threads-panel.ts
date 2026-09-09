import { Component, inject } from '@angular/core';
import { FollowedThreadsStore } from '../../../core/services/followed-threads.store';
import { FollowedThreadItem } from '../../../shared/models/chat.models';
import { IconButton, Badge } from '../../../shared/ui';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-followed-threads-panel',
  standalone: true,
  imports: [IconButton, Badge],
  template: `
    <section class="followed-threads-panel" [attr.aria-label]="ui.channelFollowedThreads">
      <header class="followed-threads-panel__header">
        <h2>{{ ui.channelFollowedThreads }}</h2>
        <vc-icon-button [label]="ui.closePanel" (click)="followed.closePanel()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      @if (followed.loading()) {
        <p class="followed-threads-panel__status">{{ ui.loading }}</p>
      } @else if (followed.error()) {
        <p class="followed-threads-panel__status" role="alert">{{ followed.error() }}</p>
      } @else if (!followed.items().length) {
        <p class="followed-threads-panel__status">{{ ui.threadFollowEmpty }}</p>
        <p class="followed-threads-panel__hint">{{ ui.threadFollowEmptyHint }}</p>
      } @else {
        <ul class="followed-threads-panel__list">
          @for (item of followed.items(); track item.threadId) {
            <li class="followed-threads-panel__item">
              <div class="followed-threads-panel__meta">
                <strong>{{ formatOrigin(item) }}</strong>
                <span>{{ item.rootPreview }}</span>
              </div>
              @if (item.unreadCount > 0) {
                <vc-badge tone="accent">{{ item.unreadCount }}</vc-badge>
              }
              <div class="followed-threads-panel__actions">
                <button type="button" (click)="followed.jumpToThread(item)">{{ ui.threadFollowJump }}</button>
                <button type="button" (click)="followed.unfollow(item.threadId)">{{ ui.threadFollowUnfollow }}</button>
              </div>
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .followed-threads-panel {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
      height: 100%;
      min-height: 0;
      padding: var(--vc-space-4);
    }

    .followed-threads-panel__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }

    .followed-threads-panel__header h2 {
      margin: 0;
      font-size: var(--vc-text-sm);
      font-weight: 600;
    }

    .followed-threads-panel__status,
    .followed-threads-panel__hint {
      margin: 0;
      color: var(--vc-text-muted);
      font-size: var(--vc-text-sm);
    }

    .followed-threads-panel__hint {
      font-size: var(--vc-text-xs);
    }

    .followed-threads-panel__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
      flex: 1 1 auto;
      min-height: 0;
      overflow: auto;
    }

    .followed-threads-panel__item {
      border-bottom: 1px solid var(--vc-border-subtle);
      padding-bottom: var(--vc-space-2);
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
    }

    .followed-threads-panel__meta {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-1);
      font-size: var(--vc-text-sm);
    }

    .followed-threads-panel__meta span {
      color: var(--vc-text-muted);
    }

    .followed-threads-panel__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--vc-space-2);
    }

    .followed-threads-panel__actions button {
      font: inherit;
      font-size: var(--vc-text-xs);
      padding: var(--vc-space-1) var(--vc-space-2);
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border-subtle);
      background: var(--vc-surface-raised);
      color: var(--vc-text);
      cursor: pointer;
    }
  `,
})
export class FollowedThreadsPanel {
  readonly ui = ui;
  readonly followed = inject(FollowedThreadsStore);

  formatOrigin(item: FollowedThreadItem): string {
    const raw = (item.channelName ?? '').trim();
    const isDirect = item.channelType === 'Direct' || raw === 'DM';
    if (isDirect) {
      return raw && raw !== 'DM' ? (raw.startsWith('@') ? raw : `@${raw}`) : '@DM';
    }
    if (!raw) return '#';
    return raw.startsWith('#') || raw.startsWith('@') ? raw : `#${raw}`;
  }
}
