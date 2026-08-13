import { Component, inject } from '@angular/core';
import { PushNotificationService } from '../../../core/services/push-notification.service';
import { IconButton } from '../icon-button/icon-button';
import { Button } from '../button/button';

@Component({
  selector: 'vc-push-devices-control',
  standalone: true,
  imports: [IconButton, Button],
  template: `
    <div class="vc-push-devices">
      <vc-icon-button label="Dispositivos de notificação" (click)="push.toggleDevices()">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
          <path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9" />
          <path d="M10.3 21a1.94 1.94 0 0 0 3.4 0" />
        </svg>
      </vc-icon-button>
      @if (push.devicesOpen()) {
        <div class="vc-push-devices__panel" role="dialog" aria-label="Dispositivos de notificação">
          <p class="vc-push-devices__title">Este e outros dispositivos</p>
          @if (push.devices().length === 0) {
            <p class="vc-push-devices__empty">Nenhum dispositivo registrado.</p>
          } @else {
            <ul>
              @for (device of push.devices(); track device.id) {
                <li>
                  <span>{{ deviceLabel(device.userAgent, device.endpoint) }}</span>
                  <vc-button type="button" variant="ghost" (click)="push.removeDevice(device.id)">
                    Remover
                  </vc-button>
                </li>
              }
            </ul>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .vc-push-devices {
      position: relative;
    }
    .vc-push-devices__panel {
      position: absolute;
      right: 0;
      top: calc(100% + 0.4rem);
      z-index: 20;
      width: min(22rem, 80vw);
      padding: 0.75rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      box-shadow: var(--vc-shadow-md, 0 8px 24px rgb(0 0 0 / 12%));
    }
    .vc-push-devices__title {
      margin: 0 0 0.5rem;
      font-weight: 600;
      font-size: 0.85rem;
    }
    .vc-push-devices__empty {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.82rem;
    }
    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.4rem;
    }
    li {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      font-size: 0.8rem;
    }
    li span {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
})
export class PushDevicesControl {
  readonly push = inject(PushNotificationService);

  deviceLabel(userAgent: string | null | undefined, endpoint: string): string {
    if (userAgent?.trim()) {
      return userAgent.trim().slice(0, 48);
    }
    return endpoint.replace(/^https?:\/\//, '').slice(0, 48);
  }
}
