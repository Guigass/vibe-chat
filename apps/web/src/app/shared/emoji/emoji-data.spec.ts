import { describe, expect, it, vi } from 'vitest';
import { type EmojiCatalog, readRecentEmojis, rememberRecentEmoji, searchEmojis } from './emoji-data';

const catalog: EmojiCatalog = {
  categories: [
    {
      id: 'travel',
      labelPt: 'Viagem',
      labelEn: 'Travel',
      emojis: ['🚀', '✈️'],
    },
  ],
  names: {
    '🚀': { pt: ['foguete', 'espaco'], en: ['rocket', 'space'] },
    '✈️': { pt: ['aviao'], en: ['airplane'] },
  },
};

describe('searchEmojis', () => {
  it('finds emoji by Portuguese name', () => {
    expect(searchEmojis(catalog, 'foguete', 'pt')).toContain('🚀');
  });

  it('finds emoji by English name', () => {
    expect(searchEmojis(catalog, 'rocket', 'en')).toContain('🚀');
  });

  it('returns empty for blank query', () => {
    expect(searchEmojis(catalog, '   ', 'pt')).toEqual([]);
  });
});

describe('recent emojis', () => {
  it('persists recents in localStorage', () => {
    const store = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => {
        store.set(key, value);
      },
      removeItem: (key: string) => {
        store.delete(key);
      },
    });

    rememberRecentEmoji('🚀');
    rememberRecentEmoji('👍');
    rememberRecentEmoji('🚀');
    expect(readRecentEmojis()).toEqual(['🚀', '👍']);
  });
});
