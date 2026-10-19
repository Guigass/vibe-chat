import { Component, inject, OnInit, signal } from '@angular/core';
import { ScimApiService, ScimGroupItem, ScimPreview, ScimTokenItem } from '../../core/api/scim-api.service';
import { formatLocaleDate } from '../../core/i18n/format';
import { ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-scim',
  standalone: true,
  templateUrl: './admin-scim.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminScimPage implements OnInit {
  readonly areaId: AdminAreaId = 'scim';
  readonly ui = ui;
  readonly roles = ['Member', 'Moderator', 'Auditor', 'Guest'] as const;

  private readonly api = inject(ScimApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly loadError = signal(false);
  readonly saveError = signal(false);
  readonly forbidden = signal(false);
  readonly busy = signal(false);
  readonly tokens = signal<ScimTokenItem[]>([]);
  readonly groups = signal<ScimGroupItem[]>([]);
  readonly tokenName = signal('');
  readonly secret = signal<string | null>(null);
  readonly groupId = signal('');
  readonly role = signal('Member');
  readonly preview = signal<ScimPreview | null>(null);
  readonly confirmRevokeId = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  roleLabel(role: string): string {
    switch (role) {
      case 'Moderator':
        return ui.adminRoleModerator;
      case 'Auditor':
        return ui.adminRoleAuditor;
      case 'Guest':
        return ui.adminRoleGuest;
      default:
        return ui.adminRoleMember;
    }
  }

  lastUsed(value: string | null): string {
    return value ? formatLocaleDate(value) : ui.adminScimNeverUsed;
  }

  async reload(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.loadError.set(true);
      return;
    }

    try {
      const overview = await this.api.load(workspaceId);
      this.tokens.set(overview.tokens);
      this.groups.set(overview.groups);
      if (!this.groupId() && overview.groups[0]) {
        this.groupId.set(overview.groups[0].id);
      }
      this.disabled.set(false);
    } catch (error) {
      const status = (error as { status?: number }).status;
      const message = error instanceof Error ? error.message : '';
      if (status === 404 && message.includes('ScimDisabled')) {
        this.disabled.set(true);
      } else {
        this.loadError.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }

  async createToken(): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const name = this.tokenName().trim();
    if (!workspaceId || !name) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      const created = await this.api.createToken(workspaceId, name);
      this.secret.set(created.secret);
      this.tokenName.set('');
      await this.reload();
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  async copySecret(): Promise<void> {
    const value = this.secret();
    if (!value || !navigator.clipboard) {
      return;
    }

    await navigator.clipboard.writeText(value);
  }

  async rotate(tokenId: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      const created = await this.api.rotateToken(workspaceId, tokenId);
      this.secret.set(created.secret);
      await this.reload();
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  askRevoke(tokenId: string): void {
    this.confirmRevokeId.set(tokenId);
  }

  async revoke(tokenId: string): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    try {
      await this.api.revokeToken(workspaceId, tokenId);
      this.confirmRevokeId.set(null);
      await this.reload();
    } catch {
      this.saveError.set(true);
    } finally {
      this.busy.set(false);
    }
  }

  async dryRun(): Promise<void> {
    await this.submitMapping(false);
  }

  async applyMapping(): Promise<void> {
    await this.submitMapping(true);
  }

  private async submitMapping(apply: boolean): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    const groupId = this.groupId();
    if (!workspaceId || !groupId) {
      return;
    }

    this.busy.set(true);
    this.saveError.set(false);
    this.forbidden.set(false);
    try {
      const preview = apply
        ? await this.api.applyMapping(workspaceId, groupId, this.role())
        : await this.api.dryRun(workspaceId, groupId, this.role());
      this.preview.set(preview);
    } catch (error) {
      const message = error instanceof Error ? error.message : '';
      if (message.includes('ForbiddenRole')) {
        this.forbidden.set(true);
        this.preview.set(null);
      } else {
        this.saveError.set(true);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
