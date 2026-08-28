const PREFIX = 'vc.search.recent.';
const MAX_ITEMS = 8;

export function recentSearchKey(userId: string): string {
  return `${PREFIX}${userId}`;
}

export function readRecentSearches(userId: string, storage: Storage | null = defaultStorage()): string[] {
  if (!userId || !storage) {
    return [];
  }

  try {
    const raw = storage.getItem(recentSearchKey(userId));
    if (!raw) {
      return [];
    }
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) {
      return [];
    }
    return parsed.filter((item): item is string => typeof item === 'string' && item.trim().length > 0).slice(0, MAX_ITEMS);
  } catch {
    return [];
  }
}

export function writeRecentSearch(
  userId: string,
  query: string,
  storage: Storage | null = defaultStorage(),
): string[] {
  const term = query.trim();
  if (!userId || !term || !storage) {
    return readRecentSearches(userId, storage);
  }

  const next = [term, ...readRecentSearches(userId, storage).filter((item) => item !== term)].slice(0, MAX_ITEMS);
  try {
    storage.setItem(recentSearchKey(userId), JSON.stringify(next));
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
