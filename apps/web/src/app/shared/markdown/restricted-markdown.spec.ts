import { describe, expect, it } from 'vitest';
import { parseRestrictedMarkdown, highlightCode } from './restricted-markdown';

describe('parseRestrictedMarkdown', () => {
  it('parses bold inline', () => {
    const doc = parseRestrictedMarkdown('**a**');
    expect(doc.blocks).toEqual([
      { kind: 'paragraph', inlines: [{ kind: 'strong', children: [{ kind: 'text', text: 'a' }] }] },
    ]);
  });

  it('parses fenced code block with language', () => {
    const doc = parseRestrictedMarkdown('```sql\nSELECT 1\n```');
    expect(doc.blocks[0]).toMatchObject({ kind: 'code', language: 'sql', text: 'SELECT 1' });
  });

  it('parses blockquote', () => {
    const doc = parseRestrictedMarkdown('> quoted');
    expect(doc.blocks[0]).toMatchObject({ kind: 'quote' });
  });

  it('parses unordered list', () => {
    const doc = parseRestrictedMarkdown('- one\n- two');
    expect(doc.blocks[0]).toMatchObject({ kind: 'ul' });
  });

  it('auto-links http URLs', () => {
    const doc = parseRestrictedMarkdown('see https://example.com/path');
    const paragraph = doc.blocks[0];
    expect(paragraph.kind).toBe('paragraph');
    if (paragraph.kind === 'paragraph') {
      expect(paragraph.inlines).toContainEqual({
        kind: 'link',
        href: 'https://example.com/path',
        text: 'https://example.com/path',
      });
    }
  });

  it('leaves script tags as plain text', () => {
    const payload = '<script>alert(1)</script>';
    const doc = parseRestrictedMarkdown(payload);
    const paragraph = doc.blocks[0];
    expect(paragraph.kind).toBe('paragraph');
    if (paragraph.kind === 'paragraph') {
      expect(paragraph.inlines).toEqual([{ kind: 'text', text: payload }]);
    }
  });

  it('handles malformed markers as plain text', () => {
    const doc = parseRestrictedMarkdown('**unclosed');
    const paragraph = doc.blocks[0];
    if (paragraph.kind === 'paragraph') {
      expect(paragraph.inlines.some((n) => n.kind === 'text' && n.text.includes('**'))).toBe(true);
    }
  });
});

describe('highlightCode', () => {
  it('highlights SQL keywords', () => {
    const tokens = highlightCode('sql', 'SELECT id FROM users');
    expect(tokens.some((t) => t.className === 'sql-kw' && t.text === 'SELECT')).toBe(true);
  });

  it('returns plain text when language is empty', () => {
    expect(highlightCode('', 'plain')).toEqual([{ text: 'plain' }]);
  });
});
