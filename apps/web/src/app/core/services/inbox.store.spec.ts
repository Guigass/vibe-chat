import '@angular/compiler';
import { Injector } from '@angular/core';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { InboxApiService, InboxPage } from '../api/inbox-api.service';
import { ChannelStore } from './channel.store';
import { ChatHubService } from './chat-hub.service';
import { FollowedThreadsStore } from './followed-threads.store';
import { InboxStore } from './inbox.store';
import { MessageStore } from './message.store';
import { NotificationPreferencesStore } from './notification-preferences.store';
import { PinStore } from './pin.store';
import { SavedStore } from './saved.store';
import { ThreadStore } from './thread.store';
import { TopicStore } from './topic.store';

describe('InboxStore', () => {
  let api: { list: ReturnType<typeof vi.fn> };
  let store: InboxStore;

  beforeEach(() => {
    api = { list: vi.fn() };
    const hub = {
      onMessage: () => () => undefined,
      onReadCursorChanged: () => () => undefined,
      onAnnouncementAcknowledged: () => () => undefined,
      onReconnected: () => () => undefined,
    };
    const injector = Injector.create({
      providers: [
        InboxStore,
        { provide: InboxApiService, useValue: api },
        {
          provide: ChannelStore,
          useValue: {
            isDemo: () => false,
            activeWorkspace: () => ({ id: 'ws-1' }),
            selectChannel: vi.fn(),
          },
        },
        { provide: MessageStore, useValue: { jumpToSequence: vi.fn() } },
        { provide: ThreadStore, useValue: { close: vi.fn(), openById: vi.fn() } },
        { provide: TopicStore, useValue: { requestOpen: vi.fn() } },
        { provide: SavedStore, useValue: { closePanel: vi.fn() } },
        { provide: PinStore, useValue: { closePanel: vi.fn() } },
        { provide: FollowedThreadsStore, useValue: { closePanel: vi.fn() } },
        { provide: NotificationPreferencesStore, useValue: { closePanel: vi.fn() } },
        { provide: ChatHubService, useValue: hub },
      ],
    });
    store = injector.get(InboxStore);
  });

  it('keeps explainable counts and does not duplicate a page', async () => {
    const page: InboxPage = {
      items: [
        {
          itemKey: 'mention:m1',
          kind: 'mention',
          priority: 2,
          reason: 'mention',
          channelId: 'c1',
          channelName: 'geral',
          messageId: 'm1',
          sequence: 4,
          bodyPreview: 'oi',
          messageRemoved: false,
          sortAt: '2026-10-13T18:00:00.000Z',
          navigation: { channelId: 'c1', messageId: 'm1', sequence: 4 },
        },
      ],
      nextCursor: null,
      counts: { announcement: 0, mention: 1, direct: 0, thread: 0, saved: 0, total: 1 },
    };
    api.list.mockResolvedValue(page);

    await store.refresh();
    expect(store.openCount()).toBe(1);
    expect(store.items().map((item) => item.itemKey)).toEqual(['mention:m1']);
    expect(store.error()).toBeNull();
  });
});
