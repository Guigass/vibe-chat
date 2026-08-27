export type SearchAttachmentKind = 'image' | 'audio' | 'document';
export type SearchSort = 'relevance' | 'date';
export type SearchChipKey =
  | 'author'
  | 'channel'
  | 'from'
  | 'to'
  | 'hasAttachment'
  | 'hasLink'
  | 'attachmentKind';

export interface SearchChip {
  key: SearchChipKey;
  label: string;
  raw: string;
}

export interface SearchOperatorContext {
  op: 'de' | 'em' | 'antes' | 'depois' | 'tem';
  query: string;
}

export interface ParsedSearchQuery {
  term: string;
  authorToken?: string;
  channelToken?: string;
  from?: string;
  to?: string;
  hasAttachment?: boolean;
  hasLink?: boolean;
  attachmentKind?: SearchAttachmentKind;
  chips: SearchChip[];
  activeOperator: SearchOperatorContext | null;
}

const OPERATOR = /(^|\s)(de|em|antes|depois|desde|tem):(\S*)/gi;
const TAIL_OPERATOR = /(?:^|\s)(de|em|antes|depois|desde|tem):(\S*)$/i;

const TEM_VALUES: Record<string, { hasAttachment?: boolean; hasLink?: boolean; kind?: SearchAttachmentKind; label: string }> =
  {
    anexo: { hasAttachment: true, label: 'Com anexo' },
    attachment: { hasAttachment: true, label: 'Com anexo' },
    link: { hasLink: true, label: 'Com link' },
    imagem: { kind: 'image', label: 'Imagem' },
    image: { kind: 'image', label: 'Imagem' },
    audio: { kind: 'audio', label: 'Áudio' },
    documento: { kind: 'document', label: 'Documento' },
    document: { kind: 'document', label: 'Documento' },
    arquivo: { kind: 'document', label: 'Documento' },
  };

export function parseSearchQuery(input: string): ParsedSearchQuery {
  const chips: SearchChip[] = [];
  let authorToken: string | undefined;
  let channelToken: string | undefined;
  let from: string | undefined;
  let to: string | undefined;
  let hasAttachment: boolean | undefined;
  let hasLink: boolean | undefined;
  let attachmentKind: SearchAttachmentKind | undefined;
  const ranges: Array<{ start: number; end: number }> = [];

  for (const match of input.matchAll(OPERATOR)) {
    const prefix = match[1] ?? '';
    const op = (match[2] ?? '').toLowerCase();
    const value = match[3] ?? '';
    const start = (match.index ?? 0) + prefix.length;
    const end = start + op.length + 1 + value.length;
    if (!value) {
      continue;
    }

    ranges.push({ start, end });
    const raw = `${op}:${value}`;
    if (op === 'de') {
      authorToken = value;
      chips.push({ key: 'author', label: `de:${formatAuthor(value)}`, raw });
    } else if (op === 'em') {
      channelToken = value;
      chips.push({ key: 'channel', label: `em:${formatChannel(value)}`, raw });
    } else if (op === 'antes') {
      to = value;
      chips.push({ key: 'to', label: `antes:${value}`, raw });
    } else if (op === 'depois' || op === 'desde') {
      from = value;
      chips.push({ key: 'from', label: `depois:${value}`, raw });
    } else if (op === 'tem') {
      const mapped = TEM_VALUES[value.toLowerCase()];
      if (mapped?.hasAttachment) {
        hasAttachment = true;
        chips.push({ key: 'hasAttachment', label: mapped.label, raw });
      } else if (mapped?.hasLink) {
        hasLink = true;
        chips.push({ key: 'hasLink', label: mapped.label, raw });
      } else if (mapped?.kind) {
        attachmentKind = mapped.kind;
        chips.push({ key: 'attachmentKind', label: mapped.label, raw });
      }
    }
  }

  let term = input;
  for (const range of [...ranges].sort((a, b) => b.start - a.start)) {
    term = `${term.slice(0, range.start)}${term.slice(range.end)}`;
  }
  term = term.replace(/\s+/g, ' ').trim();

  const tail = input.match(TAIL_OPERATOR);
  let activeOperator: SearchOperatorContext | null = null;
  if (tail) {
    const rawOp = tail[1].toLowerCase();
    const op: SearchOperatorContext['op'] =
      rawOp === 'desde' ? 'depois' : (rawOp as SearchOperatorContext['op']);
    activeOperator = { op, query: tail[2] ?? '' };
  }

  return {
    term,
    authorToken,
    channelToken,
    from,
    to,
    hasAttachment,
    hasLink,
    attachmentKind,
    chips,
    activeOperator,
  };
}

export function serializeSearchQuery(parsed: Omit<ParsedSearchQuery, 'chips' | 'activeOperator'>): string {
  const parts: string[] = [];
  if (parsed.authorToken) parts.push(`de:${formatAuthor(parsed.authorToken)}`);
  if (parsed.channelToken) parts.push(`em:${formatChannel(parsed.channelToken)}`);
  if (parsed.from) parts.push(`depois:${parsed.from}`);
  if (parsed.to) parts.push(`antes:${parsed.to}`);
  if (parsed.hasAttachment) parts.push('tem:anexo');
  if (parsed.hasLink) parts.push('tem:link');
  if (parsed.attachmentKind === 'image') parts.push('tem:imagem');
  if (parsed.attachmentKind === 'audio') parts.push('tem:audio');
  if (parsed.attachmentKind === 'document') parts.push('tem:documento');
  if (parsed.term) parts.push(parsed.term);
  return parts.join(' ');
}

export function removeSearchChip(input: string, chip: SearchChip): string {
  const parsed = parseSearchQuery(input);
  return serializeSearchQuery({
    term: parsed.term,
    authorToken: chip.key === 'author' ? undefined : parsed.authorToken,
    channelToken: chip.key === 'channel' ? undefined : parsed.channelToken,
    from: chip.key === 'from' ? undefined : parsed.from,
    to: chip.key === 'to' ? undefined : parsed.to,
    hasAttachment: chip.key === 'hasAttachment' ? undefined : parsed.hasAttachment,
    hasLink: chip.key === 'hasLink' ? undefined : parsed.hasLink,
    attachmentKind: chip.key === 'attachmentKind' ? undefined : parsed.attachmentKind,
  });
}

export function applySearchOperator(input: string, op: SearchOperatorContext['op'], value: string): string {
  const cleaned = input.replace(TAIL_OPERATOR, '').trimEnd();
  const token =
    op === 'de' ? `de:${formatAuthor(value)}` :
    op === 'em' ? `em:${formatChannel(value)}` :
    `${op}:${value}`;
  return `${cleaned}${cleaned ? ' ' : ''}${token} `;
}

export function hasSearchFilter(parsed: ParsedSearchQuery): boolean {
  return !!(
    parsed.authorToken ||
    parsed.channelToken ||
    parsed.from ||
    parsed.to ||
    parsed.hasAttachment ||
    parsed.hasLink ||
    parsed.attachmentKind
  );
}

export function highlightSearchParts(text: string, term: string): Array<{ text: string; hit: boolean }> {
  const needle = term.trim();
  if (!needle) {
    return [{ text, hit: false }];
  }

  const escaped = needle.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const re = new RegExp(escaped, 'ig');
  const parts: Array<{ text: string; hit: boolean }> = [];
  let last = 0;
  for (const match of text.matchAll(re)) {
    const start = match.index ?? 0;
    if (start > last) {
      parts.push({ text: text.slice(last, start), hit: false });
    }
    parts.push({ text: match[0], hit: true });
    last = start + match[0].length;
  }
  if (last < text.length) {
    parts.push({ text: text.slice(last), hit: false });
  }
  return parts.length ? parts : [{ text, hit: false }];
}

function formatAuthor(value: string): string {
  const trimmed = value.replace(/^@/, '');
  return trimmed.startsWith('@') ? trimmed : `@${trimmed}`;
}

function formatChannel(value: string): string {
  const trimmed = value.replace(/^#/, '');
  return `#${trimmed}`;
}
