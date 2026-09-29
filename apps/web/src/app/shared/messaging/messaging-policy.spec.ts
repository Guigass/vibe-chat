import { describe, expect, it } from 'vitest';
import {
  defaultMessagingPolicy,
  deleteLifecycle,
  editLifecycle,
  type MessagingPolicy,
} from './messaging-policy';

const now = Date.parse('2026-09-29T12:00:00.000Z');

function policy(overrides: Partial<MessagingPolicy> = {}): MessagingPolicy {
  return { ...defaultMessagingPolicy, ...overrides };
}

describe('messaging policy UI (B-107)', () => {
  it('keeps edit and delete for the author when the workspace has no extra rules', () => {
    const createdAt = new Date(now - 5 * 60_000).toISOString();
    expect(
      editLifecycle({ policy: defaultMessagingPolicy, role: 'Member', mine: true, createdAt, nowMs: now }),
    ).toBe('allow');
    expect(
      deleteLifecycle({ policy: defaultMessagingPolicy, role: 'Member', mine: true, createdAt, nowMs: now }),
    ).toBe('allow');
  });

  it('hides edit after the window and still allows a message inside it', () => {
    const rules = policy({ editWindowMinutes: 15 });
    expect(
      editLifecycle({
        policy: rules,
        role: 'Member',
        mine: true,
        createdAt: new Date(now - 16 * 60_000).toISOString(),
        nowMs: now,
      }),
    ).toBe('expired');
    expect(
      editLifecycle({
        policy: rules,
        role: 'Member',
        mine: true,
        createdAt: new Date(now - 14 * 60_000).toISOString(),
        nowMs: now,
      }),
    ).toBe('allow');
  });

  it('hides author edit when disabled and shows it for a moderator only with override', () => {
    const rules = policy({ editEnabled: false, editAllowModeratorOverride: true });
    expect(
      editLifecycle({
        policy: rules,
        role: 'Member',
        mine: true,
        createdAt: new Date(now).toISOString(),
        nowMs: now,
      }),
    ).toBe('hide');
    expect(
      editLifecycle({
        policy: rules,
        role: 'Moderator',
        mine: false,
        createdAt: new Date(now - 120 * 60_000).toISOString(),
        nowMs: now,
      }),
    ).toBe('allow');
    expect(
      editLifecycle({
        policy: policy({ editEnabled: false }),
        role: 'Moderator',
        mine: false,
        createdAt: new Date(now).toISOString(),
        nowMs: now,
      }),
    ).toBe('hide');
  });

  it('hides edit for Member when only Admin may edit', () => {
    const rules = policy({ editRolesRestricted: true, editRoles: ['Admin'] });
    expect(
      editLifecycle({
        policy: rules,
        role: 'Member',
        mine: true,
        createdAt: new Date(now).toISOString(),
        nowMs: now,
      }),
    ).toBe('hide');
    expect(
      editLifecycle({
        policy: rules,
        role: 'WorkspaceOwner',
        mine: true,
        createdAt: new Date(now).toISOString(),
        nowMs: now,
      }),
    ).toBe('allow');
  });

  it('hides edit and delete for a guest even on their own message', () => {
    const createdAt = new Date(now).toISOString();
    expect(
      editLifecycle({ policy: defaultMessagingPolicy, role: 'Guest', mine: true, createdAt, nowMs: now }),
    ).toBe('hide');
    expect(
      deleteLifecycle({ policy: defaultMessagingPolicy, role: 'Guest', mine: true, createdAt, nowMs: now }),
    ).toBe('hide');
  });

  it('mirrors the window and role rules for delete', () => {
    const rules = policy({
      deleteWindowMinutes: 15,
      deleteRolesRestricted: true,
      deleteRoles: ['Admin'],
      deleteAllowModeratorOverride: false,
    });
    expect(
      deleteLifecycle({
        policy: rules,
        role: 'Member',
        mine: true,
        createdAt: new Date(now).toISOString(),
        nowMs: now,
      }),
    ).toBe('hide');
    expect(
      deleteLifecycle({
        policy: policy({ deleteWindowMinutes: 15, deleteAllowModeratorOverride: false }),
        role: 'Admin',
        mine: true,
        createdAt: new Date(now - 16 * 60_000).toISOString(),
        nowMs: now,
      }),
    ).toBe('expired');
    expect(
      deleteLifecycle({
        policy: policy({ deleteAllowModeratorOverride: true }),
        role: 'Moderator',
        mine: false,
        createdAt: new Date(now - 16 * 60_000).toISOString(),
        nowMs: now,
      }),
    ).toBe('allow');
  });
});
