import { describe, expect, it } from 'vitest';
import { formatLocaleDate, formatLocaleNumber, pluralCount } from './format';

describe('format', () => {
  it('formats dates and numbers with the active locale', () => {
    const date = new Date(2026, 2, 12);
    expect(formatLocaleDate(date, 'pt-BR', { day: 'numeric', month: 'long' })).toBe('12 de março');
    expect(formatLocaleDate(date, 'en', { day: 'numeric', month: 'long' }).toLowerCase()).toContain(
      'march',
    );
    expect(formatLocaleNumber(1234, 'pt-BR')).toMatch(/1.?234/);
    expect(formatLocaleNumber(1234, 'en')).toBe('1,234');
  });

  it('uses ICU-style 0 / 1 / N labels', () => {
    expect(pluralCount(0, 'result')).toBe('Nenhum resultado');
    expect(pluralCount(1, 'result')).toBe('1 resultado');
    expect(pluralCount(3, 'result')).toBe('3 resultados');
    expect(pluralCount(1, 'pin')).toBe('1 fixada');
    expect(pluralCount(4, 'pin')).toBe('4 fixadas');
  });
});
