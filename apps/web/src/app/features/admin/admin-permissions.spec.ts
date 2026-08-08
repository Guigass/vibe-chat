import { describe, expect, it } from 'vitest';
import {
  canAccessArea,
  firstAllowedPath,
  hasAdminDashboard,
  hasWorkspaceAdmin,
  visibleNavItems,
} from './admin-permissions';

describe('admin-permissions (B-106)', () => {
  it('Admin has full nav including settings and plugins', () => {
    const items = visibleNavItems('Admin');
    expect(items.map((i) => i.id)).toEqual([
      'overview',
      'members',
      'conversations',
      'audit',
      'settings',
      'plugins',
    ]);
    expect(hasWorkspaceAdmin('Admin')).toBe(true);
  });

  it('Auditor sees dashboard areas but not settings or plugins', () => {
    const items = visibleNavItems('Auditor');
    expect(items.map((i) => i.id)).toEqual(['overview', 'members', 'conversations', 'audit']);
    expect(canAccessArea('Auditor', 'settings')).toBe(false);
    expect(firstAllowedPath('Auditor')).toBe('overview');
  });

  it('Member cannot access admin dashboard', () => {
    expect(hasAdminDashboard('Member')).toBe(false);
    expect(visibleNavItems('Member')).toEqual([]);
  });
});
