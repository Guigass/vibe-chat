import { describe, expect, it } from 'vitest';
import { ChatMessage } from '../../shared/models/chat.models';
import {
  bumpChannelParentForThreadReply,
  findMessageByCorrelators,
  gapFillAfterSeq,
  hasSeqGap,
  idsEqual,
  markReplyQuotesDeleted,
  maxSeqForChannel,
  minSeqForChannel,
  mergeMessagesById,
  replyPreviewText,
  upsertRemoteMessage,
} from './message-sync';

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

describe('message-sync', () => {
  it('maxSeqForChannel ignores other channels and missing seq', () => {
    const messages = [
      msg({ id: '1', channelId: 'c1', seq: 3 }),
      msg({ id: '2', channelId: 'c1', seq: 7 }),
      msg({ id: '3', channelId: 'c2', seq: 99 }),
      msg({ id: '4', channelId: 'c1', seq: undefined }),
    ];
    expect(maxSeqForChannel(messages, 'c1')).toBe(7);
    expect(maxSeqForChannel(messages, 'missing')).toBe(0);
  });

  it('minSeqForChannel ignores other channels and missing seq', () => {
    const messages = [
      msg({ id: '1', channelId: 'c1', seq: 3 }),
      msg({ id: '2', channelId: 'c1', seq: 7 }),
      msg({ id: '3', channelId: 'c2', seq: 99 }),
      msg({ id: '4', channelId: 'c1', seq: undefined }),
    ];
    expect(minSeqForChannel(messages, 'c1')).toBe(3);
    expect(minSeqForChannel(messages, 'missing')).toBe(0);
  });

  it('hasSeqGap detects skipped sequences', () => {
    const messages = [msg({ id: '1', channelId: 'c1', seq: 5 })];
    expect(hasSeqGap(messages, 'c1', 6)).toBe(false);
    expect(hasSeqGap(messages, 'c1', 7)).toBe(true);
    expect(hasSeqGap(messages, 'c1', undefined)).toBe(false);
    expect(hasSeqGap([], 'c1', 2)).toBe(false);
  });

  it('gapFillAfterSeq keeps an overlap window', () => {
    expect(gapFillAfterSeq(0)).toBe(0);
    expect(gapFillAfterSeq(10, 50)).toBe(0);
    expect(gapFillAfterSeq(80, 50)).toBe(30);
  });

  it('mergeMessagesById upserts edits/deletes and keeps clientMessageId', () => {
    const current = [
      msg({
        id: 'm1',
        channelId: 'c1',
        seq: 1,
        body: 'old',
        clientMessageId: 'client-1',
        mine: true,
        status: 'sending',
      }),
      msg({ id: 'm2', channelId: 'c1', seq: 2, body: 'keep' }),
    ];
    const incoming = [
      msg({
        id: 'm1',
        channelId: 'c1',
        seq: 1,
        body: 'edited',
        editedAt: '2026-01-01T01:00:00.000Z',
        status: 'persisted',
        mine: true,
      }),
      msg({
        id: 'm3',
        channelId: 'c1',
        seq: 3,
        body: 'new',
      }),
      msg({
        id: 'm2',
        channelId: 'c1',
        seq: 2,
        body: '',
        deletedAt: '2026-01-01T01:05:00.000Z',
      }),
    ];

    const merged = mergeMessagesById(current, incoming);
    const byId = Object.fromEntries(merged.map((m) => [m.id, m]));

    expect(byId['m1'].body).toBe('edited');
    expect(byId['m1'].clientMessageId).toBe('client-1');
    expect(byId['m1'].mine).toBe(true);
    expect(byId['m1'].editedAt).toBe('2026-01-01T01:00:00.000Z');
    expect(byId['m2'].deletedAt).toBeTruthy();
    expect(byId['m3'].body).toBe('new');
    expect(merged).toHaveLength(3);
  });

  it('idsEqual compares case-insensitively', () => {
    expect(idsEqual('AaBb', 'aabb')).toBe(true);
    expect(idsEqual('a', 'b')).toBe(false);
    expect(idsEqual(undefined, 'a')).toBe(false);
  });

  it('findMessageByCorrelators matches optimistic id / clientMessageId', () => {
    const clientId = '11111111-1111-1111-1111-111111111111';
    const current = [
      msg({
        id: clientId,
        channelId: 'c1',
        clientMessageId: clientId,
        status: 'sending',
        mine: true,
      }),
    ];

    expect(
      findMessageByCorrelators(current, {
        id: clientId.toUpperCase(),
        clientMessageId: undefined,
      })?.id,
    ).toBe(clientId);

    expect(
      findMessageByCorrelators(current, {
        id: '99999999-9999-9999-9999-999999999999',
        clientMessageId: clientId,
      })?.clientMessageId,
    ).toBe(clientId);
  });

  it('upsertRemoteMessage merges hub fan-out onto optimistic bubble (BUG-001)', () => {
    const clientId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
    const optimistic = msg({
      id: clientId,
      channelId: 'c1',
      body: 'ping',
      clientMessageId: clientId,
      status: 'sending',
      mine: true,
    });

    const fromHubNoClientId = msg({
      id: clientId.toUpperCase(),
      channelId: 'c1',
      seq: 12,
      body: 'ping',
      status: 'persisted',
      mine: true,
    });

    const merged = upsertRemoteMessage([optimistic], fromHubNoClientId);
    expect(merged).toHaveLength(1);
    expect(merged[0].id).toBe(clientId);
    expect(merged[0].clientMessageId).toBe(clientId);
    expect(merged[0].seq).toBe(12);
    expect(merged[0].status).toBe('persisted');

    const fromHubWithClientId = msg({
      id: clientId,
      channelId: 'c1',
      seq: 12,
      body: 'ping',
      clientMessageId: clientId,
      status: 'persisted',
      mine: true,
    });
    expect(upsertRemoteMessage([optimistic], fromHubWithClientId)).toHaveLength(1);
  });

  it('bumpChannelParentForThreadReply sets threadId and bumps when parent had no threadId (BUG-009)', () => {
    const parentId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
    const threadId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
    const channelId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
    const current = [
      msg({
        id: parentId,
        channelId,
        conversationId: channelId,
        body: 'parent',
        replyCount: 0,
        threadId: null,
      }),
    ];

    const bumped = bumpChannelParentForThreadReply(current, {
      threadId,
      parentMessageId: parentId,
      replyToMessageId: parentId,
    });

    expect(bumped[0].threadId).toBe(threadId);
    expect(bumped[0].replyCount).toBe(1);

    const again = bumpChannelParentForThreadReply(bumped, {
      threadId: threadId.toUpperCase(),
      parentMessageId: null,
      replyToMessageId: 'other',
    });
    expect(again[0].replyCount).toBe(2);
  });

  it('replyPreviewText flattens and truncates', () => {
    expect(replyPreviewText('  hello\nworld  ')).toBe('hello world');
    expect(replyPreviewText('x'.repeat(200)).length).toBe(140);
    expect(replyPreviewText('x'.repeat(200)).endsWith('…')).toBe(true);
  });

  it('markReplyQuotesDeleted marks cite previews', () => {
    const deletedId = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
    const current = [
      msg({
        id: '1',
        channelId: 'c1',
        replyTo: {
          messageId: deletedId,
          authorName: 'Alice',
          preview: 'hi',
          deleted: false,
        },
      }),
    ];
    const next = markReplyQuotesDeleted(current, deletedId);
    expect(next[0].replyTo?.deleted).toBe(true);
    expect(next[0].replyTo?.preview).toBe('');
  });
});
