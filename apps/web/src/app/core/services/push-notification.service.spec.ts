/** @vitest-environment jsdom */
import '@angular/compiler';
import { Injector, signal } from '@angular/core';
import { SwPush } from '@angular/service-worker';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';
import {
  PUSH_DISMISSED_KEY,
  PUSH_SENT_KEY,
  PushNotificationService,
} from './push-notification.service';
import type { ChatMessage } from '../../shared/models/chat.models';

type StorageMap = Record<string, string>;

function installLocalStorage(storage: StorageMap = {}): StorageMap {
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => storage[key] ?? null,
    setItem: (key: string, value: string) => {
      storage[key] = value;
    },
    removeItem: (key: string) => {
      delete storage[key];
    },
    clear: () => {
      for (const key of Object.keys(storage)) delete storage[key];
    },
  });
  return storage;
}

describe('PushNotificationService (B-095)', () => {
  let getPushPublicKey: ReturnType<typeof vi.fn>;
  let registerPushSubscription: ReturnType<typeof vi.fn>;
  let requestPermission: ReturnType<typeof vi.fn>;
  let requestSubscription: ReturnType<typeof vi.fn>;
  let messageHandler: ((message: ChatMessage) => void) | null;
  let activeChannelId: ReturnType<typeof signal<string | null>>;

  beforeEach(() => {
    messageHandler = null;
    activeChannelId = signal<string | null>('ch-active');
    getPushPublicKey = vi.fn();
    registerPushSubscription = vi.fn();
    requestPermission = vi.fn();
    requestSubscription = vi.fn();
    vi.stubGlobal('Notification', {
      permission: 'default',
      requestPermission,
    });
    vi.stubGlobal('document', {
      hasFocus: () => true,
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function createService(options?: {
    swEnabled?: boolean;
    storage?: StorageMap;
  }): PushNotificationService {
    installLocalStorage(options?.storage ?? {});
    const injector = Injector.create({
      providers: [
        PushNotificationService,
        {
          provide: ApiService,
          useValue: {
            getPushPublicKey,
            registerPushSubscription,
            listPushSubscriptions: vi.fn().mockResolvedValue([]),
            deletePushSubscription: vi.fn(),
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
            onMessage: (handler: (message: ChatMessage) => void) => {
              messageHandler = handler;
              return () => {
                messageHandler = null;
              };
            },
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            isDemo: () => false,
            activeChannelId: () => activeChannelId(),
            mentionLabels: () => ({
              '55555555-5555-5555-5555-555555555555': 'Bob',
            }),
            channels: () => [
              { id: 'ch-other', name: 'random', isDirect: false },
              {
                id: 'ch-dm',
                name: 'dm:44444444-4444-4444-4444-444444444444:55555555-5555-5555-5555-555555555555',
                isDirect: true,
                peerDisplayName: 'Bob',
              },
            ],
          },
        },
        {
          provide: SwPush,
          useValue: {
            isEnabled: options?.swEnabled ?? true,
            requestSubscription,
          },
        },
      ],
    });
    return injector.get(PushNotificationService);
  }

  it('does not ask permission when push is disabled', async () => {
    getPushPublicKey.mockResolvedValue({ enabled: false, publicKey: null });
    const service = createService();

    service.recordSuccessfulSend();
    await vi.waitFor(() => expect(getPushPublicKey).toHaveBeenCalled());
    expect(service.bannerOpen()).toBe(false);
    expect(requestPermission).not.toHaveBeenCalled();

    await service.enablePush();
    expect(requestPermission).not.toHaveBeenCalled();
    expect(registerPushSubscription).not.toHaveBeenCalled();
  });

  it('does not show the banner when the service worker is off', async () => {
    getPushPublicKey.mockResolvedValue({ enabled: true, publicKey: 'Btest' });
    const service = createService({ swEnabled: false });

    service.recordSuccessfulSend();
    await Promise.resolve();

    expect(service.bannerOpen()).toBe(false);
    expect(getPushPublicKey).not.toHaveBeenCalled();
  });

  it('opens the opt-in banner after the first send and Agora não persists', async () => {
    getPushPublicKey.mockResolvedValue({ enabled: true, publicKey: 'Btest' });
    const storage: StorageMap = {};
    const service = createService({ storage });

    service.recordSuccessfulSend();
    await vi.waitFor(() => expect(service.bannerOpen()).toBe(true));

    expect(storage[PUSH_SENT_KEY]).toBe('1');

    service.dismissBanner();
    expect(service.bannerOpen()).toBe(false);
    expect(storage[PUSH_DISMISSED_KEY]).toBe('1');

    service.recordSuccessfulSend();
    await Promise.resolve();
    expect(service.bannerOpen()).toBe(false);
  });

  it('shows an in-app notice for a mention on another focused channel', () => {
    const service = createService();
    expect(messageHandler).toBeTruthy();

    messageHandler!({
      id: 'm-1',
      conversationId: 'ch-other',
      channelId: 'ch-other',
      authorUserId: 'u-bob',
      authorName: 'Bob',
      body: 'alice olha isso',
      createdAt: '2026-08-13T12:00:00.000Z',
      status: 'persisted',
      mine: false,
      mentionsMe: true,
      seq: 9,
    });

    const notice = service.notice();
    expect(notice?.channelId).toBe('ch-other');
    expect(notice?.title).toBe('Bob · #random');
    expect(notice?.body).toContain('alice olha isso');
  });

  it('shows human names instead of ids in in-app notices', () => {
    const service = createService();
    const bobId = '55555555-5555-5555-5555-555555555555';

    messageHandler!({
      id: 'm-mention',
      conversationId: 'ch-other',
      channelId: 'ch-other',
      authorUserId: 'u-bob',
      authorName: 'Alice',
      body: `hey <@${bobId}> olha isso`,
      createdAt: '2026-08-13T12:00:00.000Z',
      status: 'persisted',
      mine: false,
      mentionsMe: true,
      seq: 10,
    });

    expect(service.notice()?.title).toBe('Alice · #random');
    expect(service.notice()?.body).toBe('hey @Bob olha isso');
    expect(service.notice()?.body).not.toContain(bobId);

    messageHandler!({
      id: 'm-dm',
      conversationId: 'ch-dm',
      channelId: 'ch-dm',
      authorUserId: 'u-alice',
      authorName: 'Alice',
      body: 'oi',
      createdAt: '2026-08-13T12:00:00.000Z',
      status: 'persisted',
      mine: false,
      seq: 11,
    });

    expect(service.notice()?.title).toBe('Alice');
    expect(service.notice()?.title).not.toMatch(/dm:/i);
  });
});
