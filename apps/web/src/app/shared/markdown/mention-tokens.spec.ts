import { describe, expect, it } from 'vitest';
import {
  detectMentionQuery,
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

  it('detects mention query at cursor', () => {
    const text = 'hello @ali';
    const context = detectMentionQuery(text, text.length);
    expect(context?.query).toBe('ali');
    expect(context?.atIndex).toBe(6);
  });

  it('inserts mention token replacing query', () => {
    const userId = '55555555-5555-5555-5555-555555555555';
    const result = insertMentionToken('Oi @al', 3, 2, userMentionToken(userId));
    expect(result.value).toBe(`Oi ${userMentionToken(userId)} `);
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
