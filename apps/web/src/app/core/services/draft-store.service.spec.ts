/** @vitest-environment jsdom */
import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TenantContext } from '../tenant/tenant-context';
import { DRAFT_DEBOUNCE_MS, DRAFT_EXPIRY_MS, DRAFT_LS_PREFIX, draftRecordKey } from './draft-storage';
import { DraftStoreService } from './draft-store.service';

type StorageMap = Record<string, string>;

function installLocalStorage(initial: StorageMap = {}): StorageMap {
  const storage: StorageMap = { ...initial };
  const api = {
    get length() {
      return Object.keys(storage).length;
    },
    key(index: number) {
      return Object.keys(storage)[index] ?? null;
    },
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
  };
  vi.stubGlobal('localStorage', api);
  return storage;
}

describe('DraftStoreService (B-086)', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    installLocalStorage();
    // Force localStorage backend (no IDB in unit tests).
    vi.stubGlobal('indexedDB', undefined);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  async function createService(tenantId = 'tenant-a', userId = 'user-a'): Promise<{
    drafts: DraftStoreService;
    tenant: TenantContext;
  }> {
    TestBed.configureTestingModule({
      providers: [TenantContext, DraftStoreService],
    });
    const tenant = TestBed.inject(TenantContext);
    tenant.setContext({ tenantId, userId, roles: [], displayName: 'Test' });
    const drafts = TestBed.inject(DraftStoreService);
    await vi.advanceTimersByTimeAsync(0);
    return { drafts, tenant };
  }

  it('persists with debounce and composite key in localStorage fallback', async () => {
    const { drafts } = await createService();
    drafts.scheduleSave('channel-1', { body: 'rascunho' });
    expect(localStorage.length).toBe(0);

    await vi.advanceTimersByTimeAsync(DRAFT_DEBOUNCE_MS);
    const key = DRAFT_LS_PREFIX + draftRecordKey('tenant-a', 'user-a', 'channel-1');
    expect(localStorage.getItem(key)).toContain('rascunho');
    expect(drafts.hasDraft('channel-1')).toBe(true);

    const loaded = await drafts.get('channel-1');
    expect(loaded?.body).toBe('rascunho');
  });

  it('does not share drafts across tenants or users', async () => {
    const { drafts, tenant } = await createService('tenant-a', 'user-a');
    await drafts.saveNow('channel-1', { body: 'para-a' });

    tenant.setContext({ tenantId: 'tenant-b', userId: 'user-a' });
    await vi.advanceTimersByTimeAsync(0);
    await drafts.refreshIndex();
    expect(await drafts.get('channel-1')).toBeNull();
    expect(drafts.hasDraft('channel-1')).toBe(false);

    tenant.setContext({ tenantId: 'tenant-a', userId: 'user-b' });
    await vi.advanceTimersByTimeAsync(0);
    await drafts.refreshIndex();
    expect(await drafts.get('channel-1')).toBeNull();
  });

  it('clearUser removes only that user drafts', async () => {
    const { drafts } = await createService('tenant-a', 'user-a');
    await drafts.saveNow('channel-1', { body: 'keep-me-out' });
    expect(drafts.hasDraft('channel-1')).toBe(true);

    await drafts.clearUser('tenant-a', 'user-a');
    expect(drafts.hasDraft('channel-1')).toBe(false);
    expect(await drafts.get('channel-1')).toBeNull();
  });

  it('drops expired drafts on get', async () => {
    const { drafts } = await createService();
    const key = DRAFT_LS_PREFIX + draftRecordKey('tenant-a', 'user-a', 'channel-1');
    localStorage.setItem(
      key,
      JSON.stringify({
        tenantId: 'tenant-a',
        userId: 'user-a',
        conversationId: 'channel-1',
        body: 'old',
        attachments: [],
        updatedAt: Date.now() - DRAFT_EXPIRY_MS - 1000,
      }),
    );
    await drafts.refreshIndex();
    expect(await drafts.get('channel-1')).toBeNull();
  });

  it('remove clears indicator after successful send path', async () => {
    const { drafts } = await createService();
    await drafts.saveNow('channel-1', { body: 'going' });
    await drafts.remove('channel-1');
    expect(drafts.hasDraft('channel-1')).toBe(false);
  });
});
