export type PaletteKind = 'recent' | 'channel' | 'person' | 'action';

export type PaletteAction =
  | { type: 'channel'; channelId: string }
  | { type: 'person'; userId: string }
  | { type: 'slash'; name: string }
  | { type: 'saved' }
  | { type: 'admin' }
  | { type: 'theme' }
  | { type: 'density' }
  | { type: 'shortcuts' }
  | { type: 'search' }
  | { type: 'mentions' }
  | { type: 'mark-read' };

export interface PaletteItem {
  id: string;
  kind: Exclude<PaletteKind, 'recent'>;
  label: string;
  hint?: string;
  shortcut?: string;
  keywords: string[];
  action: PaletteAction;
}

export interface RankedPaletteItem extends PaletteItem {
  score: number;
  recent: boolean;
}

export interface PaletteGroup {
  kind: PaletteKind;
  title: string;
  items: RankedPaletteItem[];
}

export const PALETTE_GROUP_TITLES: Record<PaletteKind, string> = {
  recent: 'Recentes',
  channel: 'Canais',
  person: 'Pessoas',
  action: 'Ações',
};

export const SHORTCUT_SHEET: ReadonlyArray<{ combo: string; action: string }> = [
  { combo: 'Ctrl/Cmd+K', action: 'Abrir a paleta de comandos' },
  { combo: 'Ctrl/Cmd+Shift+F', action: 'Buscar mensagens com filtros' },
  { combo: 'Alt+↑ / Alt+↓', action: 'Canal anterior / próximo' },
  { combo: 'Alt+Shift+↑ / Alt+Shift+↓', action: 'Canal não lido anterior / próximo' },
  { combo: 'Esc', action: 'Fechar painel ou cancelar edição' },
  { combo: '↑', action: 'Editar a última mensagem própria no composer vazio' },
  { combo: 'Ctrl/Cmd+Shift+M', action: 'Ir para a próxima menção' },
  { combo: 'Shift+Esc', action: 'Marcar o canal atual como lido' },
  { combo: '?', action: 'Abrir esta folha de atalhos' },
];

export type GlobalShortcut =
  | 'palette'
  | 'search'
  | 'channel-prev'
  | 'channel-next'
  | 'unread-prev'
  | 'unread-next'
  | 'mentions'
  | 'mark-read'
  | 'shortcuts';

export function fuzzyScore(query: string, text: string): number {
  const q = query.trim().toLowerCase();
  const t = text.toLowerCase();
  if (!q) return 1;
  if (t === q) return 100;
  if (t.startsWith(q)) return 80;
  if (t.includes(q)) return 60;

  let qi = 0;
  for (const ch of t) {
    if (ch === q[qi]) qi += 1;
    if (qi === q.length) return 30;
  }
  return 0;
}

export function scorePaletteItem(item: PaletteItem, query: string): number {
  const fields = [item.label, item.hint ?? '', ...item.keywords];
  return fields.reduce((best, field) => Math.max(best, fuzzyScore(query, field)), 0);
}

export function rankPaletteItems(
  items: PaletteItem[],
  query: string,
  recentIds: readonly string[],
): PaletteGroup[] {
  const recentSet = new Set(recentIds);
  const ranked: RankedPaletteItem[] = [];

  for (const item of items) {
    const base = scorePaletteItem(item, query);
    if (base <= 0) continue;
    const recent = recentSet.has(item.id);
    ranked.push({
      ...item,
      recent,
      score: recent ? base + 15 : base,
    });
  }

  ranked.sort((a, b) => b.score - a.score || a.label.localeCompare(b.label));

  const groups: PaletteGroup[] = [];
  const emptyQuery = query.trim().length === 0;
  if (emptyQuery) {
    const recents = recentIds
      .map((id) => ranked.find((item) => item.id === id))
      .filter((item): item is RankedPaletteItem => !!item);
    if (recents.length) {
      groups.push({ kind: 'recent', title: PALETTE_GROUP_TITLES.recent, items: recents });
    }
  }

  for (const kind of ['channel', 'person', 'action'] as const) {
    const itemsOfKind = ranked.filter((item) => item.kind === kind);
    if (!itemsOfKind.length) continue;
    groups.push({ kind, title: PALETTE_GROUP_TITLES[kind], items: itemsOfKind });
  }

  return groups;
}

export function flattenPaletteItems(groups: PaletteGroup[]): RankedPaletteItem[] {
  const seen = new Set<string>();
  const flat: RankedPaletteItem[] = [];
  for (const group of groups) {
    for (const item of group.items) {
      if (seen.has(item.id)) continue;
      seen.add(item.id);
      flat.push(item);
    }
  }
  return flat;
}

export function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  if (target.isContentEditable) return true;
  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
}

export function matchGlobalShortcut(
  event: KeyboardEvent,
  inEditable: boolean,
): GlobalShortcut | null {
  const key = event.key;
  const lower = key.length === 1 ? key.toLowerCase() : key;
  const mod = event.ctrlKey || event.metaKey;

  if (mod && !event.altKey && lower === 'k' && !event.shiftKey) return 'palette';
  if (mod && !event.altKey && lower === 'f' && event.shiftKey) return 'search';
  if (mod && !event.altKey && lower === 'm' && event.shiftKey) return 'mentions';
  if (key === 'Escape' && event.shiftKey && !mod && !event.altKey) return 'mark-read';

  if (inEditable) return null;

  if (event.altKey && !mod && (key === 'ArrowUp' || key === 'ArrowDown')) {
    if (event.shiftKey) return key === 'ArrowUp' ? 'unread-prev' : 'unread-next';
    return key === 'ArrowUp' ? 'channel-prev' : 'channel-next';
  }

  if (!mod && !event.altKey && !event.shiftKey && key === '?') return 'shortcuts';
  return null;
}

export interface NavigableChannel {
  id: string;
  unreadCount: number;
  mentionCount?: number;
}

export function cycleChannel(
  channels: readonly NavigableChannel[],
  currentId: string | null,
  direction: 1 | -1,
  filter: 'all' | 'unread' | 'mention',
): NavigableChannel | null {
  const list =
    filter === 'all'
      ? [...channels]
      : channels.filter((channel) =>
          filter === 'mention'
            ? (channel.mentionCount ?? 0) > 0
            : channel.unreadCount > 0 || (channel.mentionCount ?? 0) > 0,
        );
  if (!list.length) return null;

  const currentIndex = currentId ? list.findIndex((channel) => channel.id === currentId) : -1;
  if (currentIndex < 0) return list[direction === 1 ? 0 : list.length - 1] ?? null;
  const next = (currentIndex + direction + list.length) % list.length;
  return list[next] ?? null;
}

export const SLASH_COMMANDS_NEEDING_ARGS = new Set(['dm', 'topico', 'convidar']);
