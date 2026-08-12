import '@angular/compiler';
import { Injector } from '@angular/core';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ChatHubService } from './chat-hub.service';
import { ChannelStore } from './channel.store';

describe('ChannelStore authenticated API failure', () => {
  it('does not replace a failed workspace request with demo data', async () => {
    const injector = Injector.create({
      providers: [
        ChannelStore,
        {
          provide: ApiService,
          useValue: { getWorkspaces: vi.fn().mockRejectedValue(new Error('API unavailable')) },
        },
        {
          provide: ChatHubService,
          useValue: { onReadCursorChanged: () => () => undefined },
        },
        {
          provide: AuthService,
          useValue: {
            isOfflineDemo: () => false,
            profile: () => ({ id: 'authenticated-user' }),
          },
        },
      ],
    });

    const store = injector.get(ChannelStore);
    await store.load();

    expect(store.isDemo()).toBe(false);
    expect(store.workspaces()).toEqual([]);
    expect(store.channels()).toEqual([]);
    expect(store.error()).toBe('Não foi possível carregar o workspace. Tente novamente.');
  });
});
