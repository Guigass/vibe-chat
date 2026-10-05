import { ChatMessage } from '../../shared/models/chat.models';
import { preferRicherPoll } from '../../shared/polls/poll-summary';
import {
  bumpChannelParentForThreadReply,
  findMessageByCorrelators,
  hasSeqGap,
  idsEqual,
  isOwnAuthor,
  upsertRemoteMessage,
} from './message-sync';
import { patchMessageByClientId } from './message-patches';

export interface MessageIngestDeps {
  messages: () => readonly ChatMessage[];
  updateMessages: (fn: (list: ChatMessage[]) => ChatMessage[]) => void;
  normalize: (message: ChatMessage) => ChatMessage;
  profileId: () => string | undefined;
  activeChannelId: () => string | null | undefined;
  viewingLatest: () => boolean;
  scheduleReadCursor: (channelId: string, seq: number) => void;
  gapFillChannel: (channelId: string) => Promise<void>;
  bumpMention: (channelId: string) => void;
  bumpUnread: (channelId: string) => void;
  bumpThreadReply: (threadId: string) => void;
}

/** Merge a hub/HTTP message into the channel timeline without duplicating an optimistic row. */
export function ingestChannelMessage(deps: MessageIngestDeps, message: ChatMessage): void {
  const normalized = deps.normalize(message);
  const isThreadReply =
    !!normalized.threadId &&
    normalized.conversationId !== normalized.channelId &&
    idsEqual(normalized.conversationId, normalized.threadId);

  if (isThreadReply) {
    deps.updateMessages((list) =>
      bumpChannelParentForThreadReply(list, {
        threadId: normalized.threadId!,
        parentMessageId: normalized.parentMessageId,
        replyToMessageId: normalized.replyToMessageId,
      }),
    );
    deps.bumpThreadReply(normalized.threadId!);
    return;
  }

  if (hasSeqGap(deps.messages(), normalized.channelId, normalized.seq)) {
    void deps.gapFillChannel(normalized.channelId);
  }

  const mine = isOwnAuthor(normalized.authorUserId, deps.profileId());
  const remote: ChatMessage = { ...normalized, mine, status: 'persisted' };
  const existing = findMessageByCorrelators(deps.messages(), remote);
  const isActive = idsEqual(normalized.channelId, deps.activeChannelId());

  if (existing) {
    if (existing.clientMessageId) {
      deps.updateMessages((list) =>
        patchMessageByClientId(list, existing.clientMessageId!, {
          ...remote,
          clientMessageId: existing.clientMessageId,
          mine,
          poll: preferRicherPoll(existing.poll, remote.poll),
        }),
      );
    } else {
      deps.updateMessages((list) => upsertRemoteMessage(list, remote));
    }
    if (isActive && deps.viewingLatest() && (normalized.seq ?? 0) > 0) {
      deps.scheduleReadCursor(normalized.channelId, normalized.seq!);
    }
    return;
  }

  deps.updateMessages((list) => upsertRemoteMessage(list, remote));

  if (isActive && deps.viewingLatest() && (normalized.seq ?? 0) > 0) {
    deps.scheduleReadCursor(normalized.channelId, normalized.seq!);
    return;
  }

  if (!mine) {
    if (normalized.mentionsMe) {
      deps.bumpMention(normalized.channelId);
    } else {
      deps.bumpUnread(normalized.channelId);
    }
  }
}
