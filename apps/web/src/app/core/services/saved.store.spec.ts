import '@angular/compiler';
import { Injector, signal } from '@angular/core';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../api/api.service';
import { ChannelStore } from './channel.store';
import { MessageStore } from './message.store';
import { SavedStore } from './saved.store';
import { SavedMessageItem } from '../../shared/models/chat.models';

describe('SavedStore', () => {
  let api: {
    getSavedMessages: ReturnType<typeof vi.fn>;
    saveMessage: ReturnType<typeof vi.fn>;
    unsaveMessage: ReturnType<typeof vi.fn>;
    patchSavedMessage: ReturnType<typeof vi.fn>;
  };
  let messages: {
    applySavedFlags: ReturnType<typeof vi.fn>;
    setSaved: ReturnType<typeof vi.fn>;
    jumpToSequence: ReturnType<typeof vi.fn>;
  };
  let store: SavedStore;

  beforeEach(() => {
    api = {
      getSavedMessages: vi.fn(),
      saveMessage: vi.fn(),
      unsaveMessage: vi.fn(),
      patchSavedMessage: vi.fn(),
    };
    messages = {
      applySavedFlags: vi.fn(),
      setSaved: vi.fn(),
      jumpToSequence: vi.fn(),
    };
    const workspace = signal({ id: 'ws-1', name: 'Demo' });
    const injector = Injector.create({
      providers: [
        SavedStore,
        { provide: ApiService, useValue: api },
        {
          provide: ChannelStore,
          useValue: {
            isDemo: () => false,
            activeWorkspace: () => workspace(),
            selectChannel: vi.fn(),
          },
        },
        { provide: MessageStore, useValue: messages },
      ],
    });
    store = injector.get(SavedStore);
  });

  it('tracks pending count on empty list', async () => {
    api.getSavedMessages.mockResolvedValue({
      items: [],
      nextCursor: null,
      pendingCount: 0,
    });

    await store.loadForWorkspace('ws-1');
    expect(store.pendingCount()).toBe(0);
    expect(store.items()).toEqual([]);
    expect(messages.applySavedFlags).toHaveBeenCalledWith([]);
  });

  it('exposes removed message item in panel list', async () => {
    const removed: SavedMessageItem = {
      messageId: 'm1',
      channelId: 'c1',
      channelName: 'geral',
      channelType: 'Public',
      sequence: 1,
      authorUserId: 'u1',
      authorName: 'Alice',
      bodyPreview: 'Mensagem removida',
      note: null,
      completedAt: null,
      createdAt: '2026-08-11T12:00:00.000Z',
      messageRemoved: true,
    };
    api.getSavedMessages.mockResolvedValue({
      items: [removed],
      nextCursor: null,
      pendingCount: 1,
    });

    store.openPanel();
    await Promise.resolve();
    expect(store.items()[0]?.messageRemoved).toBe(true);
    expect(store.items()[0]?.bodyPreview).toBe('Mensagem removida');
    expect(store.pendingCount()).toBe(1);
  });
});
