import '@angular/compiler';
import { Injector } from '@angular/core';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChannelStore } from './channel.store';
import { ChatHubService } from './chat-hub.service';
import { MessageStore } from './message.store';
import { ThreadStore } from './thread.store';
import type { ChatMessage } from '../../shared/models/chat.models';

function msg(overrides: Partial<ChatMessage> = {}): ChatMessage {
  return {
    id: 'm1',
    conversationId: 'ch1',
    channelId: 'ch1',
    authorUserId: 'u1',
    authorName: 'Alice',
    body: 'hello',
    createdAt: new Date().toISOString(),
    status: 'persisted',
    mine: true,
    ...overrides,
  };
}

describe('MessageStore edit mode (B-173)', () => {
  function setup(): MessageStore {
    const injector = Injector.create({
      providers: [
        MessageStore,
        {
          provide: ApiService,
          useValue: {
            editMessage: vi.fn(),
            getMessages: vi.fn(),
            sendMessage: vi.fn(),
          },
        },
        {
          provide: ChatHubService,
          useValue: {
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
          provide: AuthService,
          useValue: {
            profile: () => ({ id: 'u1', name: 'Alice' }),
            isOfflineDemo: () => false,
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            activeChannelId: () => 'ch1',
            activeChannel: () => ({ id: 'ch1', name: 'geral' }),
            isDemo: () => true,
          },
        },
        {
          provide: ThreadStore,
          useValue: {
            gapFillActive: vi.fn(),
            applyLinkPreviewCleared: vi.fn(),
          },
        },
      ],
    });
    return injector.get(MessageStore);
  }

  it('startEdit sets editingMessage and clears reply', () => {
    const store = setup();
    store.setReplyTarget(msg({ id: 'reply', mine: false }));
    expect(store.replyTarget()?.id).toBe('reply');

    store.startEdit(msg({ id: 'edit-me' }));
    expect(store.editingMessage()?.id).toBe('edit-me');
    expect(store.replyTarget()).toBeNull();
  });

  it('setReplyTarget clears editingMessage', () => {
    const store = setup();
    store.startEdit(msg({ id: 'edit-me' }));
    store.setReplyTarget(msg({ id: 'reply', mine: false }));
    expect(store.editingMessage()).toBeNull();
    expect(store.replyTarget()?.id).toBe('reply');
  });

  it('rejects startEdit for non-own or non-persisted messages', () => {
    const store = setup();
    store.startEdit(msg({ mine: false }));
    expect(store.editingMessage()).toBeNull();

    store.startEdit(msg({ status: 'sending' }));
    expect(store.editingMessage()).toBeNull();

    store.startEdit(msg({ deletedAt: new Date().toISOString() }));
    expect(store.editingMessage()).toBeNull();
  });
});
