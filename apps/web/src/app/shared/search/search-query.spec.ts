import { describe, expect, it } from 'vitest';
import {
  applySearchOperator,
  hasSearchFilter,
  highlightSearchParts,
  parseSearchQuery,
  removeSearchChip,
  serializeSearchQuery,
} from './search-query';

describe('search query parser', () => {
  it('parses operators and leftover term', () => {
    const parsed = parseSearchQuery('de:@alice em:#geral antes:2026-07-01 tem:anexo pdf urgente');
    expect(parsed.authorToken).toBe('@alice');
    expect(parsed.channelToken).toBe('#geral');
    expect(parsed.to).toBe('2026-07-01');
    expect(parsed.hasAttachment).toBe(true);
    expect(parsed.term).toBe('pdf urgente');
    expect(parsed.chips.map((c) => c.key)).toEqual(['author', 'channel', 'to', 'hasAttachment']);
  });

  it('detects active operator for autocomplete', () => {
    expect(parseSearchQuery('de:al').activeOperator).toEqual({ op: 'de', query: 'al' });
    expect(parseSearchQuery('hello em:#').activeOperator).toEqual({ op: 'em', query: '#' });
    expect(parseSearchQuery('tem:').activeOperator).toEqual({ op: 'tem', query: '' });
  });

  it('serializes and removes chips', () => {
    const input = 'de:@alice tem:link hello';
    const parsed = parseSearchQuery(input);
    expect(serializeSearchQuery(parsed)).toBe('de:@alice tem:link hello');
    const withoutAuthor = removeSearchChip(input, parsed.chips[0]);
    expect(parseSearchQuery(withoutAuthor).authorToken).toBeUndefined();
    expect(parseSearchQuery(withoutAuthor).hasLink).toBe(true);
    expect(parseSearchQuery(withoutAuthor).term).toBe('hello');
  });

  it('applies operator replacing the tail token', () => {
    expect(applySearchOperator('de:al', 'de', 'Alice').trim()).toBe('de:@Alice');
    expect(applySearchOperator('busca em:#ge', 'em', 'geral').trim()).toBe('busca em:#geral');
  });

  it('highlights the term without treating empty as a hit', () => {
    expect(highlightSearchParts('PDF da Alice', '')).toEqual([{ text: 'PDF da Alice', hit: false }]);
    expect(highlightSearchParts('PDF da Alice', 'alice')).toEqual([
      { text: 'PDF da ', hit: false },
      { text: 'Alice', hit: true },
    ]);
  });

  it('hasSearchFilter is true for operator-only queries', () => {
    expect(hasSearchFilter(parseSearchQuery('de:@alice tem:anexo'))).toBe(true);
    expect(hasSearchFilter(parseSearchQuery('hello'))).toBe(false);
  });
});
