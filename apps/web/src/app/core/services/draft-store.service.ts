import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { TenantContext } from '../tenant/tenant-context';
import {
  ConversationDraft,
  DRAFT_DEBOUNCE_MS,
  DRAFT_IDB_NAME,
  DRAFT_IDB_STORE,
  DRAFT_IDB_VERSION,
  DRAFT_LS_PREFIX,
  DraftAttachmentMeta,
  DraftRecord,
  draftRecordKey,
  isDraftEmpty,
  isDraftExpired,
  normalizeDraftInput,
  parseLocalStorageDraft,
  pruneDraftRecords,
} from './draft-storage';

type StorageBackend = 'idb' | 'localStorage';

@Injectable({ providedIn: 'root' })
export class DraftStoreService {
  private readonly tenant = inject(TenantContext);

  private backend: StorageBackend | null = null;
  private idb: IDBDatabase | null = null;
  private initPromise: Promise<void> | null = null;
  private readonly debounceTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly draftKeysSignal = signal<ReadonlySet<string>>(new Set());

  /** Conversation ids (channel or thread:*) that currently have a draft for the active user. */
  readonly draftConversationIds = this.draftKeysSignal.asReadonly();
  readonly hasAnyDraft = computed(() => this.draftKeysSignal().size > 0);

  constructor() {
    effect(() => {
      this.tenant.tenantId();
      this.tenant.userId();
      untracked(() => {
        void this.ensureReady().then(() => this.refreshIndex());
      });
    });
  }

  hasDraft(conversationId: string): boolean {
    return this.draftKeysSignal().has(conversationId);
  }

  async get(conversationId: string): Promise<ConversationDraft | null> {
    const scope = this.scope();
    if (!scope) return null;
    await this.ensureReady();
    const record = await this.readRecord(scope.tenantId, scope.userId, conversationId);
    if (!record) return null;
    if (isDraftExpired(record)) {
      await this.remove(conversationId);
      return null;
    }
    return {
      body: record.body,
      attachments: record.attachments,
      updatedAt: record.updatedAt,
      selectionStart: record.selectionStart,
      selectionEnd: record.selectionEnd,
    };
  }

  /** Debounced persist; empty draft removes the record. */
  scheduleSave(
    conversationId: string,
    input: {
      body: string;
      attachments?: DraftAttachmentMeta[];
      selectionStart?: number;
      selectionEnd?: number;
    },
  ): void {
    const scope = this.scope();
    if (!scope || !conversationId) return;
    const key = draftRecordKey(scope.tenantId, scope.userId, conversationId);
    const existing = this.debounceTimers.get(key);
    if (existing) clearTimeout(existing);
    this.debounceTimers.set(
      key,
      setTimeout(() => {
        this.debounceTimers.delete(key);
        void this.saveNow(conversationId, input);
      }, DRAFT_DEBOUNCE_MS),
    );
  }

  /** Immediate persist (channel switch / before unload). */
  async saveNow(
    conversationId: string,
    input: {
      body: string;
      attachments?: DraftAttachmentMeta[];
      selectionStart?: number;
      selectionEnd?: number;
    },
  ): Promise<void> {
    const scope = this.scope();
    if (!scope || !conversationId) return;
    this.cancelDebounce(scope.tenantId, scope.userId, conversationId);

    const normalized = normalizeDraftInput(input);
    if (!normalized) {
      await this.remove(conversationId);
      return;
    }

    await this.ensureReady();
    const record: DraftRecord = {
      ...normalized,
      tenantId: scope.tenantId,
      userId: scope.userId,
      conversationId,
    };
    await this.writeRecord(record);
    await this.pruneUser(scope.tenantId, scope.userId);
    this.markPresent(conversationId, true);
  }

  async remove(conversationId: string): Promise<void> {
    const scope = this.scope();
    if (!scope || !conversationId) return;
    this.cancelDebounce(scope.tenantId, scope.userId, conversationId);
    await this.ensureReady();
    await this.deleteRecord(scope.tenantId, scope.userId, conversationId);
    this.markPresent(conversationId, false);
  }

  /** Wipe all drafts for a user (logout — required for shared machines). */
  async clearUser(tenantId: string, userId: string): Promise<void> {
    if (!tenantId || !userId) return;
    for (const [key, timer] of this.debounceTimers) {
      if (key.startsWith(`${tenantId}|${userId}|`)) {
        clearTimeout(timer);
        this.debounceTimers.delete(key);
      }
    }
    await this.ensureReady();
    const records = await this.listUserRecords(tenantId, userId);
    for (const record of records) {
      await this.deleteRecord(tenantId, userId, record.conversationId);
    }
    const scope = this.scope();
    if (scope?.tenantId === tenantId && scope.userId === userId) {
      this.draftKeysSignal.set(new Set());
    }
  }

  async refreshIndex(): Promise<void> {
    const scope = this.scope();
    if (!scope) {
      this.draftKeysSignal.set(new Set());
      return;
    }
    await this.ensureReady();
    await this.pruneUser(scope.tenantId, scope.userId);
    const records = await this.listUserRecords(scope.tenantId, scope.userId);
    this.draftKeysSignal.set(new Set(records.map((r) => r.conversationId)));
  }

  private scope(): { tenantId: string; userId: string } | null {
    const tenantId = this.tenant.tenantId();
    const userId = this.tenant.userId();
    if (!tenantId || !userId) return null;
    return { tenantId, userId };
  }

  private cancelDebounce(tenantId: string, userId: string, conversationId: string): void {
    const key = draftRecordKey(tenantId, userId, conversationId);
    const timer = this.debounceTimers.get(key);
    if (timer) {
      clearTimeout(timer);
      this.debounceTimers.delete(key);
    }
  }

  private markPresent(conversationId: string, present: boolean): void {
    const next = new Set(this.draftKeysSignal());
    if (present) next.add(conversationId);
    else next.delete(conversationId);
    this.draftKeysSignal.set(next);
  }

  private async ensureReady(): Promise<void> {
    if (this.backend) return;
    if (!this.initPromise) {
      this.initPromise = this.openBackend();
    }
    await this.initPromise;
  }

  private async openBackend(): Promise<void> {
    if (typeof indexedDB === 'undefined') {
      this.backend = 'localStorage';
      return;
    }
    try {
      this.idb = await openDraftDb();
      this.backend = 'idb';
    } catch {
      this.backend = 'localStorage';
      this.idb = null;
    }
  }

  private async readRecord(
    tenantId: string,
    userId: string,
    conversationId: string,
  ): Promise<DraftRecord | null> {
    const key = draftRecordKey(tenantId, userId, conversationId);
    if (this.backend === 'idb' && this.idb) {
      return idbGet(this.idb, key);
    }
    return parseLocalStorageDraft(localStorage.getItem(DRAFT_LS_PREFIX + key));
  }

  private async writeRecord(record: DraftRecord): Promise<void> {
    const key = draftRecordKey(record.tenantId, record.userId, record.conversationId);
    if (this.backend === 'idb' && this.idb) {
      try {
        await idbPut(this.idb, key, record);
        return;
      } catch {
        this.backend = 'localStorage';
      }
    }
    localStorage.setItem(DRAFT_LS_PREFIX + key, JSON.stringify(record));
  }

  private async deleteRecord(
    tenantId: string,
    userId: string,
    conversationId: string,
  ): Promise<void> {
    const key = draftRecordKey(tenantId, userId, conversationId);
    if (this.backend === 'idb' && this.idb) {
      try {
        await idbDelete(this.idb, key);
      } catch {
        /* fall through to LS cleanup */
      }
    }
    localStorage.removeItem(DRAFT_LS_PREFIX + key);
  }

  private async listUserRecords(tenantId: string, userId: string): Promise<DraftRecord[]> {
    if (this.backend === 'idb' && this.idb) {
      const all = await idbGetAll(this.idb);
      return all.filter((r) => r.tenantId === tenantId && r.userId === userId);
    }
    const out: DraftRecord[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const storageKey = localStorage.key(i);
      if (!storageKey?.startsWith(DRAFT_LS_PREFIX)) continue;
      const record = parseLocalStorageDraft(localStorage.getItem(storageKey));
      if (record && record.tenantId === tenantId && record.userId === userId) {
        out.push(record);
      }
    }
    return out;
  }

  private async pruneUser(tenantId: string, userId: string): Promise<void> {
    const records = await this.listUserRecords(tenantId, userId);
    const kept = pruneDraftRecords(records);
    const keptKeys = new Set(kept.map((r) => draftRecordKey(r.tenantId, r.userId, r.conversationId)));
    for (const record of records) {
      const key = draftRecordKey(record.tenantId, record.userId, record.conversationId);
      if (!keptKeys.has(key) || isDraftEmpty(record)) {
        await this.deleteRecord(record.tenantId, record.userId, record.conversationId);
      }
    }
  }
}

function openDraftDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DRAFT_IDB_NAME, DRAFT_IDB_VERSION);
    request.onerror = () => reject(request.error ?? new Error('IndexedDB open failed'));
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(DRAFT_IDB_STORE)) {
        db.createObjectStore(DRAFT_IDB_STORE);
      }
    };
    request.onsuccess = () => resolve(request.result);
  });
}

function idbGet(db: IDBDatabase, key: string): Promise<DraftRecord | null> {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(DRAFT_IDB_STORE, 'readonly');
    const req = tx.objectStore(DRAFT_IDB_STORE).get(key);
    req.onerror = () => reject(req.error);
    req.onsuccess = () => resolve((req.result as DraftRecord | undefined) ?? null);
  });
}

function idbPut(db: IDBDatabase, key: string, record: DraftRecord): Promise<void> {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(DRAFT_IDB_STORE, 'readwrite');
    const req = tx.objectStore(DRAFT_IDB_STORE).put(record, key);
    req.onerror = () => reject(req.error);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

function idbDelete(db: IDBDatabase, key: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(DRAFT_IDB_STORE, 'readwrite');
    const req = tx.objectStore(DRAFT_IDB_STORE).delete(key);
    req.onerror = () => reject(req.error);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

function idbGetAll(db: IDBDatabase): Promise<DraftRecord[]> {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(DRAFT_IDB_STORE, 'readonly');
    const req = tx.objectStore(DRAFT_IDB_STORE).getAll();
    req.onerror = () => reject(req.error);
    req.onsuccess = () => resolve((req.result as DraftRecord[]) ?? []);
  });
}
