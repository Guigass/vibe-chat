import { describe, expect, it } from 'vitest';
import { TypingState } from '../../shared/models/chat.models';
import { withoutSelfTyping } from './typing-filter';

function typing(partial: Partial<TypingState> & Pick<TypingState, 'userId'>): TypingState {
  return {
    channelId: partial.channelId ?? 'c1',
    userId: partial.userId,
    displayName: partial.displayName ?? 'User',
  };
}

describe('withoutSelfTyping', () => {
  it('keeps all entries when selfUserId is missing', () => {
    const entries = [typing({ userId: 'alice' }), typing({ userId: 'bob' })];
    expect(withoutSelfTyping(entries, null)).toEqual(entries);
    expect(withoutSelfTyping(entries, undefined)).toEqual(entries);
    expect(withoutSelfTyping(entries, '')).toEqual(entries);
  });

  it('removes only the local user', () => {
    const entries = [
      typing({ userId: 'alice', displayName: 'Alice' }),
      typing({ userId: 'bob', displayName: 'Bob' }),
      typing({ userId: 'alice', channelId: 'c2', displayName: 'Alice' }),
    ];
    expect(withoutSelfTyping(entries, 'alice')).toEqual([
      typing({ userId: 'bob', displayName: 'Bob' }),
    ]);
  });
});
