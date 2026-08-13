import { Component, inject, output } from '@angular/core';
import { PushNotificationService } from '../../../core/services/push-notification.service';
import { Button } from '../button/button';

@Component({
  selector: 'vc-in-app-notice',
  standalone: true,
  imports: [Button],
  template: `
    @if (push.notice(); as notice) {
      <div class="vc-notice" role="status" data-testid="in-app-notice">
        <button type="button" class="vc-notice__body" (click)="open.emit()">
          <strong>{{ notice.title }}</strong>
          <span>{{ notice.body }}</span>
        </button>
        <vc-button type="button" variant="ghost" (click)="push.dismissNotice()">Fechar</vc-button>
      </div>
    }
  `,
  styles: `
    .vc-notice {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.4rem 0.75rem;
      border-bottom: 1px solid var(--vc-border);
      background: var(--vc-surface-elevated);
    }
    .vc-notice__body {
      flex: 1;
      display: grid;
      gap: 0.1rem;
      border: 0;
      background: transparent;
      color: inherit;
      text-align: left;
      cursor: pointer;
    }
    .vc-notice__body span {
      color: var(--vc-ink-muted);
      font-size: 0.82rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
})
export class InAppNoticeBanner {
  readonly push = inject(PushNotificationService);
  readonly open = output<void>();
}
