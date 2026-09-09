import { ui } from '../../core/i18n/strings';

/** B-106 — mirrors RolePermissionCatalog visibility for admin UI (hide, never warn). */

export type AdminAreaId =
  | 'overview'
  | 'members'
  | 'conversations'
  | 'audit'
  | 'settings'
  | 'plugins';

const WORKSPACE_ADMIN_ROLES = new Set(['PlatformOwner', 'WorkspaceOwner', 'Admin']);
const ADMIN_DASHBOARD_ROLES = new Set([...WORKSPACE_ADMIN_ROLES, 'Auditor']);

export interface AdminNavItem {
  id: AdminAreaId;
  label: string;
  path: string;
}

export const ADMIN_NAV: readonly AdminNavItem[] = [
  { id: 'overview', label: ui.adminNavOverview, path: 'overview' },
  { id: 'members', label: ui.adminNavMembers, path: 'members' },
  { id: 'conversations', label: ui.adminNavConversations, path: 'conversations' },
  { id: 'audit', label: ui.adminNavAudit, path: 'audit' },
  { id: 'settings', label: ui.adminNavSettings, path: 'settings' },
  { id: 'plugins', label: ui.adminNavPlugins, path: 'plugins' },
] as const;

export function hasAdminDashboard(role?: string | null): boolean {
  return !!role && ADMIN_DASHBOARD_ROLES.has(role);
}

export function hasWorkspaceAdmin(role?: string | null): boolean {
  return !!role && WORKSPACE_ADMIN_ROLES.has(role);
}

export function canAccessArea(role: string | undefined | null, area: AdminAreaId): boolean {
  if (!hasAdminDashboard(role)) {
    return false;
  }

  switch (area) {
    case 'overview':
    case 'members':
    case 'conversations':
    case 'audit':
      return hasAdminDashboard(role);
    case 'settings':
    case 'plugins':
      return hasWorkspaceAdmin(role);
    default:
      return false;
  }
}

export function visibleNavItems(role?: string | null): AdminNavItem[] {
  return ADMIN_NAV.filter((item) => canAccessArea(role, item.id));
}

export function firstAllowedPath(role?: string | null): string {
  return visibleNavItems(role)[0]?.path ?? 'overview';
}

export function areaTitle(area: AdminAreaId): string {
  return ADMIN_NAV.find((item) => item.id === area)?.label ?? ui.admin;
}
