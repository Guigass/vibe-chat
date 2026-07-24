import { ChatMessage } from '../../shared/models/chat.models';

/** Highest persisted sequence for a channel (ignores optimistic/sending without seq). */
export function maxSeqForChannel(messages: readonly ChatMessage[], channelId: string): number {
  let max = 0;
  for (const message of messages) {
    if (message.channelId !== channelId) continue;
    const seq = message.seq ?? 0;
    if (seq > max) max = seq;
  }
  return max;
}

/** True when an incoming seq skips ahead of the local contiguous tip. */
export function hasSeqGap(
  messages: readonly ChatMessage[],
  channelId: string,
  incomingSeq: number | undefined,
): boolean {
  if (incomingSeq == null || incomingSeq <= 0) return false;
  const localMax = maxSeqForChannel(messages, channelId);
  if (localMax <= 0) return false;
  return incomingSeq > localMax + 1;
}

/**
 * Upsert by id. Preserves clientMessageId / optimistic mine flags.
 * Prefer incoming body/edit/delete/reactions (history is source of truth).
 */
export function mergeMessagesById(
  current: readonly ChatMessage[],
  incoming: readonly ChatMessage[],
): ChatMessage[] {
  const byId = new Map<string, ChatMessage>();
  for (const message of current) {
    byId.set(message.id, message);
  }

  for (const raw of incoming) {
    const existing = byId.get(raw.id);
    if (!existing) {
      byId.set(raw.id, raw);
      continue;
    }

    byId.set(raw.id, {
      ...existing,
      ...raw,
      clientMessageId: existing.clientMessageId ?? raw.clientMessageId,
      mine: existing.mine || raw.mine,
      status:
        raw.status === 'persisted' || existing.status === 'sending' || existing.status === 'failed'
          ? (raw.status ?? 'persisted')
          : (existing.status ?? raw.status ?? 'persisted'),
    });
  }

  return [...byId.values()];
}

/** Overlap window so reconnect also refreshes recent edit/delete/reaction state. */
export function gapFillAfterSeq(localMaxSeq: number, overlap = 50): number {
  if (localMaxSeq <= 0) return 0;
  return Math.max(0, localMaxSeq - overlap);
}
