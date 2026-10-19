import { describe, expect, it } from 'vitest';
import { ScheduleItem } from '../../shared/models/chat.models';
import { visibleScheduleItems } from './schedule.store';

function item(status: string): ScheduleItem {
  return {
    kind: 'scheduled_message',
    id: status,
    status,
    dueAtUtc: '2027-06-01T09:00:00Z',
    timeZone: 'UTC',
    body: 'editado',
    note: null,
    targetKind: null,
    channelId: 'c1',
    channelName: 'geral',
    messageId: null,
    threadId: null,
    sentMessageId: null,
    failureCode: null,
    canOpen: true,
    createdAt: '2026-10-19T11:00:00Z',
  };
}

describe('visibleScheduleItems', () => {
  it('drops cancelled rows so the panel does not keep them after cancel', () => {
    const visible = visibleScheduleItems([
      item('Pending'),
      item('Cancelled'),
      item('Failed'),
    ]);
    expect(visible.map((row) => row.status)).toEqual(['Pending', 'Failed']);
  });
});
