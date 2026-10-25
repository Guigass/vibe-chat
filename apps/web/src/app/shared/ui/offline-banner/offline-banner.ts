import { Component, HostListener, inject, signal } from '@angular/core';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import { OfflineSyncService } from '../../../core/offline/offline-sync.service';

@Component({
  selector: 'vc-offline-banner',
  standalone: true,
  template: `
    @if (offline.ready()) {
      <div class="offline-toggle">
        <button type="button" data-testid="offline-data-toggle" (click)="toggle()">
          {{ offline.confirmingClear() ? ui.offlineConfirmDisable : offline.enabled() ? ui.offlineDisable : offline.cryptoMissing() ? ui.offlineCrypto : ui.offlineEnable }}
        </button>
      </div>
    }
    @if (offline.ready() && offline.enabled() && !online()) {
      <div class="vc-banner vc-banner--warn" role="status" data-testid="offline-banner">
        {{ ui.offlineBanner }}
      </div>
    }
    @if (offline.revoked()) {
      <div class="vc-banner vc-banner--warn" role="status" data-testid="offline-revoked">
        {{ ui.offlineRevoked }}
      </div>
    }
    @if (offline.items().length) {
      <p class="offline-queue__count" data-testid="offline-pending">{{ pendingLabel() }}</p>
      <ul class="offline-queue" [attr.aria-label]="ui.offlineEnable" data-testid="offline-queue">
        @for (item of offline.items(); track item.id) {
          <li [attr.data-state]="item.state">
            <span>{{ label(item.state) }}</span>
            <span class="offline-queue__body">{{ item.serverBody || item.body || item.task?.title || item.emoji }}</span>
            @if (item.state === 'conflict' || item.state === 'failed') {
              <button type="button" (click)="offline.retry(item.id)">{{ ui.offlineRetry }}</button>
              <button type="button" (click)="offline.discard(item.id)">{{ ui.offlineDiscard }}</button>
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .offline-toggle {
      display: flex;
      justify-content: center;
      padding: 0.4rem 0.75rem;
      background: var(--vc-surface-elevated);
      border-bottom: 1px solid var(--vc-border);
    }
    .offline-toggle button {
      max-width: 100%;
      border: 1px solid var(--vc-border);
      background: transparent;
      color: var(--vc-ink);
      border-radius: var(--vc-radius-sm);
      padding: 0.35rem 0.7rem;
      font: inherit;
      font-size: 0.85rem;
      line-height: 1.3;
      text-align: center;
    }
    .vc-banner {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.55rem;
      padding: 0.45rem 0.75rem;
      background: color-mix(in srgb, var(--vc-warning) 16%, var(--vc-surface-elevated));
      color: var(--vc-ink);
      font-size: 0.85rem;
      border-bottom: 1px solid var(--vc-border);
    }
    .offline-queue__count {
      margin: 0;
      padding: 0.45rem 0.75rem 0;
      color: var(--vc-ink-muted);
      font-size: 0.8rem;
      background: var(--vc-surface-elevated);
    }
    .offline-queue {
      list-style: none;
      margin: 0;
      padding: 0;
      background: var(--vc-surface-elevated);
      border-bottom: 1px solid var(--vc-border);
    }
    .offline-queue li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
      min-height: 2.5rem;
      padding: 0.35rem 0.75rem;
      color: var(--vc-ink);
      font-size: 0.85rem;
    }
    .offline-queue li + li {
      border-top: 1px solid var(--vc-border);
    }
    .offline-queue__body {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .offline-queue button {
      border: 1px solid var(--vc-border);
      background: transparent;
      color: var(--vc-ink);
      border-radius: var(--vc-radius-sm, 6px);
      padding: 0.2rem 0.55rem;
      font: inherit;
    }
  `,
})
export class OfflineBanner {
  readonly ui = ui;
  readonly offline = inject(OfflineSyncService);
  readonly online = signal(typeof navigator === 'undefined' ? true : navigator.onLine);

  @HostListener('window:online')
  onOnline(): void {
    this.online.set(true);
  }

  @HostListener('window:offline')
  onOffline(): void {
    this.online.set(false);
  }

  toggle(): void {
    if (!this.offline.enabled()) {
      void this.offline.enable();
      return;
    }
    if (!this.offline.confirmingClear()) {
      this.offline.askClear();
      return;
    }
    void this.offline.clear();
  }

  label(state: string): string {
    if (state === 'conflict') return ui.offlineConflict;
    if (state === 'failed') return ui.offlineFailed;
    return ui.offlineQueued;
  }

  pendingLabel(): string {
    return fillTemplate(ui.offlinePending, { n: this.offline.items().length });
  }
}
