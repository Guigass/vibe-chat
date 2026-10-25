import { Injectable } from '@angular/core';

const DATABASE = 'vibechat-offline';
const STORE = 'partitions';

@Injectable({ providedIn: 'root' })
export class OfflineDatabase {
  read(partition: string): Promise<string | null> {
    return this.request<string>('readonly', (store) => store.get(partition));
  }

  write(partition: string, ciphertext: string): Promise<void> {
    return this.request('readwrite', (store) => store.put(ciphertext, partition)).then(() => undefined);
  }

  readKey(partition: string): Promise<CryptoKey | null> {
    return this.request<CryptoKey>('readonly', (store) => store.get(`key:${partition}`));
  }

  writeKey(partition: string, key: CryptoKey): Promise<void> {
    return this.request('readwrite', (store) => store.put(key, `key:${partition}`)).then(() => undefined);
  }

  async dropAll(): Promise<void> {
    if (typeof indexedDB === 'undefined') return;
    await new Promise<void>((resolve, reject) => {
      const request = indexedDB.deleteDatabase(DATABASE);
      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
      request.onblocked = () => resolve();
    });
  }

  private async request<T>(
    mode: IDBTransactionMode,
    run: (store: IDBObjectStore) => IDBRequest<T>,
  ): Promise<T | null> {
    if (typeof indexedDB === 'undefined') return null;
    const db = await open();
    try {
      return await new Promise<T | null>((resolve, reject) => {
        const tx = db.transaction(STORE, mode);
        const request = run(tx.objectStore(STORE));
        request.onsuccess = () => resolve((request.result ?? null) as T | null);
        request.onerror = () => reject(request.error);
      });
    } finally {
      db.close();
    }
  }
}

function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE);
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}
