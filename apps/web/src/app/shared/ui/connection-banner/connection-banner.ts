import { Component, HostListener, input, signal } from '@angular/core';
import { ConnectionStatus } from '../../../core/services/chat-hub.service';

@Component({
  selector: 'vc-connection-banner',
  standalone: true,
  template: `
    @if (!online()) {
      <div class="vc-banner vc-banner--warn" role="status">
        Você está offline. O shell do app continua disponível; envios ficam pausados.
      </div>
    } @else if (status() === 'reconnecting' || status() === 'connecting') {
      <div class="vc-banner" role="status">
        <span class="vc-banner__dot" aria-hidden="true"></span>
        Reconectando ao tempo real…
      </div>
    } @else if (status() === 'disconnected') {
      <div class="vc-banner vc-banner--warn" role="status">
        Sem conexão em tempo real. Mensagens podem atrasar.
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
export class ConnectionBanner {
  readonly status = input<ConnectionStatus>('disconnected');
  readonly online = signal(typeof navigator === 'undefined' ? true : navigator.onLine);

  @HostListener('window:online')
  onOnline(): void {
    this.online.set(true);
  }

  @HostListener('window:offline')
  onOffline(): void {
    this.online.set(false);
  }
}
