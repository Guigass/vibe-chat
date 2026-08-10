export interface EmojiCategory {
  id: string;
  labelPt: string;
  labelEn: string;
  emojis: string[];
}

export interface EmojiCatalog {
  categories: EmojiCategory[];
  names: Record<string, { pt: string[]; en: string[] }>;
}

export type EmojiLocale = 'pt' | 'en';

const RECENT_STORAGE_KEY = 'vibechat.emoji.recent';
const MAX_RECENT = 24;

let catalogPromise: Promise<EmojiCatalog> | null = null;

export function loadEmojiCatalog(): Promise<EmojiCatalog> {
  if (!catalogPromise) {
    catalogPromise = fetch('/emoji/catalog.json')
      .then((response) => {
        if (!response.ok) {
          throw new Error('Failed to load emoji catalog');
        }
        return response.json() as Promise<EmojiCatalog>;
      })
      .catch(() => ({
        categories: [],
        names: {},
      }));
  }
  return catalogPromise;
}

export function readRecentEmojis(): string[] {
  try {
    const raw = localStorage.getItem(RECENT_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((item): item is string => typeof item === 'string').slice(0, MAX_RECENT);
  } catch {
    return [];
  }
}

export function rememberRecentEmoji(emoji: string): void {
  if (!emoji) return;
  const current = readRecentEmojis().filter((item) => item !== emoji);
  current.unshift(emoji);
  try {
    localStorage.setItem(RECENT_STORAGE_KEY, JSON.stringify(current.slice(0, MAX_RECENT)));
  } catch {
    // ignore quota errors
  }
}

export function searchEmojis(
  catalog: EmojiCatalog,
  query: string,
  locale: EmojiLocale,
): string[] {
  const normalized = query.trim().toLowerCase();
  if (!normalized) return [];

  const results = new Set<string>();
  for (const [emoji, names] of Object.entries(catalog.names)) {
    const labels = locale === 'pt' ? names.pt : names.en;
    if (labels.some((label) => label.toLowerCase().includes(normalized))) {
      results.add(emoji);
    }
  }

  for (const category of catalog.categories) {
    for (const emoji of category.emojis) {
      if (emoji.includes(normalized)) {
        results.add(emoji);
      }
    }
  }

  return [...results];
}

export function categoryLabel(category: EmojiCategory, locale: EmojiLocale): string {
  return locale === 'pt' ? category.labelPt : category.labelEn;
}
