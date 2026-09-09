import { Component, inject } from '@angular/core';
import { PushNotificationService } from '../../../core/services/push-notification.service';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-push-devices-control',
  standalone: true,
  template: `
    <div class="vc-push-devices" data-testid="push-devices">
      <p class="vc-push-devices__title">{{ ui.pushDevicesTitle }}</p>
      @if (push.devices().length === 0) {
        <p class="vc-push-devices__empty">{{ ui.pushDevicesEmpty }}</p>
      } @else {
        <ul>
          @for (device of push.devices(); track device.id) {
            <li>
              <span class="vc-push-devices__name">{{ deviceLabel(device.userAgent, device.endpoint) }}</span>
              <button type="button" class="vc-push-devices__remove" (click)="push.removeDevice(device.id)">
                {{ ui.pushDeviceRemove }}
              </button>
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .vc-push-devices {
      display: grid;
      gap: 0.55rem;
    }
    .vc-push-devices__title {
      margin: 0;
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
    .vc-push-devices__remove:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
  `,
})
export class PushDevicesControl {
  readonly push = inject(PushNotificationService);
  readonly ui = ui;

  deviceLabel(userAgent: string | null | undefined, endpoint: string): string {
    const ua = userAgent?.trim() ?? '';
    if (!ua) {
      return endpoint.replace(/^https?:\/\//, '').slice(0, 32) || ui.pushThisBrowser;
    }

    const browser = /Edg\//.test(ua)
      ? 'Edge'
      : /Chrome\//.test(ua)
        ? 'Chrome'
        : /Firefox\//.test(ua)
          ? 'Firefox'
          : /Safari\//.test(ua)
            ? 'Safari'
            : ui.pushThisBrowser;
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
    return os ? `${browser} · ${os}` : browser;
  }
}
