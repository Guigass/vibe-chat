export type SystemEventKind = 'pin' | 'unpin';

export interface ParsedSystemEvent {
  kind: SystemEventKind;
  targetMessageId: string;
}

const PIN_PREFIX = '<system:pin:';
const UNPIN_PREFIX = '<system:unpin:';

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

  return null;
}

export function formatSystemEventLabel(
  authorName: string,
  event: ParsedSystemEvent,
): string {
  return event.kind === 'pin'
    ? `${authorName} fixou uma mensagem`
    : `${authorName} desafixou uma mensagem`;
}

export function isSystemEventBody(body: string): boolean {
  return parseSystemEventBody(body) !== null;
}
