import { Component, inject, input, OnInit, signal } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { PluginRegistryEntry, PluginRegistryInstallView } from '../../core/api/plugin-api.service';
import { translateErrorCode, ui } from '../../core/i18n/strings';

@Component({
  selector: 'vc-admin-plugin-registry',
  standalone: true,
  templateUrl: './admin-plugin-registry.html',
  styleUrl: './admin-plugin-registry.scss',
})
export class AdminPluginRegistry implements OnInit {
  readonly workspaceId = input.required<string>();
  readonly ui = ui;

  private readonly api = inject(ApiService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly indexText = signal('');
  readonly grant = signal(false);
  readonly entries = signal<PluginRegistryEntry[]>([]);
  readonly installs = signal<PluginRegistryInstallView[]>([]);
  readonly revealedToken = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  async preview(): Promise<void> {
    const index = this.indexText().trim();
    if (!index || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.api.previewPluginRegistry(this.workspaceId(), index);
      this.entries.set(result.entries);
    } catch (error) {
      this.note(error);
    } finally {
      this.busy.set(false);
    }
  }

  async install(entry: PluginRegistryEntry): Promise<void> {
    if (!this.grant() || !entry.accepted || this.busy()) {
      this.error.set(ui.pluginRegistryGrant);
      return;
    }
    await this.change('install', entry);
  }

  async update(entry: PluginRegistryEntry): Promise<void> {
    if (!this.grant() || !entry.accepted || this.busy()) {
      this.error.set(ui.pluginRegistryCapability);
      return;
    }
    await this.change('update', entry);
  }

  async rollback(row: PluginRegistryInstallView): Promise<void> {
    if (this.busy() || !row.previousVersion) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const updated = await this.api.rollbackPluginRegistry(this.workspaceId(), row.pluginId, crypto.randomUUID());
      this.installs.set(this.installs().map((item) => (item.pluginId === row.pluginId ? updated : item)));
    } catch (error) {
      this.note(error);
    } finally {
      this.busy.set(false);
    }
  }

  async revoke(row: PluginRegistryInstallView): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const updated = await this.api.revokePluginRegistry(this.workspaceId(), row.pluginId, row.version, crypto.randomUUID());
      this.installs.set(this.installs().map((item) => (item.pluginId === row.pluginId ? { ...item, ...updated, token: undefined } : item)));
    } catch (error) {
      this.note(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async change(action: 'install' | 'update', entry: PluginRegistryEntry): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.revealedToken.set(null);
    try {
      const input = {
        index: this.indexText().trim(),
        pluginId: entry.pluginId,
        version: entry.version,
        grantCapabilities: entry.capabilities,
        confirmCapabilities: entry.capabilities,
        channelIds: [],
        allowDms: false,
        idempotencyKey: crypto.randomUUID(),
      };
      const saved = action === 'install'
        ? await this.api.installPluginRegistry(this.workspaceId(), input)
        : await this.api.updatePluginRegistry(this.workspaceId(), input);
      this.revealedToken.set(saved.token ?? null);
      await this.reload();
    } catch (error) {
      this.note(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    try {
      const listed = await this.api.listPluginRegistry(this.workspaceId());
      this.installs.set(listed.installs);
      this.disabled.set(false);
    } catch (error) {
      const message = error instanceof Error ? error.message : '';
      this.disabled.set(message.includes('PluginRegistryDisabled'));
      if (!this.disabled()) this.note(error);
    } finally {
      this.loading.set(false);
    }
  }

  private note(error: unknown): void {
    const message = error instanceof Error ? error.message : '';
    const match = /"error"\s*:\s*"([^"]+)"/.exec(message);
    this.error.set(translateErrorCode(match?.[1]) || ui.adminPluginSaveError);
  }
}
