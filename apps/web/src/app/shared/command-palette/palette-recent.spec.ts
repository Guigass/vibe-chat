import { describe, expect, it } from 'vitest';
import { readRecentPaletteIds, writeRecentPaletteId } from './palette-recent';

describe('palette recents (B-099)', () => {
  it('stores unique ids with the newest first', () => {
    const storage = memoryStorage();
    writeRecentPaletteId('u-1', 'ch-geral', storage);
    writeRecentPaletteId('u-1', 'u-bob', storage);
    writeRecentPaletteId('u-1', 'ch-geral', storage);
    expect(readRecentPaletteIds('u-1', storage)).toEqual(['ch-geral', 'u-bob']);
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
    key() {
      return null;
    },
    removeItem(key: string) {
      data.delete(key);
    },
    setItem(key: string, value: string) {
      data.set(key, value);
    },
  };
}
