/** @vitest-environment jsdom */
import { describe, expect, it } from 'vitest';
import {
  ACCENT_PALETTE,
  DARK_INK,
  DARK_MUTED,
  LIGHT_INK,
  LIGHT_MUTED,
  MIN_CONTRAST,
  applyVisualPreferences,
  contrastRatio,
  resolveAccentId,
  resolveWallpaperId,
} from './visual-preferences';

describe('visual preferences', () => {
  it('applies a catalog wallpaper and accent', () => {
    const choice = applyVisualPreferences('tide', 'ocean');
    expect(choice).toEqual({ wallpaper: 'tide', accent: 'ocean' });
    expect(document.documentElement.getAttribute('data-wallpaper')).toBe('tide');
    expect(document.documentElement.getAttribute('data-accent')).toBe('ocean');
  });

  it('resets to the product default', () => {
    applyVisualPreferences('slate', 'rose');
    const choice = applyVisualPreferences(null, null);
    expect(choice).toEqual({ wallpaper: null, accent: null });
    expect(document.documentElement.hasAttribute('data-wallpaper')).toBe(false);
    expect(document.documentElement.hasAttribute('data-accent')).toBe(false);
  });

  it('falls back when the id is not in the catalog', () => {
    applyVisualPreferences('tide', 'ocean');
    const choice = applyVisualPreferences('javascript:alert(1)', '#ff00ff');
    expect(choice).toEqual({ wallpaper: null, accent: null });
    expect(resolveWallpaperId('  mist  ')).toBe('mist');
    expect(resolveAccentId('nope')).toBeNull();
    expect(document.documentElement.hasAttribute('data-wallpaper')).toBe(false);
    expect(document.documentElement.hasAttribute('data-accent')).toBe(false);
  });

  it('keeps accent bubbles at AA contrast in light and dark', () => {
    for (const entry of ACCENT_PALETTE) {
      expect(contrastRatio(entry.lightBubble, LIGHT_INK)).toBeGreaterThanOrEqual(MIN_CONTRAST);
      expect(contrastRatio(entry.lightBubble, LIGHT_MUTED)).toBeGreaterThanOrEqual(MIN_CONTRAST);
      expect(contrastRatio(entry.darkBubble, DARK_INK)).toBeGreaterThanOrEqual(MIN_CONTRAST);
      expect(contrastRatio(entry.darkBubble, DARK_MUTED)).toBeGreaterThanOrEqual(MIN_CONTRAST);
    }
  });
});
