import '@angular/compiler';
import { Injector } from '@angular/core';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../api/api.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { PinStore } from './pin.store';

describe('PinStore jump ordering (BUG-013)', () => {
  it('closes the context panel before loading and anchoring the target page', async () => {
    const selectChannel = vi.fn();
    let store!: PinStore;
    const jumpToSequence = vi.fn().mockImplementation(async () => {
      expect(store.panelOpen()).toBe(false);
    });
    const injector = Injector.create({
      providers: [
        PinStore,
        { provide: ApiService, useValue: {} },
        {
          provide: ChatHubService,
          useValue: { onPinChanged: () => () => undefined },
        },
        {
          provide: ChannelStore,
          useValue: {
            selectChannel,
            activeChannelId: () => 'channel-1',
            isDemo: () => false,
          },
        },
        {
          provide: MessageStore,
          useValue: { jumpToSequence, setPinned: vi.fn(), applyPinnedFlags: vi.fn() },
        },
      ],
    });
    store = injector.get(PinStore);
    store.openPanel();

    await store.jumpToPin({
      messageId: 'message-1',
      channelId: 'channel-1',
      sequence: 42,
      bodyPreview: 'fixada',
      authorName: 'Alice',
      pinnedByUserId: 'u-bob',
      pinnedByName: 'Bob',
      pinnedAt: '2026-08-11T12:00:00.000Z',
      limit: 20,
    });

    expect(selectChannel).toHaveBeenCalledWith('channel-1');
    expect(jumpToSequence).toHaveBeenCalledWith('channel-1', 42, 'message-1');
  });
});
