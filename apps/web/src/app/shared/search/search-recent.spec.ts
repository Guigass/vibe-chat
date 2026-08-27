import { describe, expect, it } from 'vitest';
import { readRecentSearches, recentSearchKey, writeRecentSearch } from './search-recent';

describe('recent searches', () => {
  it('stores newest first and dedupes per user', () => {
    const storage = memoryStorage();
    writeRecentSearch('u1', 'de:@alice', storage);
    writeRecentSearch('u1', 'tem:anexo', storage);
    writeRecentSearch('u1', 'de:@alice', storage);
    expect(readRecentSearches('u1', storage)).toEqual(['de:@alice', 'tem:anexo']);
    expect(storage.getItem(recentSearchKey('u1'))).toContain('de:@alice');
  });
});

function memoryStorage(): Storage {
  const data = new Map<string, string>();
  return {
    get length() {
      return data.size;
    },
    clear() {
      data.clear();
    },
    getItem(key: string) {
      return data.get(key) ?? null;
    },
    key(index: number) {
      return [...data.keys()][index] ?? null;
    },
    removeItem(key: string) {
      data.delete(key);
    },
    setItem(key: string, value: string) {
      data.set(key, value);
    },
  };
}
