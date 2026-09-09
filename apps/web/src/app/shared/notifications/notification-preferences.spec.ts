import { describe, expect, it } from 'vitest';
import {
  mapChannelNotificationOverride,
  mapNotificationPreferences,
} from './notification-preferences';

describe('mapNotificationPreferences', () => {
  it('maps camelCase HTTP payloads', () => {
    const mapped = mapNotificationPreferences({
      level: 'All',
      hidePreview: true,
      dndEnabled: true,
      dndStart: '20:00:00',
      dndEnd: '08:00:00',
      dndDays: 3,
      timeZone: 'America/Sao_Paulo',
      digestEnabled: true,
      priorityContactUserIds: ['bob'],
      channelOverrides: [{ channelId: 'c1', level: 'None', mutedUntil: '2026-09-10T00:00:00Z' }],
    });

    expect(mapped.level).toBe('All');
    expect(mapped.hidePreview).toBe(true);
    expect(mapped.dndStart).toBe('20:00:00');
    expect(mapped.priorityContactUserIds).toEqual(['bob']);
    expect(mapped.channelOverrides).toEqual([
      { channelId: 'c1', level: 'None', mutedUntil: '2026-09-10T00:00:00Z' },
    ]);
  });

  it('maps PascalCase .NET records without wiping fields', () => {
    const mapped = mapNotificationPreferences({
      Level: 'None',
      HidePreview: true,
      DndEnabled: true,
      DndStart: '22:30:00',
      DndEnd: '07:15:00',
      DndDays: 127,
      TimeZone: 'UTC',
      DigestEnabled: false,
      PriorityContactUserIds: ['ALICE-ID'],
      ChannelOverrides: [{ ChannelId: 'c2', Level: 'All', MutedUntil: null }],
    });

    expect(mapped.level).toBe('None');
    expect(mapped.hidePreview).toBe(true);
    expect(mapped.dndEnabled).toBe(true);
    expect(mapped.dndStart).toBe('22:30:00');
    expect(mapped.dndEnd).toBe('07:15:00');
    expect(mapped.dndDays).toBe(127);
    expect(mapped.timeZone).toBe('UTC');
    expect(mapped.priorityContactUserIds).toEqual(['ALICE-ID']);
    expect(mapped.channelOverrides).toEqual([{ channelId: 'c2', level: 'All', mutedUntil: null }]);
  });

  it('falls back to MentionsAndDms when the payload is empty or the level is unknown', () => {
    expect(mapNotificationPreferences(null).level).toBe('MentionsAndDms');
    expect(mapNotificationPreferences({ Level: 'Loud' }).level).toBe('MentionsAndDms');
    expect(mapNotificationPreferences({}).channelOverrides).toEqual([]);
  });
});

describe('mapChannelNotificationOverride', () => {
  it('reads ChannelId / Level from either casing', () => {
    expect(mapChannelNotificationOverride({ ChannelId: 'c9', Level: 'None' })).toEqual({
      channelId: 'c9',
      level: 'None',
      mutedUntil: null,
    });
    expect(mapChannelNotificationOverride({ channelId: 'c9', level: 'All' })?.level).toBe('All');
  });

  it('returns null without a channel id', () => {
    expect(mapChannelNotificationOverride({ Level: 'None' })).toBeNull();
  });
});
