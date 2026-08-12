import '@angular/compiler';
import { Injector } from '@angular/core';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';

const chatHubMock = () => ({
  joinChannel: vi.fn(),
  joinChannels: vi.fn(),
  onReadCursorChanged: () => () => undefined,
});

describe('ChannelStore.activeChannelId (mic recording)', () => {
  it('keeps the same id when another channel unread is bumped', async () => {
    const injector = Injector.create({
      providers: [
        ChannelStore,
        { provide: ApiService, useValue: {} },
        { provide: ChatHubService, useValue: chatHubMock() },
        {
          provide: AuthService,
          useValue: {
            isOfflineDemo: () => true,
            profile: () => ({ id: 'u-alice' }),
          },
        },
      ],
    });
    const store = injector.get(ChannelStore);
    await store.load();

    const activeId = store.activeChannelId();
    const other = store.channels().find((c) => c.id !== activeId);
    expect(activeId).toBeTruthy();
    expect(other).toBeTruthy();

    const otherUnreadBefore = other!.unreadCount;
    store.bumpUnread(other!.id);

    expect(store.activeChannelId()).toBe(activeId);
    expect(store.channels().find((c) => c.id === other!.id)?.unreadCount).toBe(
      otherUnreadBefore + 1,
    );
  });

  it('increments unread on the active channel (reader in history)', async () => {
    const injector = Injector.create({
      providers: [
        ChannelStore,
        { provide: ApiService, useValue: {} },
        { provide: ChatHubService, useValue: chatHubMock() },
        {
          provide: AuthService,
          useValue: {
            isOfflineDemo: () => true,
            profile: () => ({ id: 'u-alice' }),
          },
        },
      ],
    });
    const store = injector.get(ChannelStore);
    await store.load();

    const activeId = store.activeChannelId();
    expect(activeId).toBeTruthy();
    expect(store.channels().find((c) => c.id === activeId)?.unreadCount).toBe(2);

    store.bumpUnread(activeId!);
    store.bumpUnread(activeId!.toUpperCase());

    expect(store.channels().find((c) => c.id === activeId)?.unreadCount).toBe(4);
  });

  it('snapshots unreadCount when selecting a channel (B-088)', async () => {
    const injector = Injector.create({
      providers: [
        ChannelStore,
        { provide: ApiService, useValue: {} },
        { provide: ChatHubService, useValue: chatHubMock() },
        {
          provide: AuthService,
          useValue: {
            isOfflineDemo: () => true,
            profile: () => ({ id: 'u-alice' }),
          },
        },
      ],
    });
    const store = injector.get(ChannelStore);
    await store.load();

    const ops = store.channels().find((c) => c.name === 'incidentes');
    expect(ops).toBeTruthy();
    expect(ops!.unreadCount).toBe(5);

    store.selectChannel(ops!.id);
    expect(store.openedUnreadCount()).toBe(5);
    expect(store.channels().find((c) => c.id === ops!.id)?.unreadCount).toBe(5);
  });
});
