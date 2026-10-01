import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api/api.service';
import { ui } from '../../core/i18n/strings';
import { Channel, InstalledPlugin } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';
import { filterInstalledPlugins } from './plugin-list';

@Component({
  selector: 'vc-admin-plugins',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './admin-plugins.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminPluginsPage implements OnInit {
  readonly areaId: AdminAreaId = 'plugins';
  readonly ui = ui;
  readonly builtinId = 'incoming-messages';

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly saveError = signal(false);
  readonly busy = signal(false);
  readonly plugins = signal<InstalledPlugin[]>([]);
  readonly channels = signal<Channel[]>([]);
  readonly filterQuery = signal('');
  readonly manifestText = signal('');
  readonly allowDms = signal(false);
  readonly selectedChannelIds = signal<string[]>([]);
  readonly revealedToken = signal<string | null>(null);
  readonly filtered = computed(() => filterInstalledPlugins(this.plugins(), this.filterQuery()));

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.loadError.set(true);
      return;
    }

    try {
      const [plugins, channels] = await Promise.all([
        this.api.listInstalledPlugins(workspaceId),
        this.api.getChannels(workspaceId),
      ]);
      this.plugins.set(plugins);
      this.channels.set(channels.filter((channel) => !channel.isDirect && !channel.isGroupDm));
    } catch {
      this.loadError.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  toggleChannel(channelId: string, checked: boolean): void {
    const current = this.selectedChannelIds();
    this.selectedChannelIds.set(
      checked ? [...current, channelId] : current.filter((id) => id !== channelId),
    );
  }

  async installBuiltin(): Promise<void> {
    await this.install({ builtinId: this.builtinId });
  }

  async installManifest(): Promise<void> {
    const raw = this.manifestText().trim();
    if (!raw) {
      return;
    }

    let manifest: Record<string, unknown>;
    try {
      const parsed: unknown = JSON.parse(raw);
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
        this.saveError.set(true);
        return;
      }
      manifest = parsed as Record<string, unknown>;
    } catch {
      this.saveError.set(true);
      return;
    }

    const created = await this.install({ manifest });
    if (created) {
      this.manifestText.set('');
    }
  }

  async setEnabled(plugin: InstalledPlugin, enabled: boolean): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      const updated = await this.api.updateInstalledPlugin(workspaceId, plugin.id, enabled);
      this.plugins.set(this.plugins().map((item) => (item.id === plugin.id ? { ...item, ...updated, token: undefined } : item)));
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  async rotate(plugin: InstalledPlugin): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      const rotated = await this.api.rotateInstalledPlugin(workspaceId, plugin.id);
      this.plugins.set(this.plugins().map((item) => (item.id === plugin.id ? { ...item, ...rotated, token: undefined } : item)));
      this.revealedToken.set(rotated.token ?? null);
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  async uninstall(plugin: InstalledPlugin): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      await this.api.uninstallPlugin(workspaceId, plugin.id);
      this.plugins.set(this.plugins().filter((item) => item.id !== plugin.id));
      this.revealedToken.set(null);
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  private async install(input: { builtinId?: string; manifest?: Record<string, unknown> }): Promise<boolean> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return false;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      const created = await this.api.installPlugin(workspaceId, {
        ...input,
        channelIds: this.selectedChannelIds(),
        allowDms: this.allowDms(),
      });
      this.plugins.set([...this.plugins(), created]);
      this.revealedToken.set(created.token ?? null);
      this.allowDms.set(false);
      this.selectedChannelIds.set([]);
      return true;
    } catch {
      this.saveError.set(true);
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
