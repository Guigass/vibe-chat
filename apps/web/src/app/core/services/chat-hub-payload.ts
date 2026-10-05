import { AnnouncementSummary, ChatMessage } from '../../shared/models/chat.models';
import { mapPollSummary } from '../../shared/polls/poll-summary';
import { AvailabilityKind, UserStatusStateName } from '../../shared/status/user-status';
import { MessageCreatedPayload } from './chat-hub.types';

export function coerceHubPayload<T extends object>(
  payload: T | string | null | undefined,
): T | null {
  if (payload == null) return null;
  if (typeof payload === 'string') {
    try {
      return JSON.parse(payload) as T;
    } catch {
      return null;
    }
  }
  return payload;
}

export function mapMessageCreated(
  payload: MessageCreatedPayload,
  me: string | undefined,
): ChatMessage | null {
  const raw = payload as MessageCreatedPayload & {
    AuthorId?: string;
    AuthorName?: string;
    MessageId?: string;
    Id?: string;
  };
  const id = payload.messageId ?? payload.id ?? raw.MessageId ?? raw.Id;
  if (!id || !payload.channelId) return null;
  const authorId = String(payload.authorId ?? raw.AuthorId ?? '');
  const authorName = payload.authorName || raw.AuthorName || authorId;
  const conversationId = String(payload.conversationId || payload.channelId);
  const channelId = String(payload.channelId);
  const mentionedUserIds = (payload.mentionedUserIds ?? []).map(String);
  const mentionsMe = !!me && mentionedUserIds.includes(me);
  const clientMessageId = payload.clientMessageId ? String(payload.clientMessageId) : undefined;
  return {
    id: String(id),
    clientMessageId,
    conversationId,
    channelId,
    authorUserId: authorId,
    authorName,
    authorIsBot: !!payload.authorIsBot,
    body: payload.body ?? '',
    createdAt: payload.createdAt ?? new Date().toISOString(),
    seq: payload.sequence,
    status: 'persisted',
    mine: !!me && me.toLowerCase() === authorId.toLowerCase(),
    mentionsMe,
    threadId: payload.threadId ? String(payload.threadId) : null,
    parentMessageId: payload.parentMessageId ? String(payload.parentMessageId) : null,
    replyToMessageId: payload.replyToMessageId ? String(payload.replyToMessageId) : null,
    replyTo: payload.replyTo?.messageId
      ? {
          messageId: String(payload.replyTo.messageId),
          authorName: payload.replyTo.authorName ?? '',
          preview: payload.replyTo.preview ?? '',
          deleted: !!payload.replyTo.deleted,
        }
      : null,
    forwardedFromMessageId: payload.forwardedFromMessageId
      ? String(payload.forwardedFromMessageId)
      : null,
    forwardedFromChannelId: payload.forwardedFromChannelId
      ? String(payload.forwardedFromChannelId)
      : null,
    movedFromMessageId: payload.movedFromMessageId ? String(payload.movedFromMessageId) : null,
    movedFromChannelId: payload.movedFromChannelId ? String(payload.movedFromChannelId) : null,
    forwardedFrom: payload.forwardedFrom?.messageId
      ? {
          messageId: String(payload.forwardedFrom.messageId),
          channelId: String(payload.forwardedFrom.channelId ?? ''),
          channelName: payload.forwardedFrom.channelName ?? '',
          authorName: payload.forwardedFrom.authorName ?? '',
          createdAt: payload.forwardedFrom.createdAt ?? new Date().toISOString(),
          isDirect: !!payload.forwardedFrom.isDirect,
        }
      : null,
    attachments: (payload.attachments ?? []).map((a) => ({
      id: String(a.id),
      fileName: a.fileName,
      contentType: a.contentType,
      sizeBytes: a.sizeBytes,
      status: 'Ready',
    })),
    poll: mapPollSummary(payload.poll),
    announcement: mapAnnouncementPayload(payload.announcement),
  };
}

export function mapAnnouncementPayload(value: unknown): AnnouncementSummary | null {
  if (!value || typeof value !== 'object') return null;
  const row = value as Partial<AnnouncementSummary>;
  if (!row.messageId) return null;
  return {
    messageId: String(row.messageId),
    requiresAcknowledgement: !!row.requiresAcknowledgement,
    acknowledgeBy: row.acknowledgeBy ?? null,
    closedAt: row.closedAt ?? null,
    acknowledgedByMe: !!row.acknowledgedByMe,
    acknowledgementCount: row.acknowledgementCount ?? 0,
    canAcknowledge: !!row.canAcknowledge,
    canViewReport: !!row.canViewReport,
  };
}

export function isAvailability(value: string): value is AvailabilityKind {
  return (
    value === 'available' ||
    value === 'away' ||
    value === 'busy' ||
    value === 'vacation' ||
    value === 'offline'
  );
}

export function isStatusState(value: string | undefined): value is UserStatusStateName {
  return value === 'focus' || value === 'meeting' || value === 'vacation' || value === 'custom';
}
