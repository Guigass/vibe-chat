import { Component, effect, inject, signal } from '@angular/core';
import { ApiService } from '../core/api/api.service';
import { FederationView } from '../core/api/federation-api.service';
import { ChannelStore } from '../core/services/channel.store';
import { AuthService } from '../core/auth/auth.service';
import { fillTemplate, translateErrorCode, ui } from '../core/i18n/strings';

@Component({
  selector: 'vc-federation-banner',
  standalone: true,
  template: `
    @if (row(); as trust) {
      <section class="federation" data-testid="federation-banner" [attr.data-status]="trust.status">
        <p class="federation__dest">{{ destination(trust) }}</p>
        <p class="federation__meta">{{ state(trust) }} · {{ ui.federationLimitation }}</p>
        <p class="federation__meta">{{ ui.federationRemote }}{{ names(trust) }}</p>
        @if (trust.canManage) {
          <div class="federation__actions">
            @if (trust.consentRequired) {
              <p class="federation__consent">{{ consentCopy(trust) }}</p>
              <button type="button" data-testid="federation-consent" (click)="consent(trust)">{{ ui.federationConsentAction }}</button>
            }
            @if (trust.status === 'active') {
              <button type="button" data-testid="federation-pause" (click)="pause(trust)">{{ ui.federationPause }}</button>
            }
            @if (trust.status === 'paused') {
              <button type="button" data-testid="federation-resume" (click)="resume(trust)">{{ ui.federationResume }}</button>
            }
            @if (trust.status !== 'revoked') {
              <button type="button" data-testid="federation-revoke" (click)="revoke(trust)">
                {{ confirming() ? ui.federationRevokeConfirm : ui.federationRevoke }}
              </button>
            }
          </div>
        }
        @if (error()) {
          <p class="federation__error" role="alert">{{ error() }}</p>
        }
      </section>
    }
  `,
  styles: `
    :host { display: contents; }
    .federation {
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
    .federation__dest,
    .federation__meta,
    .federation__consent,
    .federation__error {
      margin: 0;
      max-width: 100%;
    }
    .federation__dest { color: var(--vc-ink); }
    .federation__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--vc-space-2);
    }
    .federation__actions button {
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
    .federation__actions button:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
    .federation__error { color: var(--vc-ink); }
  `,
})
export class FederationBanner {
  private readonly channels = inject(ChannelStore);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  protected readonly ui = ui;
  protected readonly row = signal<FederationView | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly confirming = signal(false);

  constructor() {
    effect(() => {
      const channel = this.channels.activeChannel();
      const workspaceId = this.channels.activeWorkspace()?.id;
      this.confirming.set(false);
      this.error.set(null);
      if (!channel || !workspaceId || channel.isDirect || channel.isGroupDm || this.channels.isDemo() || this.auth.isOfflineDemo()) {
        this.row.set(null);
        return;
      }
      const channelId = channel.id;
      void this.api.channelFederation(workspaceId, channelId).then(
        (trust) => {
          if (this.channels.activeChannel()?.id === channelId) this.row.set(trust);
        },
        () => {
          if (this.channels.activeChannel()?.id === channelId) this.row.set(null);
        },
      );
    });
  }

  protected destination(trust: FederationView): string {
    return fillTemplate(ui.federationBannerDestination, {
      domain: trust.domain,
      room: trust.remoteRoom || trust.remoteInstanceId,
    });
  }

  protected consentCopy(trust: FederationView): string {
    return fillTemplate(ui.federationConsent, { domain: trust.domain });
  }

  protected names(trust: FederationView): string {
    const labels = trust.identities.map((item) => item.displayName).filter((name) => name.length > 0);
    return labels.length ? ` · ${labels.join(', ')}` : '';
  }

  protected state(trust: FederationView): string {
    switch (trust.status) {
      case 'pending':
        return ui.federationStatePending;
      case 'paused':
        return ui.federationStatePaused;
      case 'revoked':
        return ui.federationStateRevoked;
      default:
        return ui.federationStateActive;
    }
  }

  protected consent(trust: FederationView): void {
    void this.mutate(trust, (workspaceId) => this.api.consentFederation(workspaceId, trust.trustId));
  }

  protected pause(trust: FederationView): void {
    void this.mutate(trust, (workspaceId) => this.api.pauseFederation(workspaceId, trust.trustId));
  }

  protected resume(trust: FederationView): void {
    void this.mutate(trust, (workspaceId) => this.api.resumeFederation(workspaceId, trust.trustId));
  }

  protected revoke(trust: FederationView): void {
    if (!this.confirming()) {
      this.confirming.set(true);
      return;
    }
    void this.mutate(trust, (workspaceId) => this.api.revokeFederation(workspaceId, trust.trustId));
  }

  private async mutate(trust: FederationView, run: (workspaceId: string) => Promise<FederationView>): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId) return;
    this.error.set(null);
    try {
      this.row.set(await run(workspaceId));
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
    return ui.federationStateRevoked;
  }
}
