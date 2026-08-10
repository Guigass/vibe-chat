import '@angular/compiler';
import { Injector, signal } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatMessage } from '../../shared/models/chat.models';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { ThreadStore } from './thread.store';

describe('MessageStore read cursor (BUG-002)', () => {
  const channelId = '11111111-1111-1111-1111-111111111111';
  let upsertReadCursor: ReturnType<typeof vi.fn>;
  let getMessages: ReturnType<typeof vi.fn>;
  let messageHandler: ((message: ChatMessage) => void) | null;
  let activeChannelId: ReturnType<typeof signal<string | null>>;
  let bumpUnread: ReturnType<typeof vi.fn>;
  let store: MessageStore;

  beforeEach(() => {
    vi.useFakeTimers();
    upsertReadCursor = vi.fn().mockResolvedValue(undefined);
    getMessages = vi.fn().mockResolvedValue([
      {
        id: 'm-1',
        conversationId: channelId,
        channelId,
        authorUserId: 'u-bob',
        authorName: 'Bob',
        body: 'olá',
        createdAt: '2026-08-10T12:00:00.000Z',
        seq: 7,
      },
      {
        id: 'm-2',
        conversationId: channelId,
        channelId,
        authorUserId: 'u-alice',
        authorName: 'Alice',
        body: 'oi',
        createdAt: '2026-08-10T12:01:00.000Z',
        seq: 12,
      },
    ]);
    messageHandler = null;
    activeChannelId = signal<string | null>(channelId);
    bumpUnread = vi.fn();

    const injector = Injector.create({
      providers: [
        MessageStore,
        {
          provide: ApiService,
          useValue: {
            getMessages,
            upsertReadCursor,
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
            onMessage: (handler: (message: ChatMessage) => void) => {
              messageHandler = handler;
              return () => {
                messageHandler = null;
              };
            },
            onMessageEdited: () => () => undefined,
            onMessageDeleted: () => () => undefined,
            onReactionChanged: () => () => undefined,
            onReconnected: () => () => undefined,
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            isDemo: () => false,
            activeChannelId: () => activeChannelId(),
            activeChannel: () => ({
              id: channelId,
              workspaceId: 'ws',
              name: 'geral',
              unreadCount: 0,
            }),
            bumpUnread,
            bumpMention: vi.fn(),
          },
        },
        {
          provide: ThreadStore,
          useValue: {
            bumpReplyCount: vi.fn(),
            gapFillActive: vi.fn(),
          },
        },
      ],
    });
    store = injector.get(MessageStore);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('calls upsertReadCursor with max seq after loadChannel', async () => {
    await store.loadChannel(channelId);

    expect(getMessages).toHaveBeenCalledWith(channelId);
    expect(upsertReadCursor).toHaveBeenCalledWith(channelId, 12);
  });

  it('debounces upsertReadCursor for remote messages on the active channel', async () => {
    expect(messageHandler).toBeTruthy();

    messageHandler!({
      id: 'm-remote-1',
      conversationId: channelId,
      channelId,
      authorUserId: 'u-bob',
      authorName: 'Bob',
      body: 'ping',
      createdAt: '2026-08-10T12:02:00.000Z',
      status: 'persisted',
      mine: false,
      seq: 13,
    });
    messageHandler!({
      id: 'm-remote-2',
      conversationId: channelId,
      channelId,
      authorUserId: 'u-bob',
      authorName: 'Bob',
      body: 'pong',
      createdAt: '2026-08-10T12:02:01.000Z',
      status: 'persisted',
      mine: false,
      seq: 14,
    });

    expect(upsertReadCursor).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(500);

    expect(upsertReadCursor).toHaveBeenCalledTimes(1);
    expect(upsertReadCursor).toHaveBeenCalledWith(channelId, 14);
  });

  it('does not call upsertReadCursor for messages on inactive channels', async () => {
    activeChannelId.set('22222222-2222-2222-2222-222222222222');
    expect(messageHandler).toBeTruthy();

    messageHandler!({
      id: 'm-other',
      conversationId: channelId,
      channelId,
      authorUserId: 'u-bob',
      authorName: 'Bob',
      body: 'outra',
      createdAt: '2026-08-10T12:03:00.000Z',
      status: 'persisted',
      mine: false,
      seq: 20,
    });

    await vi.advanceTimersByTimeAsync(500);

    expect(upsertReadCursor).not.toHaveBeenCalled();
  });

  it('bumps unread and skips read cursor when viewer is in history', async () => {
    store.setViewingLatest(false);
    expect(messageHandler).toBeTruthy();

    messageHandler!({
      id: 'm-history',
      conversationId: channelId,
      channelId,
      authorUserId: 'u-bob',
      authorName: 'Bob',
      body: 'enquanto lia',
      createdAt: '2026-08-10T12:04:00.000Z',
      status: 'persisted',
      mine: false,
      seq: 21,
    });

    await vi.advanceTimersByTimeAsync(500);

    expect(upsertReadCursor).not.toHaveBeenCalled();
    expect(bumpUnread).toHaveBeenCalledWith(channelId);
  });

  it('persists max seq when markViewedLatest is called', async () => {
    store.setViewingLatest(false);
    await store.loadChannel(channelId);
    upsertReadCursor.mockClear();

    store.markViewedLatest();
    await vi.advanceTimersByTimeAsync(500);

    expect(upsertReadCursor).toHaveBeenCalledWith(channelId, 12);
  });
});
