import {
  ChannelNotificationOverride,
  NotificationLevel,
  NotificationPreferences,
} from '../models/chat.models';

const LEVELS: readonly NotificationLevel[] = ['All', 'MentionsAndDms', 'None'];

export function emptyNotificationPreferences(): NotificationPreferences {
  return {
    level: 'MentionsAndDms',
    hidePreview: false,
    dndEnabled: false,
    dndStart: null,
    dndEnd: null,
    dndDays: 0,
    timeZone: null,
    digestEnabled: false,
    priorityContactUserIds: [],
    channelOverrides: [],
  };
}

/** HTTP is camelCase; records .NET may still arrive PascalCase. */
export function mapNotificationPreferences(raw: unknown): NotificationPreferences {
  const fallback = emptyNotificationPreferences();
  if (!raw || typeof raw !== 'object') {
    return fallback;
  }
  const source = raw as Record<string, unknown>;
  const overridesRaw = readValue(source, 'channelOverrides', 'ChannelOverrides');
  return {
    level: readLevel(readValue(source, 'level', 'Level')),
    hidePreview: !!readValue(source, 'hidePreview', 'HidePreview'),
    dndEnabled: !!readValue(source, 'dndEnabled', 'DndEnabled'),
    dndStart: readTime(readValue(source, 'dndStart', 'DndStart')),
    dndEnd: readTime(readValue(source, 'dndEnd', 'DndEnd')),
    dndDays: Number(readValue(source, 'dndDays', 'DndDays') ?? 0) || 0,
    timeZone: readString(source, 'timeZone', 'TimeZone') || null,
    digestEnabled: !!readValue(source, 'digestEnabled', 'DigestEnabled'),
    priorityContactUserIds: readIdList(readValue(source, 'priorityContactUserIds', 'PriorityContactUserIds')),
    channelOverrides: Array.isArray(overridesRaw)
      ? overridesRaw
          .map((item) => mapChannelNotificationOverride(item))
          .filter((item): item is ChannelNotificationOverride => item !== null)
      : [],
  };
}

export function mapChannelNotificationOverride(raw: unknown): ChannelNotificationOverride | null {
  if (!raw || typeof raw !== 'object') {
    return null;
  }
  const source = raw as Record<string, unknown>;
  const channelId = readString(source, 'channelId', 'ChannelId');
  if (!channelId) {
    return null;
  }
  return {
    channelId,
    level: readLevel(readValue(source, 'level', 'Level')),
    mutedUntil: readTime(readValue(source, 'mutedUntil', 'MutedUntil')),
  };
}

function readLevel(value: unknown): NotificationLevel {
  return LEVELS.includes(value as NotificationLevel) ? (value as NotificationLevel) : 'MentionsAndDms';
}

function readIdList(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value.map((item) => String(item)).filter((item) => item.length > 0);
}

function readTime(value: unknown): string | null {
  if (value == null || value === '') {
    return null;
  }
  return String(value);
}

function readValue(source: Record<string, unknown>, camel: string, pascal: string): unknown {
  return source[camel] ?? source[pascal];
}

function readString(source: Record<string, unknown>, camel: string, pascal: string): string {
  const value = readValue(source, camel, pascal);
  return value == null ? '' : String(value);
}
