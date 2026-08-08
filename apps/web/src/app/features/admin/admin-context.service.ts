import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { Workspace } from '../../shared/models/chat.models';
import {
  AdminAreaId,
  AdminNavItem,
  canAccessArea as canAccessAdminArea,
  firstAllowedPath,
  hasAdminDashboard,
  hasWorkspaceAdmin,
  visibleNavItems,
} from './admin-permissions';

const MANAGER_ROLES = new Set(['PlatformOwner', 'WorkspaceOwner', 'Admin']);

@Injectable({ providedIn: 'root' })
export class AdminContextService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  private readonly readySignal = signal(false);
  private initPromise: Promise<void> | null = null;

  readonly workspace = signal<Workspace | null>(null);
  readonly currentUserId = signal<string | null>(null);
  readonly loadError = signal(false);

  readonly role = computed(() => this.workspace()?.role ?? null);
  readonly canAccessAdmin = computed(() => hasAdminDashboard(this.role()));
  readonly canManageWorkspace = computed(() => hasWorkspaceAdmin(this.role()));
  readonly navItems = computed<AdminNavItem[]>(() => visibleNavItems(this.role()));
  readonly firstPath = computed(() => firstAllowedPath(this.role()));

  ready(): boolean {
    return this.readySignal();
  }

  async ensureReady(): Promise<void> {
    if (this.readySignal()) {
      return;
    }
    if (!this.initPromise) {
      this.initPromise = this.init();
    }
    await this.initPromise;
  }

  canAccessArea(area: AdminAreaId): boolean {
    return canAccessAdminArea(this.role(), area);
  }

  canInvite(): boolean {
    const role = this.role();
    return !!role && MANAGER_ROLES.has(role);
  }

  private async init(): Promise<void> {
    this.currentUserId.set(this.auth.profile()?.id ?? null);
    try {
      const workspaces = await this.api.getWorkspaces();
      const managed =
        workspaces.find((w) => w.role && hasAdminDashboard(w.role)) ?? null;
      this.workspace.set(managed);
      this.loadError.set(false);
    } catch {
      this.workspace.set(null);
      this.loadError.set(true);
    } finally {
      this.readySignal.set(true);
    }
  }
}
