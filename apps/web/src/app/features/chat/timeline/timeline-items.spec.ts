import { describe, expect, it } from 'vitest';
import type { ChatMessage } from '../../../shared/models/chat.models';
import {
  buildTimelineItems,
  formatDayLabel,
  groupRoleAt,
  unreadDividerAfterSeq,
} from './timeline-items';

const now = new Date(2026, 7, 10, 18, 0, 0);

function msg(overrides: Partial<ChatMessage> & Pick<ChatMessage, 'id' | 'seq'>): ChatMessage {
  return {
    conversationId: 'ch1',
    channelId: 'ch1',
    authorUserId: 'u-alice',
    authorName: 'Alice',
    body: overrides.body ?? overrides.id,
    createdAt: overrides.createdAt ?? localIso(2026, 8, 10, 12, 0),
    status: 'persisted',
    mine: false,
    ...overrides,
  };
}

function localIso(year: number, month: number, day: number, hour = 12, minute = 0): string {
  return new Date(year, month - 1, day, hour, minute, 0).toISOString();
}

describe('groupRoleAt', () => {
  it('groups three consecutive Alice messages within 1 minute as one block', () => {
    const messages = [
      msg({ id: 'a1', seq: 1, createdAt: new Date(2026, 7, 10, 12, 0, 0).toISOString() }),
      msg({ id: 'a2', seq: 2, createdAt: new Date(2026, 7, 10, 12, 0, 30).toISOString() }),
      msg({ id: 'a3', seq: 3, createdAt: new Date(2026, 7, 10, 12, 1, 0).toISOString() }),
    ];

    expect(groupRoleAt(messages, 0)).toBe('start');
    expect(groupRoleAt(messages, 1)).toBe('middle');
    expect(groupRoleAt(messages, 2)).toBe('end');
  });

  it('opens a new block when the next message is 6 minutes later', () => {
    const messages = [
      msg({ id: 'a1', seq: 1, createdAt: localIso(2026, 8, 10, 12, 0) }),
      msg({ id: 'a2', seq: 2, createdAt: localIso(2026, 8, 10, 12, 1) }),
      msg({ id: 'a3', seq: 3, createdAt: localIso(2026, 8, 10, 12, 7) }),
    ];
    expect(groupRoleAt(messages, 0)).toBe('start');
    expect(groupRoleAt(messages, 1)).toBe('end');
    expect(groupRoleAt(messages, 2)).toBe('single');
  });

  it('opens a new block across a local day boundary even inside the window', () => {
    const messages = [
      msg({ id: 'a1', seq: 1, createdAt: new Date(2026, 7, 9, 23, 58, 0).toISOString() }),
      msg({ id: 'a2', seq: 2, createdAt: new Date(2026, 7, 10, 0, 1, 0).toISOString() }),
    ];
    expect(groupRoleAt(messages, 0)).toBe('single');
    expect(groupRoleAt(messages, 1)).toBe('single');
  });

  it('splits when the author changes even inside the window', () => {
    const messages = [
      msg({ id: 'a1', seq: 1, createdAt: localIso(2026, 8, 10, 12, 0) }),
      msg({
        id: 'b1',
        seq: 2,
        authorUserId: 'u-bob',
        authorName: 'Bob',
        createdAt: localIso(2026, 8, 10, 12, 1),
      }),
    ];
    expect(groupRoleAt(messages, 0)).toBe('single');
    expect(groupRoleAt(messages, 1)).toBe('single');
  });
});

describe('formatDayLabel', () => {
  it('labels today, yesterday and a long date', () => {
    expect(formatDayLabel('2026-08-10', now).label).toBe('Hoje');
    expect(formatDayLabel('2026-08-09', now).label).toBe('Ontem');
    expect(formatDayLabel('2026-03-12', now).label).toBe('12 de março');
    expect(formatDayLabel('2025-03-12', now).label).toBe('12 de março de 2025');
    expect(formatDayLabel('2026-08-10', now, 'en').label).toBe('Today');
    expect(formatDayLabel('2026-03-12', now, 'en').label.toLowerCase()).toContain('march');
  });

  it('uses the full date as aria-label', () => {
    expect(formatDayLabel('2026-03-12', now).ariaLabel.toLowerCase()).toContain('12');
    expect(formatDayLabel('2026-03-12', now).ariaLabel.toLowerCase()).toContain('março');
  });
});

describe('unreadDividerAfterSeq', () => {
  it('places the divider before the last N sequenced messages', () => {
    const messages = Array.from({ length: 10 }, (_, i) =>
      msg({ id: `m${i + 1}`, seq: i + 1, createdAt: localIso(2026, 8, 10, 12, i) }),
    );
    expect(unreadDividerAfterSeq(messages, 5)).toBe(5);
    expect(unreadDividerAfterSeq(messages, 0)).toBeNull();
    expect(unreadDividerAfterSeq(messages, 10)).toBe(0);
    expect(unreadDividerAfterSeq(messages, 12)).toBe(0);
  });
});

describe('buildTimelineItems', () => {
  it('inserts day separators on local day change and groups Alice', () => {
    const messages = [
      msg({ id: 'a1', seq: 1, createdAt: localIso(2026, 8, 9, 23, 50) }),
      msg({ id: 'a2', seq: 2, createdAt: localIso(2026, 8, 10, 12, 0) }),
      msg({
        id: 'a3',
        seq: 3,
        createdAt: new Date(2026, 7, 10, 12, 0, 40).toISOString(),
      }),
      msg({ id: 'a4', seq: 4, createdAt: localIso(2026, 8, 10, 12, 1) }),
    ];

    const items = buildTimelineItems(messages, { now, unreadCount: 0 });
    const kinds = items.map((i) => i.kind);
    expect(kinds).toEqual(['day', 'stack', 'day', 'stack']);

    const days = items.filter((i) => i.kind === 'day');
    expect(days[0]?.kind === 'day' && days[0].label).toBe('Ontem');
    expect(days[1]?.kind === 'day' && days[1].label).toBe('Hoje');

    const stacks = items.filter((i) => i.kind === 'stack');
    expect(stacks).toHaveLength(2);
    expect(stacks[0]?.kind === 'stack' && stacks[0].messages.map((m) => m.group)).toEqual(['single']);
    expect(stacks[1]?.kind === 'stack' && stacks[1].messages.map((m) => m.group)).toEqual([
      'start',
      'middle',
      'end',
    ]);
    expect(stacks[1]?.kind === 'stack' && stacks[1].messages.map((m) => m.showMeta)).toEqual([
      true,
      false,
      false,
    ]);
  });

  it('inserts the unread divider before the first of 5 unread messages', () => {
    const messages = Array.from({ length: 8 }, (_, i) =>
      msg({ id: `m${i + 1}`, seq: i + 1, createdAt: localIso(2026, 8, 10, 10, i) }),
    );
    const items = buildTimelineItems(messages, { now, unreadCount: 5 });
    const unreadIndex = items.findIndex((i) => i.kind === 'unread');
    const firstUnreadStack = items[unreadIndex + 1];
    expect(unreadIndex).toBeGreaterThan(0);
    expect(items[unreadIndex]).toMatchObject({ kind: 'unread', afterSeq: 3 });
    expect(firstUnreadStack?.kind).toBe('stack');
    expect(firstUnreadStack?.kind === 'stack' && firstUnreadStack.messages[0]?.id).toBe('m4');
  });

  it('keeps a frozen dividerAfterSeq when newer messages append (BUG-016)', () => {
    const opened = Array.from({ length: 8 }, (_, i) =>
      msg({ id: `m${i + 1}`, seq: i + 1, createdAt: localIso(2026, 8, 10, 10, i) }),
    );
    const frozenAfterSeq = unreadDividerAfterSeq(opened, 5);
    expect(frozenAfterSeq).toBe(3);

    const withNew = [
      ...opened,
      msg({ id: 'm9', seq: 9, createdAt: localIso(2026, 8, 10, 10, 8) }),
      msg({ id: 'm10', seq: 10, createdAt: localIso(2026, 8, 10, 10, 9) }),
    ];
    const drifting = unreadDividerAfterSeq(withNew, 5);
    expect(drifting).toBe(5);

    const items = buildTimelineItems(withNew, {
      now,
      unreadCount: 5,
      dividerAfterSeq: frozenAfterSeq,
    });
    const unread = items.find((i) => i.kind === 'unread');
    expect(unread).toMatchObject({ kind: 'unread', afterSeq: 3 });
    const unreadIndex = items.findIndex((i) => i.kind === 'unread');
    const firstUnreadStack = items[unreadIndex + 1];
    expect(firstUnreadStack?.kind === 'stack' && firstUnreadStack.messages[0]?.id).toBe('m4');
  });

  it('omits the unread divider when dismissed', () => {
    const messages = [
      msg({ id: 'm1', seq: 1, createdAt: localIso(2026, 8, 10, 12, 0) }),
      msg({ id: 'm2', seq: 2, createdAt: localIso(2026, 8, 10, 12, 1) }),
    ];
    const items = buildTimelineItems(messages, {
      now,
      unreadCount: 2,
      showUnreadDivider: false,
    });
    expect(items.some((i) => i.kind === 'unread')).toBe(false);
  });
});
