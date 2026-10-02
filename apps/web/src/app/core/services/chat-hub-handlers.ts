import { WritableSignal } from '@angular/core';
import { HubConnection } from '@microsoft/signalr';
import { ChatMessage, TypingState } from '../../shared/models/chat.models';
import { mapPollSummary } from '../../shared/polls/poll-summary';
import { coerceHubPayload, isAvailability, isStatusState, mapMessageCreated } from './chat-hub-payload';
import { withoutSelfTyping } from './typing-filter';
import {
  AnnouncementAcknowledgedEvent,
  AttachmentThumbnailReadyEvent,
  AttachmentThumbnailReadyPayload,
  LinkPreviewReadyEvent,
  LinkPreviewReadyPayload,
  MessageCreatedPayload,
  MessageDeleteEvent,
  MessageDeletedPayload,
  MessageEditEvent,
  MessageEditedPayload,
  PinChangedEvent,
  PinChangedPayload,
  PollChangedEvent,
  PresenceChangedEvent,
  ReactionChangedEvent,
  ReactionChangedPayload,
  ReadCursorChangedEvent,
  ReadCursorChangedPayload,
  ScheduleNoticeEvent,
  ScheduleNoticePayload,
  HubUserStatusEvent,
} from './chat-hub.types';

export interface ChatHubHandlerSets {
  profileId: () => string | undefined;
  typing: WritableSignal<TypingState[]>;
  messageHandlers: Set<(message: ChatMessage) => void>;
  editedHandlers: Set<(event: MessageEditEvent) => void>;
  deletedHandlers: Set<(event: MessageDeleteEvent) => void>;
  reactionHandlers: Set<(event: ReactionChangedEvent) => void>;
  pinHandlers: Set<(event: PinChangedEvent) => void>;
  pollHandlers: Set<(event: PollChangedEvent) => void>;
  announcementHandlers: Set<(event: AnnouncementAcknowledgedEvent) => void>;
  scheduleNoticeHandlers: Set<(event: ScheduleNoticeEvent) => void>;
  presenceHandlers: Set<(event: PresenceChangedEvent) => void>;
  userStatusHandlers: Set<(event: HubUserStatusEvent) => void>;
  thumbnailReadyHandlers: Set<(event: AttachmentThumbnailReadyEvent) => void>;
  linkPreviewReadyHandlers: Set<(event: LinkPreviewReadyEvent) => void>;
  readCursorHandlers: Set<(event: ReadCursorChangedEvent) => void>;
}

export function bindChatHubHandlers(connection: HubConnection, deps: ChatHubHandlerSets): void {
  connection.on('MessageCreated', (raw: MessageCreatedPayload | string) => {
    const payload = coerceHubPayload<MessageCreatedPayload>(raw);
    if (!payload) return;
    const message = mapMessageCreated(payload, deps.profileId());
    if (!message) return;
    for (const handler of deps.messageHandlers) {
      handler(message);
    }
  });

  connection.on('MessageEdited', (raw: MessageEditedPayload | string) => {
    const payload = coerceHubPayload<MessageEditedPayload>(raw);
    if (!payload) return;
    const id = payload.messageId ?? payload.id;
    if (!id || !payload.channelId || !payload.body) return;
    const event: MessageEditEvent = {
      id: String(id),
      channelId: String(payload.channelId),
      body: payload.body,
      editedAt: payload.editedAt ?? new Date().toISOString(),
      seq: payload.sequence,
    };
    for (const handler of deps.editedHandlers) {
      handler(event);
    }
  });

  connection.on('MessageDeleted', (raw: MessageDeletedPayload | string) => {
    const payload = coerceHubPayload<MessageDeletedPayload>(raw);
    if (!payload) return;
    const id = payload.messageId ?? payload.id;
    if (!id || !payload.channelId) return;
    const event: MessageDeleteEvent = {
      id: String(id),
      channelId: String(payload.channelId),
      deletedAt: payload.deletedAt ?? new Date().toISOString(),
      seq: payload.sequence,
    };
    for (const handler of deps.deletedHandlers) {
      handler(event);
    }
  });

  connection.on('ReactionChanged', (raw: ReactionChangedPayload | string) => {
    const payload = coerceHubPayload<ReactionChangedPayload>(raw);
    if (!payload?.messageId || !payload.channelId || !payload.emoji) return;
    const me = deps.profileId();
    const event: ReactionChangedEvent = {
      messageId: String(payload.messageId),
      channelId: String(payload.channelId),
      emoji: payload.emoji,
      userId: String(payload.userId ?? ''),
      added: !!payload.added,
      topUsers: payload.topUsers?.map(String),
      reactions: (payload.reactions ?? []).map((r) => {
        const userIds = (r.userIds ?? []).map(String);
        return {
          emoji: r.emoji,
          count: r.count,
          me: me ? userIds.includes(me) : !!r.me,
        };
      }),
    };
    for (const handler of deps.reactionHandlers) {
      handler(event);
    }
  });

  connection.on(
    'announcement.acknowledged',
    (raw: {
      messageId?: string;
      channelId?: string;
      acknowledgementCount?: number;
      acknowledgedByUserId?: string | null;
      closedAt?: string | null;
    } | string) => {
      const payload = coerceHubPayload<{
        messageId?: string;
        channelId?: string;
        acknowledgementCount?: number;
        acknowledgedByUserId?: string | null;
        closedAt?: string | null;
      }>(raw);
      if (!payload?.messageId || !payload.channelId) return;
      const event: AnnouncementAcknowledgedEvent = {
        messageId: String(payload.messageId),
        channelId: String(payload.channelId),
        acknowledgementCount: payload.acknowledgementCount,
        acknowledgedByUserId: payload.acknowledgedByUserId ? String(payload.acknowledgedByUserId) : null,
        closedAt: payload.closedAt ?? null,
      };
      for (const handler of deps.announcementHandlers) {
        handler(event);
      }
    },
  );

  connection.on('PollChanged', (raw: { messageId?: string; channelId?: string; poll?: unknown } | string) => {
    const payload = coerceHubPayload<{ messageId?: string; channelId?: string; poll?: unknown }>(raw);
    const poll = mapPollSummary(payload?.poll);
    if (!payload?.messageId || !payload.channelId || !poll) return;
    const event: PollChangedEvent = {
      messageId: String(payload.messageId),
      channelId: String(payload.channelId),
      poll,
    };
    for (const handler of deps.pollHandlers) {
      handler(event);
    }
  });

  connection.on('ReminderDue', (raw: ScheduleNoticePayload | string) => {
    emitScheduleNotice(deps, 'reminder', raw);
  });

  connection.on('ScheduledMessageDue', (raw: ScheduleNoticePayload | string) => {
    emitScheduleNotice(deps, 'scheduled', raw);
  });

  connection.on('PinChanged', (raw: PinChangedPayload | string) => {
    const payload = coerceHubPayload<PinChangedPayload>(raw);
    if (!payload?.messageId || !payload.channelId) return;
    const event: PinChangedEvent = {
      messageId: String(payload.messageId),
      channelId: String(payload.channelId),
      pinned: !!payload.pinned,
      byUserId: String(payload.byUserId ?? ''),
    };
    for (const handler of deps.pinHandlers) {
      handler(event);
    }
  });

  connection.on('AttachmentThumbnailReady', (raw: AttachmentThumbnailReadyPayload | string) => {
    const payload = coerceHubPayload<AttachmentThumbnailReadyPayload>(raw);
    if (!payload?.attachmentId || !payload.channelId) return;
    const event: AttachmentThumbnailReadyEvent = {
      attachmentId: String(payload.attachmentId),
      channelId: String(payload.channelId),
      thumbnailStatus: payload.thumbnailStatus ?? null,
      width: payload.width ?? null,
      height: payload.height ?? null,
      pageCount: payload.pageCount ?? null,
    };
    for (const handler of deps.thumbnailReadyHandlers) {
      handler(event);
    }
  });

  connection.on('LinkPreviewReady', (raw: LinkPreviewReadyPayload | string) => {
    const payload = coerceHubPayload<LinkPreviewReadyPayload>(raw);
    if (!payload?.messageId || !payload.channelId || !payload.linkPreviewId || !payload.url) {
      return;
    }
    const event: LinkPreviewReadyEvent = {
      tenantId: payload.tenantId ? String(payload.tenantId) : undefined,
      channelId: String(payload.channelId),
      messageId: String(payload.messageId),
      linkPreviewId: String(payload.linkPreviewId),
      url: payload.url,
      title: payload.title ?? null,
      description: payload.description ?? null,
      siteName: payload.siteName ?? null,
      hasImage: !!payload.hasImage,
      status: payload.status ?? 'Ready',
    };
    for (const handler of deps.linkPreviewReadyHandlers) {
      handler(event);
    }
  });

  connection.on('ReadCursorChanged', (raw: ReadCursorChangedPayload | string) => {
    const payload = coerceHubPayload<ReadCursorChangedPayload>(raw);
    if (!payload?.channelId || payload.userId == null || payload.lastReadSequence == null) {
      return;
    }
    const event: ReadCursorChangedEvent = {
      tenantId: payload.tenantId ? String(payload.tenantId) : undefined,
      channelId: String(payload.channelId),
      userId: String(payload.userId),
      lastReadSequence: Number(payload.lastReadSequence),
    };
    for (const handler of deps.readCursorHandlers) {
      handler(event);
    }
  });

  connection.on('Typing', (raw: {
    channelId: string;
    userId: string;
    displayName: string;
  } | string) => {
    const payload = coerceHubPayload<{
      channelId: string;
      userId: string;
      displayName: string;
    }>(raw);
    if (!payload?.channelId) return;
    const typing: TypingState = {
      channelId: String(payload.channelId),
      userId: String(payload.userId),
      displayName: payload.displayName,
    };
    // Hub uses OthersInGroup; still ignore self if echo arrives (B-071).
    const me = deps.profileId();
    if (me && typing.userId === me) return;
    deps.typing.update((list) => {
      const filtered = list.filter(
        (t) => !(t.channelId === typing.channelId && t.userId === typing.userId),
      );
      return withoutSelfTyping([...filtered, typing], me);
    });
    window.setTimeout(() => {
      deps.typing.update((list) =>
        list.filter(
          (t) => !(t.channelId === typing.channelId && t.userId === typing.userId),
        ),
      );
    }, 3000);
  });

  connection.on('PresenceChanged', (raw: {
    tenantId?: string;
    userId: string;
    status: string;
  } | string) => {
    const payload = coerceHubPayload<{
      tenantId?: string;
      userId: string;
      status: string;
    }>(raw);
    if (!payload?.userId) return;
    const status = (payload.status || 'offline').toLowerCase();
    const event: PresenceChangedEvent = {
      userId: String(payload.userId),
      tenantId: payload.tenantId ? String(payload.tenantId) : undefined,
      status: status === 'online' || status === 'away' ? status : 'offline',
    };
    for (const handler of deps.presenceHandlers) {
      handler(event);
    }
  });

  connection.on('UserStatusChanged', (raw: {
    tenantId?: string;
    userId: string;
    presence?: string;
    availability?: string;
    status?: {
      state?: string;
      emoji?: string;
      text?: string;
      clearAtEndOfDay?: boolean;
      expiresAt?: string | null;
    } | null;
  } | string) => {
    const payload = coerceHubPayload<{
      tenantId?: string;
      userId: string;
      presence?: string;
      availability?: string;
      status?: {
        state?: string;
        emoji?: string;
        text?: string;
        clearAtEndOfDay?: boolean;
        expiresAt?: string | null;
      } | null;
    }>(raw);
    if (!payload?.userId) return;
    const presence = (payload.presence || 'offline').toLowerCase();
    const availability = (payload.availability || 'offline').toLowerCase();
    const state = payload.status?.state?.toLowerCase();
    const event: HubUserStatusEvent = {
      userId: String(payload.userId),
      tenantId: payload.tenantId ? String(payload.tenantId) : undefined,
      presence: presence === 'online' || presence === 'away' ? presence : 'offline',
      availability: isAvailability(availability) ? availability : 'offline',
      status: payload.status && isStatusState(state)
        ? {
            state,
            emoji: payload.status.emoji ?? '',
            text: payload.status.text ?? '',
            clearAtEndOfDay: !!payload.status.clearAtEndOfDay,
            expiresAt: payload.status.expiresAt ?? null,
          }
        : null,
    };
    for (const handler of deps.userStatusHandlers) {
      handler(event);
    }
  });
}

function emitScheduleNotice(
  deps: ChatHubHandlerSets,
  kind: ScheduleNoticeEvent['kind'],
  raw: ScheduleNoticePayload | string,
): void {
  const payload = coerceHubPayload<ScheduleNoticePayload>(raw);
  const event: ScheduleNoticeEvent = {
    kind,
    status: payload?.status ?? '',
    reveal: payload?.reveal !== false,
  };
  for (const handler of deps.scheduleNoticeHandlers) {
    handler(event);
  }
}
