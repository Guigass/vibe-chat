const PREFIX = 'vc.palette.recent.';
const MAX_ITEMS = 8;

export function recentPaletteKey(userId: string): string {
  return `${PREFIX}${userId}`;
}

export function readRecentPaletteIds(
  userId: string,
  storage: Storage | null = defaultStorage(),
): string[] {
  if (!userId || !storage) return [];
  try {
    const raw = storage.getItem(recentPaletteKey(userId));
    if (!raw) return [];
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed
      .filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
      .slice(0, MAX_ITEMS);
  } catch {
    return [];
  }
}

export function writeRecentPaletteId(
  userId: string,
  id: string,
  storage: Storage | null = defaultStorage(),
): string[] {
  const value = id.trim();
  if (!userId || !value || !storage) {
    return readRecentPaletteIds(userId, storage);
  }
  const next = [value, ...readRecentPaletteIds(userId, storage).filter((item) => item !== value)].slice(
    0,
    MAX_ITEMS,
  );
  try {
    storage.setItem(recentPaletteKey(userId), JSON.stringify(next));
  } catch {
    // ignore quota / private mode
  }
  return next;
}

function defaultStorage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
}
