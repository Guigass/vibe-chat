import { describe, expect, it } from 'vitest';
import {
  detectBrowserLocale,
  isAppLocale,
  persistLocale,
  readStoredLocale,
  resolveBootstrapLocale,
} from './locale';

describe('locale', () => {
  it('accepts only pt-BR and en', () => {
    expect(isAppLocale('pt-BR')).toBe(true);
    expect(isAppLocale('en')).toBe(true);
    expect(isAppLocale('fr')).toBe(false);
    expect(isAppLocale(null)).toBe(false);
  });

  it('detects English and Portuguese from the browser list', () => {
    expect(detectBrowserLocale(['en-US', 'pt-BR'])).toBe('en');
    expect(detectBrowserLocale(['pt-PT'])).toBe('pt-BR');
    expect(detectBrowserLocale(['fr-FR'])).toBe('pt-BR');
  });

  it('prefers a stored locale over the browser', () => {
    const storage = new Map<string, string>();
    persistLocale('en', { setItem: (k, v) => storage.set(k, v) });
    expect(readStoredLocale({ getItem: (k) => storage.get(k) ?? null })).toBe('en');
    expect(
      resolveBootstrapLocale({ getItem: (k) => storage.get(k) ?? null }, ['pt-BR']),
    ).toBe('en');
  });
});
