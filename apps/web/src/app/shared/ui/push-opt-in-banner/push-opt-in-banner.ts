import { Component, inject } from '@angular/core';
import { PushNotificationService } from '../../../core/services/push-notification.service';
import { Button } from '../button/button';

@Component({
  selector: 'vc-push-opt-in-banner',
  standalone: true,
  imports: [Button],
  template: `
    @if (push.bannerOpen()) {
      <div class="vc-push" role="status" data-testid="push-opt-in">
        <span class="vc-push__text">Receber notificações neste dispositivo?</span>
        <div class="vc-push__actions">
          <vc-button type="button" variant="ghost" (click)="push.dismissBanner()">Agora não</vc-button>
          <vc-button
            type="button"
            variant="primary"
            [loading]="push.busy()"
            [disabled]="push.busy()"
            (click)="push.enablePush()"
          >
            Ativar
          </vc-button>
        </div>
      </div>
    } @else if (push.permissionDenied()) {
      <div class="vc-push" role="status" data-testid="push-denied">
        <span class="vc-push__text">
          Notificações bloqueadas no navegador. Reative a permissão nas configurações do site.
        </span>
        <vc-button type="button" variant="ghost" (click)="push.permissionDenied.set(false)">Fechar</vc-button>
      </div>
    }
  `,
  styles: `
    .vc-push {
      display: flex;
      align-items: center;
      justify-content: center;
      flex-wrap: wrap;
      gap: 0.75rem;
      padding: 0.45rem 0.75rem;
      background: color-mix(in srgb, var(--vc-brand) 14%, var(--vc-surface-elevated));
      color: var(--vc-ink);
      font-size: 0.85rem;
      font-weight: 500;
      border-bottom: 1px solid var(--vc-border);
    }

    .vc-push__actions {
      display: flex;
      gap: 0.4rem;
    }

    .vc-push ::ng-deep .vc-btn {
      min-height: 2rem;
      padding: 0.35rem 0.85rem;
      font-size: 0.82rem;
    }
  `,
})
export class PushOptInBanner {
  readonly push = inject(PushNotificationService);
}
