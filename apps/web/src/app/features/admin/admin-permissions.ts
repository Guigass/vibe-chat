import { ui } from '../../core/i18n/strings';

/** B-106 — mirrors RolePermissionCatalog visibility for admin UI (hide, never warn). */

export type AdminAreaId =
  | 'overview'
  | 'members'
  | 'conversations'
  | 'audit'
  | 'settings'
  | 'onboarding'
  | 'plugins'
  | 'automations'
  | 'connectors'
  | 'import'
  | 'diagnostics'
  | 'scim'
  | 'quarantine'
  | 'capacity'
  | 'developer'
  | 'branding'
  | 'storage';

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
  { id: 'onboarding', label: ui.adminNavOnboarding, path: 'onboarding' },
  { id: 'plugins', label: ui.adminNavPlugins, path: 'plugins' },
  { id: 'automations', label: ui.adminNavAutomations, path: 'automations' },
  { id: 'connectors', label: ui.adminNavConnectors, path: 'connectors' },
  { id: 'import', label: ui.adminNavImport, path: 'import' },
  { id: 'diagnostics', label: ui.adminNavDiagnostics, path: 'diagnostics' },
  { id: 'scim', label: ui.adminNavScim, path: 'scim' },
  { id: 'quarantine', label: ui.adminNavQuarantine, path: 'quarantine' },
  { id: 'capacity', label: ui.adminNavCapacity, path: 'capacity' },
  { id: 'developer', label: ui.adminNavDeveloper, path: 'developer' },
  { id: 'branding', label: ui.adminNavBranding, path: 'branding' },
  { id: 'storage', label: ui.adminNavStorage, path: 'storage' },
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
    case 'diagnostics':
      return hasAdminDashboard(role);
    case 'settings':
    case 'onboarding':
    case 'plugins':
    case 'automations':
    case 'connectors':
    case 'import':
    case 'scim':
    case 'quarantine':
    case 'capacity':
    case 'developer':
    case 'branding':
    case 'storage':
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
