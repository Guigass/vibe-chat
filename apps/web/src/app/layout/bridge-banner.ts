import { Component, effect, inject, signal } from '@angular/core';
import { ApiService } from '../core/api/api.service';
import { BridgeView } from '../core/api/bridge-api.service';
import { ChannelStore } from '../core/services/channel.store';
import { AuthService } from '../core/auth/auth.service';
import { fillTemplate, translateErrorCode, ui } from '../core/i18n/strings';

@Component({
  selector: 'vc-bridge-banner',
  standalone: true,
  template: `
    @if (bridge(); as row) {
      <section class="bridge" data-testid="bridge-banner" [attr.data-status]="row.status">
        <p class="bridge__dest">{{ destination(row) }}</p>
        <p class="bridge__meta">{{ state(row) }} · {{ ui.bridgeLimitation }}</p>
        @if (row.canManage) {
          <div class="bridge__actions">
            @if (row.consentRequired) {
              <p class="bridge__consent">{{ ui.bridgeConsent }}</p>
              <button type="button" data-testid="bridge-consent" (click)="consent(row)">{{ ui.bridgeConsentAction }}</button>
            }
            @if (row.status === 'active') {
              <button type="button" data-testid="bridge-pause" (click)="pause(row)">{{ ui.bridgePause }}</button>
            }
            @if (row.status === 'paused') {
              <button type="button" data-testid="bridge-resume" (click)="resume(row)">{{ ui.bridgeResume }}</button>
            }
            @if (row.status !== 'disconnected') {
              <button type="button" data-testid="bridge-disconnect" (click)="disconnect(row)">
                {{ confirming() ? ui.bridgeDisconnectConfirm : ui.bridgeDisconnect }}
              </button>
            }
          </div>
        }
        @if (error()) {
          <p class="bridge__error" role="alert">{{ error() }}</p>
        }
      </section>
    }
  `,
  styles: `
    :host {
      display: contents;
    }
    .bridge {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--vc-space-1);
      width: 100%;
      min-width: 0;
      margin: 0;
      padding-block: var(--vc-space-1);
      color: var(--vc-ink-muted);
      font-size: var(--vc-text-sm);
    }
    .bridge__dest,
    .bridge__meta,
    .bridge__consent,
    .bridge__error {
      margin: 0;
      max-width: 100%;
    }
    .bridge__dest { color: var(--vc-ink); }
    .bridge__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--vc-space-2);
    }
    .bridge__actions button {
      margin: 0;
      padding: 0.15rem 0.55rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: transparent;
      color: var(--vc-ink);
      font: inherit;
      font-size: var(--vc-text-sm);
      cursor: pointer;
    }
    .bridge__actions button:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
    .bridge__error { color: var(--vc-ink); }
  `,
})
export class BridgeBanner {
  private readonly channels = inject(ChannelStore);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  protected readonly ui = ui;
  protected readonly bridge = signal<BridgeView | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly confirming = signal(false);

  constructor() {
    effect(() => {
      const channel = this.channels.activeChannel();
      const workspaceId = this.channels.activeWorkspace()?.id;
      this.confirming.set(false);
      this.error.set(null);
      if (!channel || !workspaceId || channel.isDirect || this.channels.isDemo() || this.auth.isOfflineDemo()) {
        this.bridge.set(null);
        return;
      }
      const channelId = channel.id;
      void this.api.channelBridge(workspaceId, channelId).then(
        (row) => {
          if (this.channels.activeChannel()?.id === channelId) this.bridge.set(row);
        },
        () => {
          if (this.channels.activeChannel()?.id === channelId) this.bridge.set(null);
        },
      );
    });
  }

  protected destination(row: BridgeView): string {
    return fillTemplate(ui.bridgeBannerDestination, { network: row.network, room: row.remoteRoom });
  }

  protected state(row: BridgeView): string {
    switch (row.status) {
      case 'pending':
        return ui.bridgeStatePending;
      case 'paused':
        return ui.bridgeStatePaused;
      case 'disconnected':
        return ui.bridgeStateDisconnected;
      default:
        return ui.bridgeStateActive;
    }
  }

  protected consent(row: BridgeView): void {
    void this.mutate(row, (workspaceId) => this.api.consentBridge(workspaceId, row.id));
  }

  protected pause(row: BridgeView): void {
    void this.mutate(row, (workspaceId) => this.api.pauseBridge(workspaceId, row.id));
  }

  protected resume(row: BridgeView): void {
    void this.mutate(row, (workspaceId) => this.api.resumeBridge(workspaceId, row.id));
  }

  protected disconnect(row: BridgeView): void {
    if (!this.confirming()) {
      this.confirming.set(true);
      return;
    }
    void this.mutate(row, (workspaceId) => this.api.disconnectBridge(workspaceId, row.id));
  }

  private async mutate(row: BridgeView, run: (workspaceId: string) => Promise<BridgeView>): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return;
    this.error.set(null);
    try {
      this.bridge.set(await run(workspaceId));
      this.confirming.set(false);
    } catch (error) {
      this.error.set(readError(error));
    }
  }
}

function readError(error: unknown): string {
  const text = error instanceof Error ? error.message : '';
  try {
    const parsed = JSON.parse(text) as { error?: string };
    return translateErrorCode(parsed.error);
  } catch {
    return ui.bridgeStateDisconnected;
  }
}
