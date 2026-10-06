import { PollSummary, PresenceStatus, ReactionSummary } from '../../shared/models/chat.models';
import { AvailabilityKind, UserStatusBody } from '../../shared/status/user-status';

export const AWAY_GRACE_MS = 120_000;

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface PresenceChangedEvent {
  userId: string;
  status: PresenceStatus;
  tenantId?: string;
}

export interface HubUserStatusEvent {
  userId: string;
  tenantId?: string;
  presence: PresenceStatus;
  availability: AvailabilityKind;
  status: UserStatusBody | null;
}

export interface MessageCreatedPayload {
  messageId?: string;
  id?: string;
  /** Echo of the client-supplied message id for optimistic UI reconciliation. */
  clientMessageId?: string;
  channelId: string;
  conversationId?: string;
  threadId?: string | null;
  parentMessageId?: string | null;
  replyToMessageId?: string | null;
  replyTo?: {
    messageId?: string;
    authorName?: string;
    preview?: string;
    deleted?: boolean;
  } | null;
  forwardedFromMessageId?: string | null;
  forwardedFromChannelId?: string | null;
  forwardedFrom?: {
    messageId?: string;
    channelId?: string;
    channelName?: string;
    authorName?: string;
    createdAt?: string;
    isDirect?: boolean;
  } | null;
  sequence?: number;
  authorId?: string;
  authorName?: string;
  authorIsBot?: boolean;
  body?: string;
  createdAt?: string;
  movedFromMessageId?: string;
  movedFromChannelId?: string;
  mentionedUserIds?: string[];
  mentionKinds?: string[];
  attachments?: Array<{
    id: string;
    fileName: string;
    contentType: string;
    sizeBytes: number;
  }>;
  poll?: unknown;
  announcement?: unknown;
}

export interface MessageEditedPayload {
  messageId?: string;
  id?: string;
  channelId: string;
  sequence?: number;
  body?: string;
  editedAt?: string;
}

export interface MessageDeletedPayload {
  messageId?: string;
  id?: string;
  channelId: string;
  sequence?: number;
  deletedAt?: string;
}

export interface MessageEditEvent {
  id: string;
  channelId: string;
  body: string;
  editedAt: string;
  seq?: number;
}

export interface MessageDeleteEvent {
  id: string;
  channelId: string;
  deletedAt: string;
  seq?: number;
}

export interface ReactionChangedEvent {
  messageId: string;
  channelId: string;
  emoji: string;
  userId: string;
  added: boolean;
  reactions: ReactionSummary[];
  topUsers?: string[];
}

export interface ScheduleNoticeEvent {
  kind: 'reminder' | 'scheduled';
  status: string;
  reveal: boolean;
}

export interface ScheduleNoticePayload {
  status?: string;
  reveal?: boolean;
}

export interface PollChangedEvent {
  messageId: string;
  channelId: string;
  poll: PollSummary;
}

export interface AnnouncementAcknowledgedEvent {
  messageId: string;
  channelId: string;
  acknowledgementCount?: number;
  acknowledgedByUserId?: string | null;
  closedAt?: string | null;
}

export interface PinChangedEvent {
  messageId: string;
  channelId: string;
  pinned: boolean;
  byUserId: string;
}

export interface AttachmentThumbnailReadyEvent {
  attachmentId: string;
  channelId: string;
  thumbnailStatus: string | null;
  width?: number | null;
  height?: number | null;
  pageCount?: number | null;
}

export interface LinkPreviewReadyEvent {
  tenantId?: string;
  channelId: string;
  messageId: string;
  linkPreviewId: string;
  url: string;
  title?: string | null;
  description?: string | null;
  siteName?: string | null;
  hasImage: boolean;
  status: string;
}

export interface ReadCursorChangedEvent {
  channelId: string;
  userId: string;
  lastReadSequence: number;
  tenantId?: string;
}

export interface ReactionChangedPayload {
  messageId?: string;
  channelId: string;
  emoji?: string;
  userId?: string;
  added?: boolean;
  topUsers?: string[];
  reactions?: Array<{ emoji: string; count: number; userIds?: string[]; me?: boolean }>;
}

export interface PinChangedPayload {
  messageId?: string;
  channelId?: string;
  pinned?: boolean;
  byUserId?: string;
}

export interface AttachmentThumbnailReadyPayload {
  attachmentId?: string;
  channelId?: string;
  thumbnailStatus?: string | null;
  width?: number | null;
  height?: number | null;
  pageCount?: number | null;
}

export interface LinkPreviewReadyPayload {
  tenantId?: string;
  channelId?: string;
  messageId?: string;
  linkPreviewId?: string;
  url?: string;
  title?: string | null;
  description?: string | null;
  siteName?: string | null;
  hasImage?: boolean;
  status?: string;
}

export interface ReadCursorChangedPayload {
  tenantId?: string;
  channelId?: string;
  userId?: string;
  lastReadSequence?: number;
}
