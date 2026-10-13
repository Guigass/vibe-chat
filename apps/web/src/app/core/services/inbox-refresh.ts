import { signal } from '@angular/core';

/** Cross-store poke so a canonical read/save updates the inbox without a DI cycle. */
export const inboxEpoch = signal(0);

const listeners = new Set<() => void>();

export function pokeInbox(): void {
  inboxEpoch.update((value) => value + 1);
  for (const listener of listeners) {
    listener();
  }
}

export function onInboxPoke(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
