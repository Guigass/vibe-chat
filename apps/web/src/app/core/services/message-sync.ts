import { ChatMessage } from '../../shared/models/chat.models';

/** Case-insensitive GUID/string id compare (hub/API casing may differ). */
export function idsEqual(a: string | undefined | null, b: string | undefined | null): boolean {
  if (!a || !b) return false;
  return a.toLowerCase() === b.toLowerCase();
}

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
 * Match optimistic/local row to an incoming hub/HTTP message by id or clientMessageId.
 */
export function findMessageByCorrelators(
  messages: readonly ChatMessage[],
  incoming: Pick<ChatMessage, 'id' | 'clientMessageId'>,
): ChatMessage | undefined {
  return messages.find(
    (m) =>
      idsEqual(m.id, incoming.id) ||
      idsEqual(m.clientMessageId, incoming.id) ||
      (!!incoming.clientMessageId &&
        (idsEqual(m.clientMessageId, incoming.clientMessageId) ||
          idsEqual(m.id, incoming.clientMessageId))),
  );
}

/**
 * Upsert by id. Preserves clientMessageId / optimistic mine flags.
 * Prefer incoming body/edit/delete/reactions (history is source of truth).
 * Keys are compared case-insensitively.
 */
export function mergeMessagesById(
  current: readonly ChatMessage[],
  incoming: readonly ChatMessage[],
): ChatMessage[] {
  const byId = new Map<string, ChatMessage>();
  for (const message of current) {
    byId.set(message.id.toLowerCase(), message);
  }

  for (const raw of incoming) {
    const key = raw.id.toLowerCase();
    const existing = byId.get(key);
    if (!existing) {
      byId.set(key, raw);
      continue;
    }

    byId.set(key, {
      ...existing,
      ...raw,
      id: existing.id,
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

/**
 * Reconcile a remote MessageCreated (or HTTP ack) into the local list without duplicating
 * an optimistic bubble that shares id / clientMessageId.
 */
export function upsertRemoteMessage(
  current: readonly ChatMessage[],
  incoming: ChatMessage,
): ChatMessage[] {
  const existing = findMessageByCorrelators(current, incoming);
  if (!existing) {
    return [...current, incoming];
  }

  return mergeMessagesById(current, [
    {
      ...incoming,
      id: existing.id,
      clientMessageId: existing.clientMessageId ?? incoming.clientMessageId,
      mine: existing.mine || incoming.mine,
      status: incoming.status ?? 'persisted',
    },
  ]);
}

/** Overlap window so reconnect also refreshes recent edit/delete/reaction state. */
export function gapFillAfterSeq(localMaxSeq: number, overlap = 50): number {
  if (localMaxSeq <= 0) return 0;
  return Math.max(0, localMaxSeq - overlap);
}

/**
 * On a thread reply fan-out: locate the channel parent (by threadId and/or parentMessageId),
 * attach threadId if missing, and increment replyCount (BUG-009).
 */
export function bumpChannelParentForThreadReply(
  messages: readonly ChatMessage[],
  reply: {
    threadId: string;
    parentMessageId?: string | null;
    replyToMessageId?: string | null;
  },
): ChatMessage[] {
  const threadId = reply.threadId;
  const parentHint = reply.parentMessageId || reply.replyToMessageId || null;

  return messages.map((m) => {
    const isChannelMessage =
      !!m.channelId && (m.conversationId === m.channelId || !m.conversationId);
    if (!isChannelMessage) return m;

    const matchByThread = !!m.threadId && idsEqual(m.threadId, threadId);
    const matchByParentHint = !!parentHint && idsEqual(m.id, parentHint);
    if (!matchByThread && !matchByParentHint) return m;

    return {
      ...m,
      threadId: m.threadId ?? threadId,
      replyCount: (m.replyCount ?? 0) + 1,
    };
  });
}

/** Mark reply-quote previews as deleted when the cited message is soft-deleted. */
export function markReplyQuotesDeleted(
  messages: readonly ChatMessage[],
  deletedMessageId: string,
): ChatMessage[] {
  return messages.map((m) => {
    if (!m.replyTo || !idsEqual(m.replyTo.messageId, deletedMessageId)) return m;
    if (m.replyTo.deleted) return m;
    return {
      ...m,
      replyTo: {
        ...m.replyTo,
        preview: '',
        deleted: true,
      },
    };
  });
}

/** Plain-text preview for composer quote bar (max 140 chars). */
export function replyPreviewText(body: string, max = 140): string {
  const flat = body.replace(/\s+/g, ' ').trim();
  if (flat.length <= max) return flat;
  return `${flat.slice(0, max - 1)}…`;
}
