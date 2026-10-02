import { describe, expect, it } from 'vitest';
import { ChatMessage } from '../../shared/models/chat.models';
import {
  applyMessageDelete,
  applyMessageEdit,
  applyPinnedIdSet,
  channelTimeline,
  mergeChannelPage,
  toggleLocalReaction,
} from './message-patches';

function msg(partial: Partial<ChatMessage> & Pick<ChatMessage, 'id' | 'channelId'>): ChatMessage {
  return {
    conversationId: partial.channelId,
    authorUserId: 'u1',
    authorName: 'Alice',
    body: partial.body ?? 'hi',
    createdAt: '2026-01-01T00:00:00.000Z',
    status: 'persisted',
    mine: false,
    ...partial,
  };
}

describe('message-patches', () => {
  it('sorts the channel timeline by seq and drops thread replies', () => {
    const channelId = 'c1';
    const timeline = channelTimeline(
      [
        msg({ id: 'late', channelId, seq: 3, createdAt: '2026-01-01T00:00:03.000Z' }),
        msg({
          id: 'reply',
          channelId,
          threadId: 't1',
          conversationId: 't1',
          seq: 9,
        }),
        msg({ id: 'early', channelId, seq: 1, createdAt: '2026-01-01T00:00:01.000Z' }),
        msg({ id: 'other', channelId: 'c2', seq: 2 }),
      ],
      channelId,
    );

    expect(timeline.map((m) => m.id)).toEqual(['early', 'late']);
  });

  it('applies an edit and a delete that clears reply quotes', () => {
    const edited = applyMessageEdit(
      [msg({ id: 'm1', channelId: 'c1', body: 'old', deletedAt: 'x' })],
      { id: 'm1', body: 'new', editedAt: '2026-01-02T00:00:00.000Z', seq: 4 },
    );
    expect(edited[0]).toMatchObject({ body: 'new', deletedAt: null, seq: 4 });

    const deleted = applyMessageDelete(
      [
        msg({ id: 'm1', channelId: 'c1', body: 'gone' }),
        msg({
          id: 'm2',
          channelId: 'c1',
          replyTo: { messageId: 'm1', authorName: 'Alice', preview: 'gone', deleted: false },
        }),
      ],
      { id: 'm1', deletedAt: '2026-01-03T00:00:00.000Z' },
    );
    expect(deleted[0].body).toBe('');
    expect(deleted[1].replyTo?.deleted).toBe(true);
    expect(deleted[1].replyTo?.preview).toBe('');
  });

  it('toggles a local reaction without duplicating the emoji', () => {
    const once = toggleLocalReaction([msg({ id: 'm1', channelId: 'c1', reactions: [] })], 'm1', '🌊');
    expect(once[0].reactions).toEqual([{ emoji: '🌊', count: 1, me: true }]);
    const twice = toggleLocalReaction(once, 'm1', '🌊');
    expect(twice[0].reactions).toEqual([]);
  });

  it('replaces one channel page and merges another', () => {
    const current = [
      msg({ id: 'keep', channelId: 'c2', seq: 1 }),
      msg({ id: 'old', channelId: 'c1', seq: 1, body: 'old', clientMessageId: 'client-1' }),
    ];
    const replaced = mergeChannelPage(current, 'c1', [msg({ id: 'new', channelId: 'c1', seq: 2 })], {
      replace: true,
    });
    expect(replaced.map((m) => m.id).sort()).toEqual(['keep', 'new']);

    const merged = mergeChannelPage(
      current,
      'c1',
      [msg({ id: 'old', channelId: 'c1', seq: 1, body: 'fresh' })],
    );
    expect(merged.find((m) => m.id === 'old')).toMatchObject({
      body: 'fresh',
      clientMessageId: 'client-1',
    });
  });

  it('applies pinned ids only inside the channel', () => {
    const pinned = applyPinnedIdSet(
      [
        msg({ id: 'a', channelId: 'c1', isPinned: false }),
        msg({ id: 'b', channelId: 'c1', isPinned: true }),
        msg({ id: 'c', channelId: 'c2', isPinned: false }),
      ],
      'c1',
      ['a'],
    );
    expect(pinned.map((m) => m.isPinned)).toEqual([true, false, false]);
  });
});
