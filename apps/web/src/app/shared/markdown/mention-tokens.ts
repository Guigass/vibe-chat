export type MentionSpecial = 'here' | 'channel';

export interface MentionTokenMatch {
  kind: 'user' | MentionSpecial;
  userId?: string;
  raw: string;
  start: number;
  end: number;
}

const MENTION_TOKEN =
  /<@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|here|channel)>/g;

export function userMentionToken(userId: string): string {
  return `<@${userId}>`;
}

export function specialMentionToken(kind: MentionSpecial): string {
  return kind === 'here' ? '<@here>' : '<@channel>';
}

export function parseMentionTokens(source: string): MentionTokenMatch[] {
  const matches: MentionTokenMatch[] = [];
  if (!source) return matches;

  for (const match of source.matchAll(MENTION_TOKEN)) {
    const raw = match[0];
    const value = match[1];
    const start = match.index ?? 0;
    if (!value) continue;

    if (value === 'here' || value === 'channel') {
      matches.push({ kind: value, raw, start, end: start + raw.length });
      continue;
    }

    matches.push({ kind: 'user', userId: value, raw, start, end: start + raw.length });
  }

  return matches;
}

export interface MentionQueryContext {
  query: string;
  atIndex: number;
}

export function detectMentionQuery(text: string, cursor: number): MentionQueryContext | null {
  const before = text.slice(0, cursor);
  const atIndex = before.lastIndexOf('@');
  if (atIndex < 0) return null;

  const prev = atIndex > 0 ? before[atIndex - 1] : ' ';
  if (prev.trim() && !/[\s([{'"`]/.test(prev)) {
    return null;
  }

  const query = before.slice(atIndex + 1);
  if (/[\s]/.test(query)) return null;
  return { query, atIndex };
}

export function insertMentionToken(
  text: string,
  atIndex: number,
  queryLength: number,
  token: string,
): { value: string; cursor: number } {
  const start = atIndex;
  const end = atIndex + 1 + queryLength;
  const value = `${text.slice(0, start)}${token} ${text.slice(end)}`;
  const cursor = start + token.length + 1;
  return { value, cursor };
}

export function mentionLabel(
  token: MentionTokenMatch,
  labels: Record<string, string>,
): string {
  if (token.kind === 'here') return '@aqui';
  if (token.kind === 'channel') return '@canal';
  return `@${labels[token.userId ?? ''] ?? 'usuário'}`;
}

export function formatMentionPlainText(
  source: string,
  labels: Record<string, string>,
): string {
  const tokens = parseMentionTokens(source);
  if (tokens.length === 0) return source ?? '';

  let result = '';
  let cursor = 0;
  for (const token of tokens) {
    result += source.slice(cursor, token.start) + mentionLabel(token, labels);
    cursor = token.end;
  }
  return result + source.slice(cursor);
}

export interface MentionAutocompleteItem {
  kind: 'user' | MentionSpecial;
  userId?: string;
  displayName: string;
  email?: string;
  subtitle?: string;
}

export function filterMentionItems(
  items: MentionAutocompleteItem[],
  query: string,
  limit = 8,
): MentionAutocompleteItem[] {
  const q = query.trim().toLowerCase();
  const specials = items.filter((item) => item.kind !== 'user');
  const users = items.filter((item) => item.kind === 'user');

  const filteredUsers = q
    ? users.filter(
        (item) =>
          item.displayName.toLowerCase().includes(q) ||
          (item.email?.toLowerCase().includes(q) ?? false),
      )
    : users;

  const showSpecials = !q || 'aqui'.startsWith(q) || 'canal'.startsWith(q);
  const visibleSpecials = showSpecials ? specials : [];
  return [...visibleSpecials, ...filteredUsers].slice(0, limit);
}
