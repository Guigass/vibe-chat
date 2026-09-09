import { describe, expect, it } from 'vitest';
import {
  detectBrowserLocale,
  isAppLocale,
  persistLocale,
  readStoredLocale,
  resolveBootstrapLocale,
} from './locale';

describe('locale', () => {
  it('accepts the supported locales only', () => {
    expect(isAppLocale('pt-BR')).toBe(true);
    expect(isAppLocale('en')).toBe(true);
    expect(isAppLocale('fr')).toBe(true);
    expect(isAppLocale('zh-CN')).toBe(true);
    expect(isAppLocale('xx')).toBe(false);
    expect(isAppLocale(null)).toBe(false);
  });

  it('maps browser language tags to a supported locale', () => {
    expect(detectBrowserLocale(['en-US', 'pt-BR'])).toBe('en');
    expect(detectBrowserLocale(['pt-PT'])).toBe('pt-BR');
    expect(detectBrowserLocale(['fr-FR'])).toBe('fr');
    expect(detectBrowserLocale(['zh-Hans-CN'])).toBe('zh-CN');
    expect(detectBrowserLocale(['sv-SE'])).toBe('pt-BR');
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
