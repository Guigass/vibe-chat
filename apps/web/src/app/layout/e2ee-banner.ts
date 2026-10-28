import { Component, effect, inject, signal } from '@angular/core';
import { E2eeApiService, E2eeChannelDevice } from '../core/api/e2ee-api.service';
import {
  createDevice,
  fingerprintOf,
  formatFingerprint,
  isConfidentialChannel,
  loadDevice,
  sealBackup,
} from '../core/e2ee/e2ee-crypto';
import { ui } from '../core/i18n/strings';
import { ChannelStore } from '../core/services/channel.store';

@Component({
  selector: 'vc-e2ee-banner',
  standalone: true,
  template: `
    @if (visible()) {
      <section class="e2ee" data-testid="e2ee-banner">
        <p class="e2ee__title">{{ ui.e2eeBanner }}</p>
        <p class="e2ee__meta">{{ ui.e2eeCapabilityBody }}</p>
        @if (fingerprint()) {
          <p class="e2ee__meta" data-testid="e2ee-fingerprint">{{ ui.e2eeFingerprint }} {{ fingerprint() }}</p>
        }
        <div class="e2ee__actions">
          @for (device of others(); track device.deviceId) {
            <button type="button" (click)="verify(device)" [disabled]="device.verified">
              {{ device.verified ? ui.e2eeVerified : ui.e2eeVerify }}
            </button>
          }
          <button type="button" data-testid="e2ee-backup" (click)="backup()">
            {{ confirming() ? ui.e2eeBackupWarning : ui.e2eeBackup }}
          </button>
          @if (confirming()) {
            <input
              type="password"
              data-testid="e2ee-backup-password"
              [value]="password()"
              [attr.aria-label]="ui.e2eeBackupPassword"
              (input)="onPassword($event)"
            />
          }
        </div>
        @if (error()) {
          <p class="e2ee__error" role="alert">{{ error() }}</p>
        }
      </section>
    }
  `,
  styles: `
    :host { display: contents; }
    .e2ee {
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
    .e2ee__title,
    .e2ee__meta,
    .e2ee__error { margin: 0; max-width: 100%; }
    .e2ee__title { color: var(--vc-ink); }
    .e2ee__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--vc-space-2);
    }
    .e2ee__actions button {
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
    .e2ee__error { color: var(--vc-danger); }
  `,
})
export class E2eeBanner {
  private readonly channels = inject(ChannelStore);
  private readonly api = inject(E2eeApiService);
  readonly ui = ui;
  readonly visible = signal(false);
  readonly fingerprint = signal('');
  readonly others = signal<E2eeChannelDevice[]>([]);
  readonly confirming = signal(false);
  readonly password = signal('');
  readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const channel = this.channels.activeChannel();
      if (!channel || !isConfidentialChannel(channel)) {
        this.visible.set(false);
        this.others.set([]);
        return;
      }
      this.visible.set(true);
      void this.load(channel.id);
    });
  }

  async verify(device: E2eeChannelDevice): Promise<void> {
    this.error.set(null);
    try {
      await this.api.verifyE2eeDevice(device.deviceId, device.fingerprint);
      this.others.update((rows) =>
        rows.map((row) => (row.deviceId === device.deviceId ? { ...row, verified: true } : row)),
      );
    } catch {
      this.error.set(ui.e2eeLocked);
    }
  }

  onPassword(event: Event): void {
    this.password.set((event.target as HTMLInputElement).value);
  }

  async backup(): Promise<void> {
    if (!this.confirming()) {
      this.confirming.set(true);
      return;
    }
    const password = this.password().trim();
    if (!password) return;
    this.confirming.set(false);
    this.password.set('');
    const record = loadDevice() ?? (await createDevice());
    const blob = await sealBackup(password, record);
    try {
      await this.api.saveE2eeBackup(blob);
    } catch {
      this.error.set(ui.e2eeLocked);
    }
  }

  private async load(channelId: string): Promise<void> {
    try {
      const record = loadDevice() ?? (await createDevice());
      const state = await this.api.e2eeChannel(channelId);
      const mine = state.devices.find((device) => device.deviceId === record.deviceId);
      this.fingerprint.set(formatFingerprint(mine?.fingerprint ?? (await fingerprintOf(record.publicJwk))));
      this.others.set(state.devices.filter((device) => device.deviceId !== record.deviceId));
      const known = state.devices.some((device) => device.deviceId === record.deviceId);
      if (!known) {
        await this.api.registerE2eeDevice(record.deviceId, record.publicJwk);
      }
    } catch {
      this.error.set(ui.e2eeComposerBlocked);
    }
  }
}
