import { Component, inject, OnInit, signal } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { AutomationDefinitionInput, AutomationItem, AutomationRun } from '../../core/api/automation-api.service';
import { ui } from '../../core/i18n/strings';
import { Channel } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-automations',
  standalone: true,
  templateUrl: './admin-automations.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminAutomationsPage implements OnInit {
  readonly areaId: AdminAreaId = 'automations';
  readonly ui = ui;

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly saveError = signal(false);
  readonly busy = signal(false);
  readonly items = signal<AutomationItem[]>([]);
  readonly channels = signal<Channel[]>([]);
  readonly runs = signal<AutomationRun[]>([]);
  readonly selectedId = signal<string | null>(null);
  readonly name = signal('');
  readonly body = signal('');
  readonly selectedChannelIds = signal<string[]>([]);

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.loadError.set(true);
      return;
    }

    try {
      const [list, channels] = await Promise.all([
        this.api.listAutomations(workspaceId),
        this.api.getChannels(workspaceId),
      ]);
      this.items.set(list.items);
      this.channels.set(channels.filter((channel) => !channel.isDirect && !channel.isGroupDm));
    } catch {
      this.loadError.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  toggleChannel(channelId: string, checked: boolean): void {
    const current = this.selectedChannelIds();
    this.selectedChannelIds.set(checked ? [...current, channelId] : current.filter((id) => id !== channelId));
  }

  async create(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const channelId = this.selectedChannelIds()[0];
    const name = this.name().trim();
    const body = this.body().trim();
    if (!workspaceId || !channelId || !name || !body) {
      return;
    }

    const definition: AutomationDefinitionInput = {
      contractVersion: 'automation.definition.v1',
      trigger: { type: 'message', channelIds: [], formIds: [], event: 'created', intervalMinutes: 0 },
      conditions: [],
      actions: [
        {
          type: 'post_message',
          capability: 'message.send',
          channelId,
          body,
          title: '',
          taskId: null,
          state: '',
          userIds: [],
          connectorId: null,
        },
      ],
      channelIds: this.selectedChannelIds(),
      secretRefs: [],
    };
    await this.mutate(async () => {
      const created = await this.api.createAutomation(workspaceId, name, definition);
      this.items.set([created, ...this.items()]);
      this.selectedId.set(created.id);
    });
  }

  async publish(item: AutomationItem): Promise<void> {
    await this.replace(item, (workspaceId) => this.api.publishAutomation(workspaceId, item.id));
  }

  async setEnabled(item: AutomationItem, enabled: boolean): Promise<void> {
    await this.replace(item, (workspaceId) => this.api.setAutomationEnabled(workspaceId, item.id, enabled));
  }

  async dryRun(item: AutomationItem): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const channelId = item.channelIds[0];
    if (!workspaceId || !channelId) {
      return;
    }

    await this.mutate(async () => {
      const run = await this.api.dryRunAutomation(workspaceId, item.id, channelId);
      this.runs.set([run, ...this.runs()]);
      this.selectedId.set(item.id);
    });
  }

  async showRuns(item: AutomationItem): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      return;
    }

    this.selectedId.set(item.id);
    await this.mutate(async () => {
      const page = await this.api.listAutomationRuns(workspaceId, item.id);
      this.runs.set(page.items);
    });
  }

  private async replace(item: AutomationItem, call: (workspaceId: string) => Promise<AutomationItem>): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      return;
    }

    await this.mutate(async () => {
      const updated = await call(workspaceId);
      this.items.set(this.items().map((row) => (row.id === item.id ? updated : row)));
    });
  }

  private async mutate(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.saveError.set(false);
    try {
      await action();
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }
}
