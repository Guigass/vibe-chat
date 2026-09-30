import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api/api.service';
import { ui } from '../../core/i18n/strings';
import { Channel, IntegrationBot } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

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

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly saveError = signal(false);
  readonly busy = signal(false);
  readonly bots = signal<IntegrationBot[]>([]);
  readonly channels = signal<Channel[]>([]);
  readonly name = signal('');
  readonly allowDms = signal(false);
  readonly selectedChannelIds = signal<string[]>([]);
  readonly revealedToken = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.loadError.set(true);
      return;
    }

    try {
      const [bots, channels] = await Promise.all([
        this.api.listIntegrationBots(workspaceId),
        this.api.getChannels(workspaceId),
      ]);
      this.bots.set(bots);
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

  async create(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const name = this.name().trim();
    if (!workspaceId || !name || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      const created = await this.api.createIntegrationBot(workspaceId, {
        name,
        channelIds: this.selectedChannelIds(),
        allowDms: this.allowDms(),
      });
      this.bots.set([...this.bots(), created]);
      this.revealedToken.set(created.token ?? null);
      this.name.set('');
      this.allowDms.set(false);
      this.selectedChannelIds.set([]);
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  async rotate(bot: IntegrationBot): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      const rotated = await this.api.rotateIntegrationBot(workspaceId, bot.id);
      this.bots.set(this.bots().map((item) => (item.id === bot.id ? { ...item, ...rotated, token: undefined } : item)));
      this.revealedToken.set(rotated.token ?? null);
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  async revoke(bot: IntegrationBot): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      await this.api.revokeIntegrationBot(workspaceId, bot.id);
      this.bots.set(
        this.bots().map((item) =>
          item.id === bot.id ? { ...item, tokenConfigured: false, tokenLast4: null } : item,
        ),
      );
      this.revealedToken.set(null);
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }
}
