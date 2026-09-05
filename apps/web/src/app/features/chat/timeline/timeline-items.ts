import { ChatMessage } from '../../../shared/models/chat.models';
import { isSystemEventBody, parseSystemEventBody, formatSystemEventLabel } from '../../../shared/system-event';
import { DEFAULT_LOCALE, type AppLocale } from '../../../core/i18n/locale';

export const GROUP_WINDOW_MS = 5 * 60 * 1000;

export type MessageGroupRole = 'start' | 'middle' | 'end' | 'single';

export type TimelineDayItem = {
  kind: 'day';
  id: string;
  label: string;
  ariaLabel: string;
  dateKey: string;
};

export type TimelineUnreadItem = {
  kind: 'unread';
  id: string;
  afterSeq: number;
};

export type TimelineMessageItem = {
  kind: 'message';
  id: string;
  message: ChatMessage;
  group: MessageGroupRole;
  showMeta: boolean;
  showAvatar: boolean;
};

export type TimelineStackItem = {
  kind: 'stack';
  id: string;
  mine: boolean;
  messages: TimelineMessageItem[];
};

export type TimelineSystemItem = {
  kind: 'system';
  id: string;
  label: string;
  createdAt: string;
};

export type TimelineItem =
  | TimelineDayItem
  | TimelineUnreadItem
  | TimelineStackItem
  | TimelineSystemItem;

export type BuildTimelineItemsOptions = {
  unreadCount?: number;
  dividerAfterSeq?: number | null;
  showUnreadDivider?: boolean;
  now?: Date;
  locale?: AppLocale;
};

function dateFormatters(locale: AppLocale) {
  return {
    dayMonth: new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long' }),
    full: new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', year: 'numeric' }),
    aria: new Intl.DateTimeFormat(locale, {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    }),
  };
}

export function localDateKey(iso: string, now = new Date()): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return localDateKeyFromDate(now);
  }
  return localDateKeyFromDate(date);
}

export function formatDayLabel(
  dateKey: string,
  now = new Date(),
  locale: AppLocale = DEFAULT_LOCALE,
): { label: string; ariaLabel: string } {
  const date = dateFromKey(dateKey);
  const todayKey = localDateKeyFromDate(now);
  const yesterday = new Date(now);
  yesterday.setDate(yesterday.getDate() - 1);
  const yesterdayKey = localDateKeyFromDate(yesterday);
  const formatters = dateFormatters(locale);
  const ariaLabel = formatters.aria.format(date);

  if (dateKey === todayKey) {
    return { label: locale === 'en' ? 'Today' : 'Hoje', ariaLabel };
  }
  if (dateKey === yesterdayKey) {
    return { label: locale === 'en' ? 'Yesterday' : 'Ontem', ariaLabel };
  }
  if (date.getFullYear() === now.getFullYear()) {
    return { label: formatters.dayMonth.format(date), ariaLabel };
  }
  return { label: formatters.full.format(date), ariaLabel };
}

export function unreadDividerAfterSeq(
  messages: readonly ChatMessage[],
  unreadCount: number,
): number | null {
  if (unreadCount <= 0 || messages.length === 0) return null;
  const sequenced = messages.filter((m) => (m.seq ?? 0) > 0);
  const source = sequenced.length > 0 ? sequenced : messages;
  const n = Math.min(unreadCount, source.length);
  const firstUnreadIndex = source.length - n;
  if (firstUnreadIndex <= 0) return 0;
  return source[firstUnreadIndex - 1].seq ?? 0;
}

export function groupRoleAt(messages: readonly ChatMessage[], index: number): MessageGroupRole {
  const current = messages[index];
  if (!current) return 'single';
  const prev = messages[index - 1];
  const next = messages[index + 1];
  const withPrev = !!prev && sameGroup(prev, current);
  const withNext = !!next && sameGroup(current, next);
  if (withPrev && withNext) return 'middle';
  if (withPrev) return 'end';
  if (withNext) return 'start';
  return 'single';
}

export function buildTimelineItems(
  messages: readonly ChatMessage[],
  options: BuildTimelineItemsOptions = {},
): TimelineItem[] {
  const now = options.now ?? new Date();
  const locale = options.locale ?? DEFAULT_LOCALE;
  const showUnread = options.showUnreadDivider !== false;
  const dividerAfterSeq =
    options.dividerAfterSeq !== undefined
      ? options.dividerAfterSeq
      : unreadDividerAfterSeq(messages, options.unreadCount ?? 0);

  const flat: Array<TimelineDayItem | TimelineUnreadItem | TimelineMessageItem | TimelineSystemItem> = [];
  let lastDateKey: string | null = null;
  let unreadInserted = false;

  for (let i = 0; i < messages.length; i++) {
    const message = messages[i];
    const dateKey = localDateKey(message.createdAt, now);
    if (dateKey !== lastDateKey) {
      const { label, ariaLabel } = formatDayLabel(dateKey, now, locale);
      flat.push({
        kind: 'day',
        id: `day-${dateKey}`,
        label,
        ariaLabel,
        dateKey,
      });
      lastDateKey = dateKey;
    }

    if (
      showUnread &&
      dividerAfterSeq !== null &&
      !unreadInserted &&
      isFirstUnread(message, dividerAfterSeq, i === 0)
    ) {
      flat.push({
        kind: 'unread',
        id: `unread-${dividerAfterSeq}`,
        afterSeq: dividerAfterSeq,
      });
      unreadInserted = true;
    }

    if (!message.deletedAt && isSystemEventBody(message.body)) {
      const parsed = parseSystemEventBody(message.body);
      if (parsed) {
        flat.push({
          kind: 'system',
          id: `system-${message.id}`,
          label: formatSystemEventLabel(message.authorName, parsed),
          createdAt: message.createdAt,
        });
        continue;
      }
    }

    const group = groupRoleAt(messages, i);
    const showMeta = group === 'start' || group === 'single';
    flat.push({
      kind: 'message',
      id: message.id,
      message,
      group,
      showMeta,
      showAvatar: showMeta,
    });
  }

  return coalesceMessageStacks(flat);
}

/** Merge consecutive message items into shared-bubble stacks; break on day/unread. */
export function coalesceMessageStacks(
  items: ReadonlyArray<TimelineDayItem | TimelineUnreadItem | TimelineMessageItem | TimelineSystemItem>,
): TimelineItem[] {
  const out: TimelineItem[] = [];
  let pending: TimelineMessageItem[] = [];

  const flush = (): void => {
    if (!pending.length) return;
    normalizeStackRoles(pending);
    out.push({
      kind: 'stack',
      id: `stack-${pending[0].id}`,
      mine: !!pending[0].message.mine,
      messages: pending,
    });
    pending = [];
  };

  for (const item of items) {
    if (item.kind !== 'message') {
      flush();
      out.push(item);
      continue;
    }
    if (item.group === 'start' || item.group === 'single') {
      flush();
      pending = [item];
      if (item.group === 'single') flush();
      continue;
    }
    // middle / end — attach, or promote if a divider broke the prior start
    if (!pending.length) {
      pending = [item];
    } else {
      pending.push(item);
    }
    if (item.group === 'end') flush();
  }
  flush();
  return out;
}

function normalizeStackRoles(messages: TimelineMessageItem[]): void {
  if (messages.length === 1) {
    messages[0].group = 'single';
    messages[0].showMeta = true;
    messages[0].showAvatar = true;
    return;
  }
  for (let i = 0; i < messages.length; i++) {
    const group: MessageGroupRole =
      i === 0 ? 'start' : i === messages.length - 1 ? 'end' : 'middle';
    messages[i].group = group;
    messages[i].showMeta = group === 'start';
    messages[i].showAvatar = group === 'start';
  }
}

function sameGroup(a: ChatMessage, b: ChatMessage): boolean {
  if (a.authorUserId !== b.authorUserId) return false;
  if (localDateKey(a.createdAt) !== localDateKey(b.createdAt)) return false;
  const aTime = Date.parse(a.createdAt);
  const bTime = Date.parse(b.createdAt);
  if (Number.isNaN(aTime) || Number.isNaN(bTime)) return false;
  return Math.abs(bTime - aTime) <= GROUP_WINDOW_MS;
}

function isFirstUnread(message: ChatMessage, dividerAfterSeq: number, isFirst: boolean): boolean {
  if (dividerAfterSeq === 0) return isFirst;
  return (message.seq ?? 0) > dividerAfterSeq;
}

function localDateKeyFromDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function dateFromKey(dateKey: string): Date {
  const [y, m, d] = dateKey.split('-').map(Number);
  return new Date(y, (m ?? 1) - 1, d ?? 1);
}
