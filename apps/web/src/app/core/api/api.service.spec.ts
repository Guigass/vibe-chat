import { Injector } from '@angular/core';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { AdminApiService } from './admin-api.service';
import { AiApiService } from './ai-api.service';
import { ApiService } from './api.service';
import { DirectoryApiService } from './directory-api.service';
import { FilesApiService } from './files-api.service';
import { MessagingApiService } from './messaging-api.service';
import { NotificationsApiService } from './notifications-api.service';
import { ProfileApiService } from './profile-api.service';
import { SearchApiService } from './search-api.service';

interface AuthStub {
  devUser: () => string | null;
  getAccessToken: () => Promise<string | null>;
  profile: () => { id: string };
}

function createApi(auth: AuthStub): ApiService {
  const injector = Injector.create({
    providers: [
      ApiService,
      ProfileApiService,
      DirectoryApiService,
      MessagingApiService,
      FilesApiService,
      AdminApiService,
      NotificationsApiService,
      SearchApiService,
      AiApiService,
      { provide: AuthService, useValue: auth },
    ],
  });
  return injector.get(ApiService);
}

function publicMethods(ctor: abstract new (...args: never[]) => object): string[] {
  return Object.getOwnPropertyNames(ctor.prototype).filter((name) => {
    if (name === 'constructor' || name.startsWith('map')) {
      return false;
    }
    return typeof (ctor.prototype as Record<string, unknown>)[name] === 'function';
  });
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(status === 204 ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('ApiService facade', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('delegates every domain call', () => {
    const api = createApi({
      devUser: () => null,
      getAccessToken: async () => null,
      profile: () => ({ id: 'user-1' }),
    });
    const domains = [
      ProfileApiService,
      DirectoryApiService,
      MessagingApiService,
      FilesApiService,
      AdminApiService,
      NotificationsApiService,
      SearchApiService,
      AiApiService,
    ];
    const names = domains.flatMap((domain) => publicMethods(domain));
    expect(names.length).toBeGreaterThan(100);
    for (const name of names) {
      expect(typeof (api as unknown as Record<string, unknown>)[name], name).toBe('function');
    }
  });

  it('sends the dev user header and keeps idempotency on send', async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({
        id: 'm1',
        channelId: 'c1',
        sequence: 1,
        authorId: 'user-1',
        body: 'oi',
        createdAt: '2026-10-02T00:00:00.000Z',
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const api = createApi({
      devUser: () => 'alice',
      getAccessToken: async () => 'should-not-be-used',
      profile: () => ({ id: 'user-1' }),
    });

    const message = await api.sendMessage({
      channelId: 'c1',
      body: 'oi',
      clientMessageId: 'client-1',
      idempotencyKey: 'idem-1',
    });

    expect(message.mine).toBe(true);
    expect(message.seq).toBe(1);
    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://localhost:5080/api/v1/channels/c1/messages');
    const headers = new Headers(init.headers);
    expect(headers.get('X-Dev-User')).toBe('alice');
    expect(headers.get('Authorization')).toBeNull();
    expect(headers.get('Accept')).toBe('application/json');
    expect(headers.get('Content-Type')).toBe('application/json');
    expect(JSON.parse(String(init.body))).toMatchObject({
      messageId: 'client-1',
      idempotencyKey: 'idem-1',
      body: 'oi',
    });
  });

  it('keeps the idempotency header on template apply', async () => {
    const fetchMock = vi.fn(async () => jsonResponse({ steps: [] }));
    vi.stubGlobal('fetch', fetchMock);
    const api = createApi({
      devUser: () => 'alice',
      getAccessToken: async () => null,
      profile: () => ({ id: 'user-1' }),
    });

    await api.applyWorkspaceTemplate('ws-1', { templateId: 't1' }, 'idem-header');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://localhost:5080/api/v1/admin/workspaces/ws-1/templates/apply');
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('idem-header');
  });

  it('sends a bearer token when dev auth is absent', async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({
        userId: 'user-1',
        subject: 'sub',
        email: 'alice@vibechat.local',
        displayName: 'Alice',
        roles: ['Member'],
        locale: 'pt-BR',
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const api = createApi({
      devUser: () => null,
      getAccessToken: async () => 'token-1',
      profile: () => ({ id: 'user-1' }),
    });

    await api.getMe();

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get('Authorization')).toBe('Bearer token-1');
    expect(headers.get('X-Dev-User')).toBeNull();
  });

  it('returns undefined on 204 and preserves the http status on failure', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(null, 204))
      .mockResolvedValueOnce(new Response('nope', { status: 403 }));
    vi.stubGlobal('fetch', fetchMock);
    const api = createApi({
      devUser: () => 'bob',
      getAccessToken: async () => null,
      profile: () => ({ id: 'user-2' }),
    });

    await expect(api.deleteMessage('c1', 'm1')).resolves.toBeUndefined();
    await expect(api.deleteMessage('c1', 'm2')).rejects.toMatchObject({ status: 403 });
  });
});
