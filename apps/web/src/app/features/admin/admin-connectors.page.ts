import { Component, inject, OnInit, signal } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { ConnectorCatalogItem, ConnectorInstallation } from '../../core/api/connector-api.service';
import { ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-connectors',
  standalone: true,
  templateUrl: './admin-connectors.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminConnectorsPage implements OnInit {
  readonly areaId: AdminAreaId = 'connectors';
  readonly ui = ui;

  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly loadError = signal(false);
  readonly saveError = signal(false);
  readonly recovery = signal(false);
  readonly busy = signal(false);
  readonly catalog = signal<ConnectorCatalogItem[]>([]);
  readonly items = signal<ConnectorInstallation[]>([]);
  readonly provider = signal('github');
  readonly destinationKind = signal('channel');
  readonly destinationId = signal('');
  readonly token = signal('');
  readonly clientId = signal('');
  readonly baseUrl = signal('');
  readonly rotateToken = signal('');
  readonly confirmingId = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  scopesFor(provider: string): string[] {
    return this.catalog().find((item) => item.id === provider)?.scopes ?? [];
  }

  async create(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const destinationId = this.destinationId().trim();
    const provider = this.provider();
    if (!workspaceId || !destinationId) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    this.recovery.set(false);
    try {
      await this.api.createConnector(workspaceId, {
        idempotencyKey: `ui-${crypto.randomUUID().replace(/-/g, '')}`.slice(0, 24),
        provider,
        destinationKind: this.destinationKind(),
        destinationId,
        scopes: this.scopesFor(provider),
        token: this.token().trim() || undefined,
        clientId: this.clientId().trim() || undefined,
        baseUrl: this.baseUrl().trim() || undefined,
      });
      this.token.set('');
      await this.reload();
    } catch {
      this.saveError.set(true);
      this.recovery.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  async revoke(item: ConnectorInstallation): Promise<void> {
    if (this.confirmingId() !== item.id) {
      this.confirmingId.set(item.id);
      return;
    }

    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      return;
    }

    this.busy.set(true);
    this.recovery.set(false);
    try {
      await this.api.revokeConnector(workspaceId, item.id);
      this.confirmingId.set(null);
      await this.reload();
    } catch {
      this.recovery.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  async rotate(item: ConnectorInstallation): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const token = this.rotateToken().trim();
    if (!workspaceId || !token) {
      return;
    }

    this.busy.set(true);
    this.recovery.set(false);
    try {
      await this.api.rotateConnector(workspaceId, item.id, token);
      this.rotateToken.set('');
      await this.reload();
    } catch {
      this.recovery.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  async reconnect(item: ConnectorInstallation): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      return;
    }

    this.busy.set(true);
    this.recovery.set(false);
    try {
      const result = await this.api.reconnectConnector(workspaceId, item.id);
      this.recovery.set(!result.ok);
      await this.reload();
    } catch {
      this.recovery.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.loadError.set(true);
      return;
    }

    try {
      const [catalog, items] = await Promise.all([
        this.api.listConnectorCatalog(workspaceId),
        this.api.listConnectors(workspaceId),
      ]);
      this.catalog.set(catalog);
      this.items.set(items);
      this.disabled.set(false);
      this.loadError.set(false);
    } catch (error: unknown) {
      const status = typeof error === 'object' && error && 'status' in error ? Number(error.status) : 0;
      this.disabled.set(status === 404);
      this.loadError.set(status !== 404);
    } finally {
      this.loading.set(false);
    }
  }
}
