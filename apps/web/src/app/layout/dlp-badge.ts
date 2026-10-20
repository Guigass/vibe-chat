import { Component, effect, inject, signal } from '@angular/core';
import { ApiService } from '../core/api/api.service';
import { AuthService } from '../core/auth/auth.service';
import { ui } from '../core/i18n/strings';
import { ChannelStore } from '../core/services/channel.store';

@Component({
  selector: 'vc-dlp-badge',
  standalone: true,
  template: `
    @if (label(); as row) {
      <p data-testid="dlp-label">{{ ui.dlpBadge }}: {{ name(row.key) }}@if (row.notInspectable) { · {{ ui.dlpNotInspectable }} }</p>
    }
  `,
})
export class DlpBadge {
  private readonly channels = inject(ChannelStore);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  protected readonly ui = ui;
  protected readonly label = signal<{ key: string; notInspectable: boolean } | null>(null);

  constructor() {
    effect(() => {
      const channel = this.channels.activeChannel();
      const workspaceId = this.channels.activeWorkspace()?.id;
      if (!channel || !workspaceId || this.channels.isDemo() || this.auth.isOfflineDemo()) {
        this.label.set(null);
        return;
      }
      void this.api.dlpLabel(workspaceId, channel.id).then(
        (row) => this.label.set(row.labelKey ? { key: row.labelKey, notInspectable: row.notInspectable } : null),
        () => this.label.set(null),
      );
    });
  }

  protected name(key: string): string {
    switch (key) {
      case 'public':
        return ui.dlpPublic;
      case 'internal':
        return ui.dlpInternal;
      case 'confidential':
        return ui.dlpConfidential;
      case 'restricted':
        return ui.dlpRestricted;
      default:
        return key;
    }
  }
}
