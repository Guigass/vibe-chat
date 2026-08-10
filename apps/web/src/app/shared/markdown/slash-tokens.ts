export interface SlashCommandDef {
  name: string;
  description: string;
  usage: string;
  permission?: string | null;
}

export interface SlashQueryContext {
  query: string;
  slashIndex: number;
}

export interface ParsedSlashCommand {
  name: string;
  argsRaw: string;
}

/** Opens slash autocomplete only when `/` is at the start of the message and the cursor is still in the command token. */
export function detectSlashQuery(text: string, cursor: number): SlashQueryContext | null {
  if (!text.startsWith('/')) return null;

  const before = text.slice(0, cursor);
  if (!before.startsWith('/')) return null;

  const afterSlash = before.slice(1);
  if (/\s/.test(afterSlash)) return null;

  return { query: afterSlash, slashIndex: 0 };
}

export function parseSlashCommand(text: string): ParsedSlashCommand | null {
  const trimmed = text.trim();
  if (!trimmed.startsWith('/')) return null;

  const body = trimmed.slice(1);
  if (!body) return { name: '', argsRaw: '' };

  const space = body.search(/\s/);
  if (space < 0) {
    return { name: body.toLowerCase(), argsRaw: '' };
  }

  return {
    name: body.slice(0, space).toLowerCase(),
    argsRaw: body.slice(space + 1).trim(),
  };
}

export function filterSlashCommands(
  items: SlashCommandDef[],
  query: string,
  limit = 8,
): SlashCommandDef[] {
  const q = query.trim().toLowerCase();
  const filtered = q
    ? items.filter(
        (item) =>
          item.name.startsWith(q) ||
          item.description.toLowerCase().includes(q) ||
          item.usage.toLowerCase().includes(q),
      )
    : items;
  return filtered.slice(0, limit);
}

export function insertSlashCommand(
  text: string,
  commandName: string,
): { value: string; cursor: number } {
  const rest = text.includes(' ') ? text.slice(text.indexOf(' ')) : '';
  const value = `/${commandName}${rest.startsWith(' ') ? rest : ' '}`;
  return { value, cursor: value.length };
}

export function looksLikeSlashCommand(text: string): boolean {
  return text.trimStart().startsWith('/');
}
