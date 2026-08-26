import '@angular/compiler';
import { Injector } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { ThreadStore } from './thread.store';

describe('MessageStore loadChannel (OPS-E2E-B097)', () => {
  const channelId = '11111111-1111-1111-1111-111111111111';
  let getMessages: ReturnType<typeof vi.fn>;
  let store: MessageStore;

  beforeEach(() => {
    getMessages = vi.fn().mockResolvedValue({
      messages: [
        {
          id: 'm-1',
          conversationId: channelId,
          channelId,
          authorUserId: 'u-bob',
          authorName: 'Bob',
          body: 'olá',
          createdAt: '2026-08-10T12:00:00.000Z',
          seq: 1,
        },
      ],
      hasMoreBefore: false,
      hasMoreAfter: false,
    });

    const injector = Injector.create({
      providers: [
        MessageStore,
        {
          provide: ApiService,
          useValue: {
            getMessages,
            upsertReadCursor: vi.fn(),
            sendMessage: vi.fn(),
          },
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
            onPollChanged: () => () => undefined,
            onReconnected: () => () => undefined,
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            isDemo: () => false,
            activeChannelId: () => channelId,
            activeChannel: () => ({
              id: channelId,
              workspaceId: 'ws',
              name: 'geral',
              unreadCount: 0,
            }),
            bumpUnread: vi.fn(),
            bumpMention: vi.fn(),
            syncChannelUnread: vi.fn(),
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

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('does not flash loading when the channel already has cached messages', async () => {
    await store.loadChannel(channelId);
    expect(store.loading()).toBe(false);
    expect(store.forActiveChannel()).toHaveLength(1);

    const pending = store.loadChannel(channelId);
    expect(store.loading()).toBe(false);
    await pending;

    expect(store.loading()).toBe(false);
    expect(getMessages).toHaveBeenCalledTimes(2);
  });

  it('shows loading on the first fetch of an empty channel', async () => {
    let release!: () => void;
    getMessages.mockReturnValue(
      new Promise((resolve) => {
        release = () =>
          resolve({
            messages: [],
            hasMoreBefore: false,
            hasMoreAfter: false,
          });
      }),
    );

    const pending = store.loadChannel(channelId);
    expect(store.loading()).toBe(true);
    release();
    await pending;
    expect(store.loading()).toBe(false);
  });
});
