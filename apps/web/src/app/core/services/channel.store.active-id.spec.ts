import '@angular/compiler';
import { Injector } from '@angular/core';
import { describe, expect, it } from 'vitest';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChannelStore } from './channel.store';

describe('ChannelStore.activeChannelId (mic recording)', () => {
  it('keeps the same id when another channel unread is bumped', async () => {
    const injector = Injector.create({
      providers: [
        ChannelStore,
        { provide: ApiService, useValue: {} },
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
});
