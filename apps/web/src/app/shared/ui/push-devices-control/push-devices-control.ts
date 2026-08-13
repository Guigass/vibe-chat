import { Component, inject } from '@angular/core';
import { PushNotificationService } from '../../../core/services/push-notification.service';
import { IconButton } from '../icon-button/icon-button';

@Component({
  selector: 'vc-push-devices-control',
  standalone: true,
  imports: [IconButton],
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
                  <span class="vc-push-devices__name">{{ deviceLabel(device.userAgent, device.endpoint) }}</span>
                  <button type="button" class="vc-push-devices__remove" (click)="push.removeDevice(device.id)">
                    Remover
                  </button>
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
      box-sizing: border-box;
      width: min(18rem, calc(100vw - 1.5rem));
      padding: 0.75rem;
      overflow: hidden;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      box-shadow: var(--vc-shadow-md, 0 8px 24px rgb(0 0 0 / 12%));
    }
    .vc-push-devices__title {
      margin: 0 0 0.55rem;
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
      gap: 0.35rem;
    }
    li {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      min-width: 0;
      font-size: 0.82rem;
    }
    .vc-push-devices__name {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .vc-push-devices__remove {
      flex-shrink: 0;
      margin: 0;
      padding: 0.15rem 0;
      border: 0;
      background: transparent;
      color: var(--vc-ink-muted);
      cursor: pointer;
      font: inherit;
      font-size: 0.8rem;
      font-weight: 500;
    }
    .vc-push-devices__remove:hover {
      color: var(--vc-danger);
    }
  `,
})
export class PushDevicesControl {
  readonly push = inject(PushNotificationService);

  deviceLabel(userAgent: string | null | undefined, endpoint: string): string {
    const ua = userAgent?.trim() ?? '';
    if (!ua) {
      return endpoint.replace(/^https?:\/\//, '').slice(0, 32) || 'Este navegador';
    }

    const browser = /Edg\//.test(ua)
      ? 'Edge'
      : /Chrome\//.test(ua)
        ? 'Chrome'
        : /Firefox\//.test(ua)
          ? 'Firefox'
          : /Safari\//.test(ua)
            ? 'Safari'
            : 'Navegador';
    const os = /Windows/.test(ua)
      ? 'Windows'
      : /Mac OS X|Macintosh/.test(ua)
        ? 'macOS'
        : /Android/.test(ua)
          ? 'Android'
          : /iPhone|iPad/.test(ua)
            ? 'iOS'
            : /Linux/.test(ua)
              ? 'Linux'
              : '';
    return os ? `${browser} no ${os}` : browser;
  }
}
