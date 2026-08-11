import '@angular/compiler';
import { Injector, signal } from '@angular/core';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { ThreadStore } from './thread.store';

describe('MessageStore targeted timeline scroll (BUG-013)', () => {
  const channelId = '11111111-1111-1111-1111-111111111111';
  const messageId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
  let getMessages: ReturnType<typeof vi.fn>;
  let store: MessageStore;

  beforeEach(() => {
    getMessages = vi.fn().mockResolvedValue({
      messages: [
        {
          id: messageId,
          conversationId: channelId,
          channelId,
          authorUserId: 'u-bob',
          authorName: 'Bob',
          body: 'mensagem fixada',
          createdAt: '2026-08-11T12:00:00.000Z',
          seq: 23,
        },
      ],
      hasMoreBefore: true,
      hasMoreAfter: true,
    });
    const activeChannelId = signal<string | null>(channelId);
    const injector = Injector.create({
      providers: [
        MessageStore,
        {
          provide: ApiService,
          useValue: { getMessages, upsertReadCursor: vi.fn(), sendMessage: vi.fn() },
        },
        {
          provide: AuthService,
          useValue: {
            isOfflineDemo: () => false,
            profile: () => ({ id: 'u-alice', name: 'Alice' }),
          },
        },
        {
          provide: ChatHubService,
          useValue: {
            joinChannel: vi.fn().mockResolvedValue(undefined),
            onMessage: () => () => undefined,
            onMessageEdited: () => () => undefined,
            onMessageDeleted: () => () => undefined,
            onReactionChanged: () => () => undefined,
            onAttachmentThumbnailReady: () => () => undefined,
            onLinkPreviewReady: () => () => undefined,
            onReconnected: () => () => undefined,
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            isDemo: () => false,
            activeChannelId: () => activeChannelId(),
            activeChannel: () => ({ id: channelId, unreadCount: 0 }),
            bumpUnread: vi.fn(),
            bumpMention: vi.fn(),
          },
        },
        {
          provide: ThreadStore,
          useValue: {
            bumpReplyCount: vi.fn(),
            gapFillActive: vi.fn(),
            applyLinkPreviewCleared: vi.fn(),
          },
        },
      ],
    });
    store = injector.get(MessageStore);
  });

  it('keeps a versioned request until Timeline acknowledges the rendered target', async () => {
    const result = await store.jumpToSequence(channelId, 23, messageId);

    expect(result).toBe('ok');
    expect(getMessages).toHaveBeenCalledWith(channelId, { around: 23, take: 50 });
    expect(store.scrollRequest()).toMatchObject({ channelId, messageId });
    expect(store.highlightMessageId()).toBe(messageId);

    const requestId = store.scrollRequest()!.requestId;
    store.acknowledgeScrollRequest(requestId + 1);
    expect(store.scrollRequest()?.requestId).toBe(requestId);
    store.acknowledgeScrollRequest(requestId);
    expect(store.scrollRequest()).toBeNull();
  });

  it('cancels the request when the around page does not contain the target', async () => {
    getMessages.mockResolvedValue({ messages: [], hasMoreBefore: false, hasMoreAfter: false });

    expect(await store.jumpToSequence(channelId, 99, messageId)).toBe('missing');
    expect(store.scrollRequest()).toBeNull();
  });
});
