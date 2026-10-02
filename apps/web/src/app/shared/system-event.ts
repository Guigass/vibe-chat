import { fillTemplate, ui } from '../core/i18n/strings';

export type SystemEventKind = 'pin' | 'unpin' | 'member-add' | 'member-remove' | 'member-leave';

export interface ParsedSystemEvent {
  kind: SystemEventKind;
  targetMessageId?: string;
  targetUserId?: string;
}

const PIN_PREFIX = '<system:pin:';
const UNPIN_PREFIX = '<system:unpin:';
const MEMBER_ADD_PREFIX = '<system:member-add:';
const MEMBER_REMOVE_PREFIX = '<system:member-remove:';
const MEMBER_LEAVE_PREFIX = '<system:member-leave:';

export function parseSystemEventBody(body: string): ParsedSystemEvent | null {
  if (!body) return null;

  if (body.startsWith(PIN_PREFIX) && body.endsWith('>')) {
    const raw = body.slice(PIN_PREFIX.length, -1);
    if (raw) return { kind: 'pin', targetMessageId: raw };
  }

  if (body.startsWith(UNPIN_PREFIX) && body.endsWith('>')) {
    const raw = body.slice(UNPIN_PREFIX.length, -1);
    if (raw) return { kind: 'unpin', targetMessageId: raw };
  }

  const member = parseMember(body, MEMBER_ADD_PREFIX, 'member-add')
    ?? parseMember(body, MEMBER_REMOVE_PREFIX, 'member-remove')
    ?? parseMember(body, MEMBER_LEAVE_PREFIX, 'member-leave');
  return member;
}

function parseMember(
  body: string,
  prefix: string,
  kind: 'member-add' | 'member-remove' | 'member-leave',
): ParsedSystemEvent | null {
  if (!body.startsWith(prefix) || !body.endsWith('>')) return null;
  const raw = body.slice(prefix.length, -1);
  return raw ? { kind, targetUserId: raw } : null;
}

export function formatSystemEventLabel(
  authorName: string,
  event: ParsedSystemEvent,
): string {
  const template = event.kind === 'pin'
    ? ui.systemPinned
    : event.kind === 'unpin'
      ? ui.systemUnpinned
      : event.kind === 'member-add'
        ? ui.systemMemberAdded
        : event.kind === 'member-remove'
          ? ui.systemMemberRemoved
          : ui.systemMemberLeft;
  return fillTemplate(template, { name: authorName });
}

export function isSystemEventBody(body: string): boolean {
  return parseSystemEventBody(body) !== null;
}
