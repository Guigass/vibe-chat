import { describe, expect, it } from 'vitest';
import { formatForwardOrigin, formatLinkPreviewLabel, formatReactionAriaLabel } from './message-media';
import { ui } from '../../../core/i18n/strings';

describe('message bubble text helpers', () => {
  it('formats a direct forward origin and a channel slug', () => {
    const origin = { messageId: 'm', channelId: 'c', authorName: 'Bob', createdAt: '' };
    expect(formatForwardOrigin({ ...origin, channelName: 'dm:bob', isDirect: true })).toBe('DM');
    expect(formatForwardOrigin({ ...origin, channelName: 'geral' })).toBe('#geral');
  });

  it('keeps the reaction accessible name even when a tooltip is present', () => {
    expect(formatReactionAriaLabel('👍', '')).toBe(ui.bubbleReaction.replace('%emoji%', '👍'));
    expect(formatReactionAriaLabel('👍', 'Bob')).toContain('👍');
    expect(formatReactionAriaLabel('👍', 'Bob')).toContain('Bob');
  });

  it('labels a link preview from title and site', () => {
    expect(
      formatLinkPreviewLabel({
        id: 'lp',
        url: 'https://example.com',
        title: 'Example',
        siteName: 'example.com',
        hasImage: false,
        status: 'Ready',
      }),
    ).toBe('Example — example.com');
  });
});
