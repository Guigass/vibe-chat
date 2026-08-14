import { describe, expect, it } from 'vitest';
import {
  detectMentionQuery,
  encodeMentionPlainText,
  filterMentionItems,
  formatMentionPlainText,
  insertMentionToken,
  parseMentionTokens,
  userMentionToken,
} from './mention-tokens';
import { parseRestrictedMarkdown } from './restricted-markdown';

describe('mention tokens', () => {
  it('parses user mention tokens', () => {
    const userId = '44444444-4444-4444-4444-444444444444';
    const tokens = parseMentionTokens(`Oi ${userMentionToken(userId)}!`);
    expect(tokens).toHaveLength(1);
    expect(tokens[0].kind).toBe('user');
    expect(tokens[0].userId).toBe(userId);
  });

  it('detects a bare @ when the cursor is after it', () => {
    expect(detectMentionQuery('@', 1)).toEqual({ query: '', atIndex: 0 });
    expect(detectMentionQuery('@', 0)).toBeNull();
  });

  it('formats mention tokens as readable plain text', () => {
    const userId = '55555555-5555-5555-5555-555555555555';
    const text = `hey ${userMentionToken(userId)} <@here>`;
    expect(formatMentionPlainText(text, { [userId]: 'Bob' })).toBe('hey @Bob @aqui');
    expect(formatMentionPlainText(userMentionToken(userId), {})).toBe('@usuário');
  });

  it('inserts mention token replacing query', () => {
    const userId = '55555555-5555-5555-5555-555555555555';
    const result = insertMentionToken('Oi @al', 3, 2, userMentionToken(userId));
    expect(result.value).toBe(`Oi ${userMentionToken(userId)} `);
  });

  it('omits the current user from autocomplete suggestions', () => {
    const me = '44444444-4444-4444-4444-444444444444';
    const items = filterMentionItems(
      [
        { kind: 'here', displayName: '@aqui' },
        { kind: 'user', userId: me, displayName: 'Alice' },
        { kind: 'user', userId: '55555555-5555-5555-5555-555555555555', displayName: 'Bob' },
      ],
      '',
      { excludeUserId: me },
    );
    expect(items.map((item) => item.displayName)).toEqual(['@aqui', 'Bob']);
  });

  it('encodes composer display mentions back to stable tokens', () => {
    const bob = '55555555-5555-5555-5555-555555555555';
    expect(encodeMentionPlainText('oi @Bob, vê @aqui', { [bob]: 'Bob' })).toBe(
      `oi ${userMentionToken(bob)}, vê <@here>`,
    );
    expect(formatMentionPlainText(`oi ${userMentionToken(bob)}`, { [bob]: 'Bob' })).toBe('oi @Bob');
  });
});

describe('restricted markdown mentions', () => {
  it('renders mention inline nodes', () => {
    const userId = '44444444-4444-4444-4444-444444444444';
    const doc = parseRestrictedMarkdown(`Ping ${userMentionToken(userId)}`);
    const paragraph = doc.blocks[0];
    expect(paragraph.kind).toBe('paragraph');
    if (paragraph.kind !== 'paragraph') return;
    expect(paragraph.inlines.some((node) => node.kind === 'mention')).toBe(true);
  });
});
