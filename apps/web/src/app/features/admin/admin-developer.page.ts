import { Component, inject, OnInit, signal } from '@angular/core';
import { DeveloperApiService, DeveloperCatalog, DeveloperCredential, DeveloperExploreResult, DeveloperUsage } from '../../core/api/developer-api.service';
import { PluginSdkCatalog, SdkApiService } from '../../core/api/sdk-api.service';
import { ApiService } from '../../core/api/api.service';
import { fillTemplate, translateErrorCode, ui } from '../../core/i18n/strings';
import { Channel } from '../../shared/models/chat.models';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-developer',
  standalone: true,
  templateUrl: './admin-developer.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminDeveloperPage implements OnInit {
  readonly areaId: AdminAreaId = 'developer';
  readonly ui = ui;

  private readonly portal = inject(DeveloperApiService);
  private readonly sdk = inject(SdkApiService);
  private readonly api = inject(ApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly denied = signal(false);
  readonly loadError = signal(false);
  readonly busy = signal(false);
  readonly catalog = signal<DeveloperCatalog | null>(null);
  readonly sdkCatalog = signal<PluginSdkCatalog | null>(null);
  readonly sdkDisabled = signal(false);
  readonly credentials = signal<DeveloperCredential[]>([]);
  readonly channels = signal<Channel[]>([]);
  readonly usage = signal<DeveloperUsage | null>(null);
  readonly name = signal('');
  readonly allowDms = signal(false);
  readonly selectedChannelIds = signal<string[]>([]);
  readonly secret = signal<string | null>(null);
  readonly copied = signal(false);
  readonly confirmingId = signal<string | null>(null);
  readonly exploreCredentialId = signal('');
  readonly exploreOperation = signal('messages.send');
  readonly exploreChannelId = signal('');
  readonly exploreResult = signal<DeveloperExploreResult | null>(null);
  readonly inspectorEvent = signal<string | null>(null);
  readonly formError = signal('');

  async ngOnInit(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.denied.set(true);
      return;
    }

    try {
      const [catalog, credentials, usage, channels] = await Promise.all([
        this.portal.catalog(workspaceId),
        this.portal.credentials(workspaceId),
        this.portal.usage(workspaceId),
        this.api.getChannels(workspaceId),
      ]);
      this.catalog.set(catalog);
      this.credentials.set(credentials);
      this.usage.set(usage);
      this.channels.set(channels.filter((channel) => !channel.isDirect && !channel.isGroupDm));
      if (!this.exploreCredentialId() && credentials[0]) {
        this.exploreCredentialId.set(credentials[0].id);
      }
      await this.loadSdk(workspaceId);
    } catch (error) {
      this.readFailure(error, true);
    } finally {
      this.loading.set(false);
    }
  }

  toggleChannel(id: string, checked: boolean): void {
    const current = this.selectedChannelIds();
    this.selectedChannelIds.set(checked ? [...current, id] : current.filter((item) => item !== id));
  }

  async create(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    try {
      const created = await this.portal.createCredential(
        workspaceId,
        { name: this.name().trim(), channelIds: this.selectedChannelIds(), allowDms: this.allowDms() },
        crypto.randomUUID(),
      );
      this.secret.set(created.token);
      this.copied.set(false);
      this.name.set('');
      await this.reload(workspaceId);
    } catch (error) {
      this.formError.set(this.readFailure(error, false));
    } finally {
      this.busy.set(false);
    }
  }

  async copySecret(): Promise<void> {
    const value = this.secret();
    if (!value) return;
    try {
      await navigator.clipboard.writeText(value);
      this.secret.set(null);
      this.copied.set(true);
    } catch {
      this.formError.set(ui.developerCopyError);
    }
  }

  dismissSecret(): void {
    this.secret.set(null);
  }

  async rotate(id: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    try {
      const rotated = await this.portal.rotateCredential(workspaceId, id, crypto.randomUUID());
      this.secret.set(rotated.token);
      this.copied.set(false);
      await this.reload(workspaceId);
    } catch (error) {
      this.formError.set(this.readFailure(error, false));
    } finally {
      this.busy.set(false);
    }
  }

  async revoke(id: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    try {
      await this.portal.revokeCredential(workspaceId, id);
      this.confirmingId.set(null);
      if (this.secret()) this.secret.set(null);
      await this.reload(workspaceId);
    } catch (error) {
      this.formError.set(this.readFailure(error, false));
    } finally {
      this.busy.set(false);
    }
  }

  async runExplore(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const credentialId = this.exploreCredentialId();
    if (!workspaceId || !credentialId || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    this.exploreResult.set(null);
    try {
      const result = await this.portal.explore(workspaceId, {
        credentialId,
        operation: this.exploreOperation(),
        channelId: this.exploreOperation() === 'messages.send' ? this.exploreChannelId() || undefined : undefined,
      });
      this.exploreResult.set(result);
      this.usage.set(await this.portal.usage(workspaceId));
    } catch (error) {
      this.formError.set(this.readFailure(error, false));
    } finally {
      this.busy.set(false);
    }
  }

  async inspect(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    try {
      const result = await this.portal.inspectWebhook(workspaceId);
      this.inspectorEvent.set(result.eventType);
      this.usage.set(await this.portal.usage(workspaceId));
    } catch (error) {
      this.formError.set(this.readFailure(error, false));
    } finally {
      this.busy.set(false);
    }
  }

  async download(kind: 'openapi' | 'kit'): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) return;
    const document = kind === 'openapi' ? await this.portal.openApi(workspaceId) : await this.portal.contractKit(workspaceId);
    const blob = new Blob([JSON.stringify(document)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const anchor = window.document.createElement('a');
    anchor.href = url;
    anchor.download = kind === 'openapi' ? ui.developerOpenApiFile : ui.developerKitFile;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  rateLimit(): string {
    const value = this.usage()?.sendPerMinute ?? this.catalog()?.rateLimit.sendPerMinute ?? 0;
    return fillTemplate(ui.developerRateLimit, { n: value });
  }

  statusLabel(status: string): string {
    return status === 'deprecated' ? ui.developerStatusDeprecated : ui.developerStatusCurrent;
  }

  changelogLabel(id: string): string {
    if (id === 'integration-http-v1') return ui.developerChangelogHttp;
    if (id === 'messaging.message.created.v1') return ui.developerChangelogEvent;
    return id;
  }

  private async loadSdk(workspaceId: string): Promise<void> {
    try {
      this.sdkCatalog.set(await this.sdk.catalog(workspaceId));
    } catch (error) {
      const message = error instanceof Error ? error.message : '';
      if (message.includes('PluginSdkDisabled')) {
        this.sdkDisabled.set(true);
      }
    }
  }

  private async reload(workspaceId: string): Promise<void> {
    const [credentials, usage] = await Promise.all([
      this.portal.credentials(workspaceId),
      this.portal.usage(workspaceId),
    ]);
    this.credentials.set(credentials);
    this.usage.set(usage);
    if (!this.exploreCredentialId() && credentials[0]) {
      this.exploreCredentialId.set(credentials[0].id);
    }
  }

  private readFailure(error: unknown, initial: boolean): string {
    const status = (error as { status?: number }).status;
    const message = error instanceof Error ? error.message : '';
    if (status === 404 && message.includes('DeveloperPortalDisabled')) {
      if (initial) this.disabled.set(true);
      return ui.developerDisabled;
    }
    if (status === 403) {
      if (initial) this.denied.set(true);
      return ui.developerDenied;
    }
    if (initial) this.loadError.set(true);
    const code = message.match(/"error":"([^"]+)"/)?.[1];
    return translateErrorCode(code) || ui.developerError;
  }
}
