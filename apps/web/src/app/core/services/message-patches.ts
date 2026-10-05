import {
  AnnouncementSummary,
  ChatMessage,
  PollSummary,
} from '../../shared/models/chat.models';
import { mergeRemotePoll } from '../../shared/polls/poll-summary';
import { compareMessagesBySeq, idsEqual, isOwnAuthor, markReplyQuotesDeleted, mergeMessagesById } from './message-sync';

export interface ChannelPaginationState {
  hasMoreBefore: boolean;
  hasMoreAfter: boolean;
  loadingOlder: boolean;
  loadError: string | null;
}

export interface MessageScrollRequest {
  requestId: number;
  channelId: string;
  messageId: string;
}

export const defaultPagination = (): ChannelPaginationState => ({
  hasMoreBefore: false,
  hasMoreAfter: false,
  loadingOlder: false,
  loadError: null,
});

/** Channel timeline: drop thread replies that live in another conversation, then sort by seq. */
export function channelTimeline(messages: readonly ChatMessage[], channelId: string): ChatMessage[] {
  return messages
    .filter(
      (m) =>
        idsEqual(m.channelId, channelId) &&
        (!m.threadId ||
          idsEqual(m.conversationId, channelId) ||
          idsEqual(m.conversationId, m.channelId)),
    )
    .sort(compareMessagesBySeq);
}

export function normalizeChannelMessage(
  message: ChatMessage,
  me: string | undefined,
): ChatMessage {
  const mentionsMe =
    message.mentionsMe ?? (!!me && !!message.body && message.body.includes(`<@${me}>`));
  return {
    ...message,
    channelId: message.channelId || message.conversationId,
    conversationId: message.conversationId || message.channelId,
    status: message.status ?? 'persisted',
    mine: isOwnAuthor(message.authorUserId, me),
    mentionsMe,
    authorName: message.authorName || 'Membro',
    body: message.deletedAt ? '' : message.body,
    replyCount: message.replyCount ?? 0,
    reactions: message.reactions ?? [],
    replyTo: message.replyTo ?? null,
    isPinned: message.isPinned ?? false,
    isSaved: message.isSaved ?? false,
  };
}

export function applyMessageEdit(
  list: readonly ChatMessage[],
  patch: { id: string; body: string; editedAt: string; seq?: number },
): ChatMessage[] {
  return list.map((m) =>
    m.id === patch.id
      ? {
          ...m,
          body: patch.body,
          editedAt: patch.editedAt,
          seq: patch.seq ?? m.seq,
          deletedAt: null,
        }
      : m,
  );
}

export function applyMessageDelete(
  list: readonly ChatMessage[],
  patch: { id: string; deletedAt: string; seq?: number },
): ChatMessage[] {
  const deleted = list.map((m) =>
    idsEqual(m.id, patch.id)
      ? {
          ...m,
          body: '',
          deletedAt: patch.deletedAt,
          seq: patch.seq ?? m.seq,
        }
      : m,
  );
  return markReplyQuotesDeleted(deleted, patch.id);
}

export function applyMessageReactions(
  list: readonly ChatMessage[],
  messageId: string,
  reactions: Array<{ emoji: string; count: number; me: boolean }>,
): ChatMessage[] {
  return list.map((m) => (m.id === messageId ? { ...m, reactions: [...reactions] } : m));
}

export function toggleLocalReaction(
  list: readonly ChatMessage[],
  messageId: string,
  emoji: string,
): ChatMessage[] {
  return list.map((m) => {
    if (m.id !== messageId) return m;
    const current = [...(m.reactions ?? [])];
    const idx = current.findIndex((r) => r.emoji === emoji);
    if (idx >= 0) {
      const item = current[idx];
      if (item.me) {
        if (item.count <= 1) current.splice(idx, 1);
        else current[idx] = { ...item, count: item.count - 1, me: false };
      } else {
        current[idx] = { ...item, count: item.count + 1, me: true };
      }
    } else {
      current.push({ emoji, count: 1, me: true });
    }
    return { ...m, reactions: current };
  });
}

export function applyThumbnailToMessages(
  list: readonly ChatMessage[],
  event: {
    attachmentId: string;
    channelId: string;
    thumbnailStatus: string | null;
    width?: number | null;
    height?: number | null;
    pageCount?: number | null;
  },
): ChatMessage[] {
  return list.map((m) => {
    if (!idsEqual(m.channelId, event.channelId) || !m.attachments?.length) {
      return m;
    }
    let changed = false;
    const attachments = m.attachments.map((a) => {
      if (!idsEqual(a.id, event.attachmentId)) return a;
      changed = true;
      return {
        ...a,
        thumbnailStatus: event.thumbnailStatus,
        width: event.width ?? a.width,
        height: event.height ?? a.height,
        pageCount: event.pageCount ?? a.pageCount,
      };
    });
    return changed ? { ...m, attachments } : m;
  });
}

export function applyLinkPreviewToMessages(
  list: readonly ChatMessage[],
  event: {
    channelId: string;
    messageId: string;
    linkPreviewId: string;
    url: string;
    title?: string | null;
    description?: string | null;
    siteName?: string | null;
    hasImage: boolean;
    status: string;
  },
): ChatMessage[] {
  return list.map((m) => {
    if (!idsEqual(m.id, event.messageId) || !idsEqual(m.channelId, event.channelId)) {
      return m;
    }
    return {
      ...m,
      linkPreview: {
        id: event.linkPreviewId,
        url: event.url,
        title: event.title ?? null,
        description: event.description ?? null,
        siteName: event.siteName ?? null,
        hasImage: event.hasImage,
        status: event.status,
      },
    };
  });
}

export function clearLinkPreview(list: readonly ChatMessage[], messageId: string): ChatMessage[] {
  return list.map((m) => (idsEqual(m.id, messageId) ? { ...m, linkPreview: null } : m));
}

export function patchMessageByClientId(
  list: readonly ChatMessage[],
  clientMessageId: string,
  patch: Partial<ChatMessage>,
): ChatMessage[] {
  return list.map((m) => (idsEqual(m.clientMessageId, clientMessageId) ? { ...m, ...patch } : m));
}

export function mergeChannelPage(
  current: readonly ChatMessage[],
  channelId: string,
  normalized: readonly ChatMessage[],
  mode: { replace?: boolean; prepend?: boolean } = {},
): ChatMessage[] {
  const others = current.filter((m) => m.channelId !== channelId);
  if (mode.replace) {
    return [...others, ...normalized];
  }
  const existing = current.filter((m) => m.channelId === channelId);
  const merged = mergeMessagesById(existing, normalized);
  return [...others, ...merged];
}

export function applyPollToMessages(
  list: readonly ChatMessage[],
  messageId: string,
  poll: PollSummary,
): ChatMessage[] {
  return list.map((message) =>
    idsEqual(message.id, messageId) || idsEqual(message.id, poll.messageId)
      ? { ...message, poll }
      : message,
  );
}

export function applyRemotePollToMessages(
  list: readonly ChatMessage[],
  messageId: string,
  incoming: PollSummary,
): ChatMessage[] {
  return list.map((message) => {
    if (!idsEqual(message.id, messageId) && !idsEqual(message.id, incoming.messageId)) {
      return message;
    }
    const merged = mergeRemotePoll(message.poll, incoming);
    return merged === message.poll ? message : { ...message, poll: merged };
  });
}

export function applyAnnouncementToMessages(
  list: readonly ChatMessage[],
  messageId: string,
  announcement: AnnouncementSummary,
): ChatMessage[] {
  return list.map((message) =>
    idsEqual(message.id, messageId) || idsEqual(message.announcement?.messageId, messageId)
      ? { ...message, announcement }
      : message,
  );
}

export function applyAnnouncementEventToMessages(
  list: readonly ChatMessage[],
  event: {
    messageId: string;
    acknowledgementCount?: number;
    acknowledgedByUserId?: string | null;
    closedAt?: string | null;
  },
  me: string | undefined,
): ChatMessage[] {
  return list.map((message) => {
    if (!idsEqual(message.id, event.messageId) || !message.announcement) return message;
    const mine = !!me && !!event.acknowledgedByUserId && idsEqual(event.acknowledgedByUserId, me);
    const announcement: AnnouncementSummary = {
      ...message.announcement,
      acknowledgementCount: event.acknowledgementCount ?? message.announcement.acknowledgementCount,
      acknowledgedByMe: message.announcement.acknowledgedByMe || mine,
      closedAt: event.closedAt ?? message.announcement.closedAt,
      canAcknowledge:
        message.announcement.requiresAcknowledgement &&
        !(event.closedAt ?? message.announcement.closedAt) &&
        !(message.announcement.acknowledgedByMe || mine),
    };
    return { ...message, announcement };
  });
}

export function setPinnedFlag(
  list: readonly ChatMessage[],
  channelId: string,
  messageId: string,
  pinned: boolean,
): ChatMessage[] {
  return list.map((m) =>
    m.channelId === channelId && m.id === messageId ? { ...m, isPinned: pinned } : m,
  );
}

export function applyPinnedIdSet(
  list: readonly ChatMessage[],
  channelId: string,
  messageIds: readonly string[],
): ChatMessage[] {
  const pinned = new Set(messageIds);
  return list.map((m) => (m.channelId === channelId ? { ...m, isPinned: pinned.has(m.id) } : m));
}

export function setSavedFlag(
  list: readonly ChatMessage[],
  messageId: string,
  saved: boolean,
): ChatMessage[] {
  return list.map((m) => (m.id === messageId ? { ...m, isSaved: saved } : m));
}

export function applySavedIdSet(list: readonly ChatMessage[], messageIds: readonly string[]): ChatMessage[] {
  const saved = new Set(messageIds);
  return list.map((m) => ({ ...m, isSaved: saved.has(m.id) }));
}

export function patchPaginationMap(
  map: Record<string, ChannelPaginationState>,
  channelId: string,
  patch: Partial<ChannelPaginationState>,
): Record<string, ChannelPaginationState> {
  return {
    ...map,
    [channelId]: { ...(map[channelId] ?? defaultPagination()), ...patch },
  };
}

export function markThreadOpenedOnMessages(
  list: readonly ChatMessage[],
  messageId: string,
  threadId: string,
): ChatMessage[] {
  return list.map((m) =>
    idsEqual(m.id, messageId) ? { ...m, threadId, replyCount: m.replyCount ?? 0 } : m,
  );
}

export function demoChannelMessages(channelId: string): ChatMessage[] {
  const now = Date.now();
  return [
    {
      id: `${channelId}-1`,
      conversationId: channelId,
      channelId,
      authorUserId: 'u-alice',
      authorName: 'Alice Mendes',
      body: 'Maré baixa no outbox — boa janela para deploy.',
      createdAt: new Date(now - 1000 * 60 * 42).toISOString(),
      status: 'persisted',
      mine: false,
      seq: 1,
    },
    {
      id: `${channelId}-2`,
      conversationId: channelId,
      channelId,
      authorUserId: 'u-bob',
      authorName: 'Bob Costa',
      body: 'SignalR reconectou limpo. Mantemos o banner só em reconnecting.',
      createdAt: new Date(now - 1000 * 60 * 18).toISOString(),
      status: 'persisted',
      mine: false,
      seq: 2,
    },
  ];
}
