export interface MessagingPolicyInput {
  editEnabled: boolean;
  editWindowMinutes: number | null;
  clearEditWindow: boolean;
  editRoles: string[];
  editAllowModeratorOverride: boolean;
  deleteEnabled: boolean;
  deleteWindowMinutes: number | null;
  clearDeleteWindow: boolean;
  deleteRoles: string[];
  deleteAllowModeratorOverride: boolean;
}

export interface MessagingPolicy {
  editEnabled: boolean;
  editWindowMinutes: number | null;
  editRolesRestricted: boolean;
  editRoles: string[];
  editAllowModeratorOverride: boolean;
  deleteEnabled: boolean;
  deleteWindowMinutes: number | null;
  deleteRolesRestricted: boolean;
  deleteRoles: string[];
  deleteAllowModeratorOverride: boolean;
}

/** Matches the server default when no row exists (B-107). */
export const defaultMessagingPolicy: MessagingPolicy = {
  editEnabled: true,
  editWindowMinutes: null,
  editRolesRestricted: false,
  editRoles: ['Member', 'Moderator', 'Admin'],
  editAllowModeratorOverride: false,
  deleteEnabled: true,
  deleteWindowMinutes: null,
  deleteRolesRestricted: false,
  deleteRoles: ['Member', 'Moderator', 'Admin'],
  deleteAllowModeratorOverride: true,
};

const overrideRoles = new Set(['moderator', 'admin', 'workspaceowner', 'platformowner']);

export type LifecycleUi = 'allow' | 'hide' | 'expired';

export function messagingPolicyOf(source: {
  messagingPolicy?: () => MessagingPolicy | null | undefined;
}): MessagingPolicy {
  return source.messagingPolicy?.() ?? defaultMessagingPolicy;
}

export function editLifecycle(input: {
  policy: MessagingPolicy;
  role?: string | null;
  mine: boolean;
  createdAt: string;
  nowMs: number;
}): LifecycleUi {
  return lifecycle({
    enabled: input.policy.editEnabled,
    windowMinutes: input.policy.editWindowMinutes,
    restricted: input.policy.editRolesRestricted,
    roles: input.policy.editRoles,
    allowOverride: input.policy.editAllowModeratorOverride,
    role: input.role,
    mine: input.mine,
    createdAt: input.createdAt,
    nowMs: input.nowMs,
  });
}

export function deleteLifecycle(input: {
  policy: MessagingPolicy;
  role?: string | null;
  mine: boolean;
  createdAt: string;
  nowMs: number;
}): LifecycleUi {
  return lifecycle({
    enabled: input.policy.deleteEnabled,
    windowMinutes: input.policy.deleteWindowMinutes,
    restricted: input.policy.deleteRolesRestricted,
    roles: input.policy.deleteRoles,
    allowOverride: input.policy.deleteAllowModeratorOverride,
    role: input.role,
    mine: input.mine,
    createdAt: input.createdAt,
    nowMs: input.nowMs,
  });
}

function lifecycle(input: {
  enabled: boolean;
  windowMinutes: number | null;
  restricted: boolean;
  roles: string[];
  allowOverride: boolean;
  role?: string | null;
  mine: boolean;
  createdAt: string;
  nowMs: number;
}): LifecycleUi {
  const role = (input.role ?? '').toLowerCase();
  // Guest has no message.edit.own / message.delete.own. The policy cannot grant it.
  if (role === 'guest') return 'hide';
  if (!input.mine) {
    return input.allowOverride && overrideRoles.has(role) ? 'allow' : 'hide';
  }
  if (!input.enabled) return 'hide';
  if (input.restricted && !roleAllowed(input.roles, role)) return 'hide';
  if (windowExpired(input.windowMinutes, input.createdAt, input.nowMs)) return 'expired';
  return 'allow';
}

function roleAllowed(roles: string[], role: string): boolean {
  if (roles.some((item) => item.toLowerCase() === role)) return true;
  return (
    (role === 'workspaceowner' || role === 'platformowner') &&
    roles.some((item) => item.toLowerCase() === 'admin')
  );
}

function windowExpired(minutes: number | null, createdAt: string, nowMs: number): boolean {
  if (minutes == null) return false;
  const created = Date.parse(createdAt);
  if (Number.isNaN(created)) return false;
  return nowMs > created + minutes * 60_000;
}
