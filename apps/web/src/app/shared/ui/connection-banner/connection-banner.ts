import { Component, HostListener, OnDestroy, OnInit, effect, inject, input, signal } from '@angular/core';
import { OfflineSyncService } from '../../../core/offline/offline-sync.service';
import { ConnectionStatus } from '../../../core/services/chat-hub.service';
import { MessageStore } from '../../../core/services/message.store';
import { ui } from '../../../core/i18n/strings';
import { OfflineBanner } from '../offline-banner/offline-banner';

@Component({
  selector: 'vc-connection-banner',
  standalone: true,
  imports: [OfflineBanner],
  template: `
    <vc-offline-banner />
    @if (!online() && !offline.enabled()) {
      <div class="vc-banner vc-banner--warn" role="status">
        {{ ui.bannerOffline }}
      </div>
    } @else if (maintenance()) {
      <div class="vc-banner" role="status">
        {{ ui.bannerMaintenance }}
      </div>
    } @else if (status() === 'reconnecting' || status() === 'connecting') {
      <div class="vc-banner" role="status">
        <span class="vc-banner__dot" aria-hidden="true"></span>
        {{ ui.bannerReconnecting }}
      </div>
    } @else if (status() === 'disconnected') {
      <div class="vc-banner vc-banner--warn" role="status">
        {{ ui.bannerNoRealtime }}
      </div>
    }
  `,
  styles: `
    .vc-banner {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.55rem;
      padding: 0.45rem 0.75rem;
      background: color-mix(in srgb, var(--vc-brand) 18%, var(--vc-surface-elevated));
      color: var(--vc-brand-ink);
      font-size: 0.85rem;
      font-weight: 500;
      border-bottom: 1px solid var(--vc-border);
    }
    .vc-banner--warn {
      background: color-mix(in srgb, var(--vc-warning) 16%, var(--vc-surface-elevated));
      color: var(--vc-ink);
    }
    .vc-banner__dot {
      width: 0.55rem;
      height: 0.55rem;
      border-radius: 50%;
      background: var(--vc-brand);
      animation: vc-connection-pulse 1.5s ease-out infinite;
    }
  `,
})
export class ConnectionBanner implements OnInit, OnDestroy {
  readonly ui = ui;
  readonly status = input<ConnectionStatus>('disconnected');
  readonly maintenance = input(false);
  readonly offline = inject(OfflineSyncService);
  private readonly messages = inject(MessageStore);
  private readonly deliver = effect(() => {
    const message = this.offline.delivered();
    if (message) this.messages.ingestRemote(message);
  });
  readonly online = signal(typeof navigator === 'undefined' ? true : navigator.onLine);

  ngOnInit(): void {
    this.offline.start();
  }

  ngOnDestroy(): void {
    this.offline.stop();
  }

  @HostListener('window:online')
  onOnline(): void {
    this.online.set(true);
  }

  @HostListener('window:offline')
  onOffline(): void {
    this.online.set(false);
  }
}
