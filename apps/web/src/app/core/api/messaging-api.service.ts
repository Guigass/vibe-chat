import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import { mapAttachment, mapMoveFields, type AttachmentDto } from './api-mappers';
import { mapPollSummary } from '../../shared/polls/poll-summary';
import type { MessagingPolicy } from '../../shared/messaging/messaging-policy';
import {
  ChatMessage,
  ChatThread,
  ReactionSummary,
  MessageLinkPreview,
  PinnedMessageItem,
  SavedMessageItem,
  ScheduleItem,
  FollowedThreadItem,
  PollSummary,
  AnnouncementSummary,
  AnnouncementReportItem,
  PendingAnnouncement,
} from '../../shared/models/chat.models';

interface ReactionSummaryDto {
  emoji: string;
  count: number;
  me: boolean;
}

interface ReplyToDto {
  messageId: string;
  authorName: string;
  preview: string;
  deleted: boolean;
}

interface ForwardedFromDto {
  messageId: string;
  channelId: string;
  channelName: string;
  authorName: string;
  createdAt: string;
  isDirect?: boolean;
}

interface LinkPreviewDto {
  id: string;
  url: string;
  title?: string | null;
  description?: string | null;
  siteName?: string | null;
  hasImage: boolean;
  status: string;
}

interface ChannelMessagesDto {
  messages: MessageDto[];
  hasMoreBefore: boolean;
  hasMoreAfter: boolean;
}

export interface ChannelMessagesPage {
  messages: ChatMessage[];
  hasMoreBefore: boolean;
  hasMoreAfter: boolean;
}

interface MessageDto {
  id: string;
  channelId: string;
  conversationId?: string | null;
  sequence: number;
  authorId: string;
  authorName?: string;
  authorIsBot?: boolean;
  body: string;
  createdAt: string;
  editedAt?: string | null;
  deletedAt?: string | null;
  attachments?: AttachmentDto[] | null;
  threadId?: string | null;
  replyToMessageId?: string | null;
  replyTo?: ReplyToDto | null;
  forwardedFromMessageId?: string | null;
  forwardedFromChannelId?: string | null;
  forwardedFrom?: ForwardedFromDto | null;
  replyCount?: number;
  reactions?: ReactionSummaryDto[] | null;
  linkPreview?: LinkPreviewDto | null;
  isPinned?: boolean;
  poll?: PollDto | null;
  announcement?: AnnouncementDto | null;
}

interface AnnouncementDto {
  messageId: string;
  requiresAcknowledgement: boolean;
  acknowledgeBy?: string | null;
  closedAt?: string | null;
  acknowledgedByMe?: boolean;
  acknowledgementCount?: number;
  canAcknowledge?: boolean;
  canViewReport?: boolean;
}

interface PollDto {
  id: string;
  messageId: string;
  channelId: string;
  question: string;
  allowMultiple: boolean;
  anonymous: boolean;
  closesAt?: string | null;
  closedAt?: string | null;
  totalVotes: number;
  canVote: boolean;
  options: Array<{
    id: string;
    text: string;
    position: number;
    voteCount: number;
    percent: number;
    votedByMe: boolean;
    voters?: Array<{ userId: string; displayName: string }> | null;
  }>;
}

interface ForwardMessageResponseDto {
  messages: MessageDto[];
}

interface ToggleReactionDto {
  messageId: string;
  channelId: string;
  emoji: string;
  added: boolean;
  reactions: ReactionSummaryDto[];
}

interface ThreadDto {
  id: string;
  channelId: string;
  parentMessageId: string;
  createdBy: string;
  createdAt: string;
  replyCount: number;
  parentMessage?: MessageDto | null;
  following?: boolean;
  followSource?: string | null;
}

interface FollowedThreadDto {
  threadId: string;
  channelId: string;
  channelName: string;
  channelType: string;
  rootPreview: string;
  rootDeleted: boolean;
  unreadCount: number;
  lastActivityAt: string;
}

interface SavedMessageDto {
  messageId: string;
  channelId: string;
  channelName: string;
  channelType: string;
  sequence: number;
  authorUserId: string;
  authorName: string;
  bodyPreview: string;
  note?: string | null;
  completedAt?: string | null;
  createdAt: string;
  messageRemoved?: boolean;
}

interface ScheduleItemDto {
  kind: 'scheduled_message' | 'reminder';
  id: string;
  status: string;
  dueAtUtc: string;
  timeZone: string;
  body?: string | null;
  note?: string | null;
  targetKind?: string | null;
  channelId?: string | null;
  channelName?: string | null;
  messageId?: string | null;
  threadId?: string | null;
  sentMessageId?: string | null;
  failureCode?: string | null;
  canOpen?: boolean;
  createdAt: string;
}

function mapScheduleItem(dto: ScheduleItemDto): ScheduleItem {
  return {
    kind: dto.kind,
    id: dto.id,
    status: dto.status,
    dueAtUtc: dto.dueAtUtc,
    timeZone: dto.timeZone,
    body: dto.body ?? null,
    note: dto.note ?? null,
    targetKind: dto.targetKind ?? null,
    channelId: dto.channelId ?? null,
    channelName: dto.channelName ?? null,
    messageId: dto.messageId ?? null,
    threadId: dto.threadId ?? null,
    sentMessageId: dto.sentMessageId ?? null,
    failureCode: dto.failureCode ?? null,
    canOpen: dto.canOpen !== false,
    createdAt: dto.createdAt,
  };
}

function mapSavedMessage(dto: SavedMessageDto): SavedMessageItem {
  return {
    messageId: String(dto.messageId),
    channelId: String(dto.channelId),
    channelName: dto.channelName ?? '',
    channelType: dto.channelType ?? 'Public',
    sequence: dto.sequence ?? 0,
    authorUserId: String(dto.authorUserId ?? ''),
    authorName: dto.authorName ?? '',
    bodyPreview: dto.bodyPreview ?? '',
    note: dto.note ?? null,
    completedAt: dto.completedAt ?? null,
    createdAt: dto.createdAt,
    messageRemoved: !!dto.messageRemoved,
  };
}

function mapFollowedThread(dto: FollowedThreadDto): FollowedThreadItem {
  return {
    threadId: String(dto.threadId),
    channelId: String(dto.channelId),
    channelName: dto.channelName ?? '',
    channelType: dto.channelType ?? 'Public',
    rootPreview: dto.rootPreview ?? '',
    rootDeleted: !!dto.rootDeleted,
    unreadCount: dto.unreadCount ?? 0,
    lastActivityAt: dto.lastActivityAt,
  };
}

@Injectable({ providedIn: 'root' })
export class MessagingApiService extends HttpApiClient {
  async getMessages(
    channelId: string,
    options: { take?: number; after?: number; before?: number; around?: number } = {},
  ): Promise<ChannelMessagesPage> {
    const take = options.take ?? 50;
    const params = new URLSearchParams({ limit: String(take) });
    if (options.after !== undefined) params.set('after', String(options.after));
    if (options.before !== undefined) params.set('before', String(options.before));
    if (options.around !== undefined) params.set('around', String(options.around));
    const dto = await this.request<ChannelMessagesDto>(
      `/api/v1/channels/${channelId}/messages?${params.toString()}`,
    );
    const me = this.auth.profile()?.id;
    return {
      messages: dto.messages.map((m) => this.mapMessage(m, me)),
      hasMoreBefore: dto.hasMoreBefore,
      hasMoreAfter: dto.hasMoreAfter,
    };
  }

  async sendMessage(input: {
    channelId: string;
    body: string;
    clientMessageId: string;
    idempotencyKey: string;
    attachmentIds?: string[];
    replyToMessageId?: string;
    requiresAcknowledgement?: boolean;
    acknowledgeBy?: string | null;
  }): Promise<ChatMessage> {
    const dto = await this.request<MessageDto>(`/api/v1/channels/${input.channelId}/messages`, {
      method: 'POST',
        body: JSON.stringify({
        messageId: input.clientMessageId,
        idempotencyKey: input.idempotencyKey,
        body: input.body,
        attachmentIds: input.attachmentIds ?? [],
        replyToMessageId: input.replyToMessageId ?? null,
        requiresAcknowledgement: input.requiresAcknowledgement ?? false,
        acknowledgeBy: input.acknowledgeBy ?? null,
      }),
    });
    return this.mapMessage(dto, this.auth.profile()?.id);
  }

  async createPoll(input: {
    channelId: string;
    clientMessageId: string;
    idempotencyKey: string;
    question: string;
    options: string[];
    allowMultiple: boolean;
    anonymous: boolean;
    closesAt?: string | null;
  }): Promise<ChatMessage> {
    const dto = await this.request<MessageDto>(`/api/v1/channels/${input.channelId}/polls`, {
      method: 'POST',
      body: JSON.stringify({
        messageId: input.clientMessageId,
        idempotencyKey: input.idempotencyKey,
        question: input.question,
        options: input.options,
        allowMultiple: input.allowMultiple,
        anonymous: input.anonymous,
        closesAt: input.closesAt ?? null,
      }),
    });
    return this.mapMessage(dto, this.auth.profile()?.id);
  }

  async votePoll(pollId: string, optionIds: string[]): Promise<PollSummary> {
    const dto = await this.request<PollDto>(`/api/v1/polls/${pollId}/votes`, {
      method: 'POST',
      body: JSON.stringify({ optionIds }),
    });
    return this.mapPoll(dto)!;
  }

  async unvotePoll(pollId: string): Promise<PollSummary> {
    const dto = await this.request<PollDto>(`/api/v1/polls/${pollId}/votes`, {
      method: 'DELETE',
    });
    return this.mapPoll(dto)!;
  }

  async closePoll(pollId: string): Promise<PollSummary> {
    const dto = await this.request<PollDto>(`/api/v1/polls/${pollId}/close`, {
      method: 'POST',
    });
    return this.mapPoll(dto)!;
  }

  async forwardMessage(input: {
    workspaceId: string;
    messageId: string;
    targetChannelIds: string[];
    comment?: string;
    idempotencyKey: string;
  }): Promise<ChatMessage[]> {
    const dto = await this.request<ForwardMessageResponseDto>(
      `/api/v1/workspaces/${input.workspaceId}/messages/${input.messageId}/forward`,
      {
        method: 'POST',
        body: JSON.stringify({
          targetChannelIds: input.targetChannelIds,
          comment: input.comment ?? null,
          idempotencyKey: input.idempotencyKey,
        }),
      },
    );
    const me = this.auth.profile()?.id;
    return (dto.messages ?? []).map((m) => this.mapMessage(m, me));
  }

  async openThread(channelId: string, messageId: string): Promise<ChatThread> {
    const dto = await this.request<ThreadDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}/threads`,
      { method: 'POST', body: '{}' },
    );
    return this.mapThread(dto);
  }

  async getThread(threadId: string): Promise<ChatThread> {
    const dto = await this.request<ThreadDto>(`/api/v1/threads/${threadId}`);
    return this.mapThread(dto);
  }

  async getThreadMessages(threadId: string, take = 50): Promise<ChatMessage[]> {
    const rows = await this.request<MessageDto[]>(
      `/api/v1/threads/${threadId}/messages?limit=${take}`,
    );
    const me = this.auth.profile()?.id;
    return rows.map((m) => this.mapMessage(m, me));
  }

  async sendThreadMessage(input: {
    threadId: string;
    body: string;
    clientMessageId: string;
    idempotencyKey: string;
    replyToMessageId?: string;
  }): Promise<ChatMessage> {
    const dto = await this.request<MessageDto>(`/api/v1/threads/${input.threadId}/messages`, {
      method: 'POST',
      body: JSON.stringify({
        messageId: input.clientMessageId,
        idempotencyKey: input.idempotencyKey,
        body: input.body,
        replyToMessageId: input.replyToMessageId ?? null,
        threadId: input.threadId,
      }),
    });
    return this.mapMessage(dto, this.auth.profile()?.id);
  }

  async followThread(threadId: string): Promise<void> {
    await this.request(`/api/v1/threads/${threadId}/subscription`, { method: 'POST' });
  }

  async unfollowThread(threadId: string): Promise<void> {
    await this.request(`/api/v1/threads/${threadId}/subscription`, { method: 'DELETE' });
  }

  async markThreadRead(threadId: string, lastReadSequence: number): Promise<void> {
    await this.request(`/api/v1/threads/${threadId}/subscription/read-cursor`, {
      method: 'PUT',
      body: JSON.stringify({ lastReadSequence }),
    });
  }

  async getFollowedThreads(
    workspaceId: string,
    options?: { limit?: number; cursor?: string | null },
  ): Promise<{ items: FollowedThreadItem[]; nextCursor: string | null }> {
    const params = new URLSearchParams();
    if (options?.limit) params.set('limit', String(options.limit));
    if (options?.cursor) params.set('cursor', options.cursor);
    const qs = params.toString();
    const dto = await this.request<{ items: FollowedThreadDto[]; nextCursor?: string | null }>(
      `/api/v1/workspaces/${workspaceId}/threads/following${qs ? `?${qs}` : ''}`,
    );
    return {
      items: (dto.items ?? []).map(mapFollowedThread),
      nextCursor: dto.nextCursor ?? null,
    };
  }

  async shareThreadReplyToChannel(threadId: string, messageId: string): Promise<void> {
    await this.request(`/api/v1/threads/${threadId}/messages/${messageId}/share-to-channel`, {
      method: 'POST',
      body: JSON.stringify({ idempotencyKey: `share-${threadId}-${messageId}` }),
    });
  }

  async setChannelFollowAllThreads(channelId: string, enabled: boolean): Promise<void> {
    await this.request(`/api/v1/notifications/preferences/channels/${channelId}/follow-all-threads`, {
      method: 'PUT',
      body: JSON.stringify({ enabled }),
    });
  }

  async deleteMessageLinkPreview(channelId: string, messageId: string): Promise<void> {
    await this.request(`/api/v1/channels/${channelId}/messages/${messageId}/link-preview`, {
      method: 'DELETE',
    });
  }

  async getMessagingPolicy(channelId: string): Promise<MessagingPolicy> {
    return this.request(`/api/v1/channels/${channelId}/messaging-policy`);
  }

  async editMessage(channelId: string, messageId: string, body: string): Promise<ChatMessage> {
    const dto = await this.request<MessageDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}`,
      {
        method: 'PUT',
        body: JSON.stringify({ body }),
      },
    );
    return this.mapMessage(dto, this.auth.profile()?.id);
  }

  async deleteMessage(channelId: string, messageId: string): Promise<void> {
    await this.request(`/api/v1/channels/${channelId}/messages/${messageId}`, {
      method: 'DELETE',
    });
  }

  async pinMessage(
    channelId: string,
    messageId: string,
  ): Promise<{ messageId: string; channelId: string; pinned: boolean; pinCount: number }> {
    const dto = await this.request<{
      messageId: string;
      channelId: string;
      pinned: boolean;
      pinCount: number;
    }>(`/api/v1/channels/${channelId}/messages/${messageId}/pin`, {
      method: 'POST',
    });
    return {
      messageId: String(dto.messageId),
      channelId: String(dto.channelId),
      pinned: !!dto.pinned,
      pinCount: dto.pinCount ?? 0,
    };
  }

  async unpinMessage(channelId: string, messageId: string): Promise<void> {
    await this.request(`/api/v1/channels/${channelId}/messages/${messageId}/pin`, {
      method: 'DELETE',
    });
  }

  async getPins(channelId: string): Promise<{
    pins: PinnedMessageItem[];
    count: number;
    limit: number;
  }> {
    const dto = await this.request<{
      pins: Array<{
        messageId: string;
        channelId: string;
        sequence: number;
        bodyPreview: string;
        authorName: string;
        pinnedByUserId: string;
        pinnedByName: string;
        pinnedAt: string;
      }>;
      count: number;
      limit: number;
    }>(`/api/v1/channels/${channelId}/pins`);
    const limit = dto.limit ?? 20;
    return {
      count: dto.count ?? dto.pins?.length ?? 0,
      limit,
      pins: (dto.pins ?? []).map((p) => ({
        messageId: String(p.messageId),
        channelId: String(p.channelId),
        sequence: p.sequence,
        bodyPreview: p.bodyPreview,
        authorName: p.authorName,
        pinnedByUserId: String(p.pinnedByUserId),
        pinnedByName: p.pinnedByName,
        pinnedAt: p.pinnedAt,
        limit,
      })),
    };
  }

  async saveMessage(
    workspaceId: string,
    messageId: string,
    note?: string | null,
  ): Promise<SavedMessageItem> {
    const dto = await this.request<SavedMessageDto>(
      `/api/v1/workspaces/${workspaceId}/saved`,
      {
        method: 'POST',
        body: JSON.stringify({ messageId, note: note ?? null }),
      },
    );
    return mapSavedMessage(dto);
  }

  async patchSavedMessage(
    workspaceId: string,
    messageId: string,
    patch: { note?: string | null; completed?: boolean },
  ): Promise<SavedMessageItem> {
    const dto = await this.request<SavedMessageDto>(
      `/api/v1/workspaces/${workspaceId}/saved/${messageId}`,
      {
        method: 'PATCH',
        body: JSON.stringify(patch),
      },
    );
    return mapSavedMessage(dto);
  }

  async unsaveMessage(workspaceId: string, messageId: string): Promise<void> {
    await this.request(`/api/v1/workspaces/${workspaceId}/saved/${messageId}`, {
      method: 'DELETE',
    });
  }

  async getSavedMessages(
    workspaceId: string,
    options?: { completed?: boolean; limit?: number; cursor?: string | null },
  ): Promise<{ items: SavedMessageItem[]; nextCursor: string | null; pendingCount: number }> {
    const params = new URLSearchParams();
    if (options?.completed === true) params.set('completed', 'true');
    if (options?.completed === false) params.set('completed', 'false');
    if (options?.limit) params.set('limit', String(options.limit));
    if (options?.cursor) params.set('cursor', options.cursor);
    const qs = params.toString();
    const dto = await this.request<{
      items: SavedMessageDto[];
      nextCursor?: string | null;
      pendingCount: number;
    }>(`/api/v1/workspaces/${workspaceId}/saved${qs ? `?${qs}` : ''}`);
    return {
      items: (dto.items ?? []).map(mapSavedMessage),
      nextCursor: dto.nextCursor ?? null,
      pendingCount: dto.pendingCount ?? 0,
    };
  }

  async createScheduledMessage(input: {
    channelId: string;
    body: string;
    sendAtLocal: string;
    timeZone: string;
    idempotencyKey: string;
    replyToMessageId?: string | null;
    threadId?: string | null;
  }): Promise<ScheduleItem> {
    const dto = await this.request<ScheduleItemDto>(
      `/api/v1/channels/${input.channelId}/scheduled-messages`,
      {
        method: 'POST',
        body: JSON.stringify({
          idempotencyKey: input.idempotencyKey,
          body: input.body,
          sendAtLocal: input.sendAtLocal,
          timeZone: input.timeZone,
          replyToMessageId: input.replyToMessageId ?? null,
          threadId: input.threadId ?? null,
        }),
      },
    );
    return mapScheduleItem(dto);
  }

  async updateScheduledMessage(
    workspaceId: string,
    id: string,
    patch: { body?: string; sendAtLocal?: string; timeZone?: string },
  ): Promise<ScheduleItem> {
    const dto = await this.request<ScheduleItemDto>(
      `/api/v1/workspaces/${workspaceId}/scheduled-messages/${id}`,
      { method: 'PATCH', body: JSON.stringify(patch) },
    );
    return mapScheduleItem(dto);
  }

  async cancelScheduledMessage(workspaceId: string, id: string): Promise<void> {
    await this.request(`/api/v1/workspaces/${workspaceId}/scheduled-messages/${id}`, {
      method: 'DELETE',
    });
  }

  async getSchedule(
    workspaceId: string,
    options?: { limit?: number; cursor?: string | null },
  ): Promise<{ items: ScheduleItem[]; nextCursor: string | null }> {
    const params = new URLSearchParams();
    if (options?.limit) params.set('limit', String(options.limit));
    if (options?.cursor) params.set('cursor', options.cursor);
    const qs = params.toString();
    const dto = await this.request<{ items: ScheduleItemDto[]; nextCursor?: string | null }>(
      `/api/v1/workspaces/${workspaceId}/schedule${qs ? `?${qs}` : ''}`,
    );
    return {
      items: (dto.items ?? []).map(mapScheduleItem),
      nextCursor: dto.nextCursor ?? null,
    };
  }

  async createReminder(input: {
    workspaceId: string;
    idempotencyKey: string;
    targetKind: 'Time' | 'Message' | 'Thread';
    remindAtLocal: string;
    timeZone: string;
    note?: string | null;
    messageId?: string | null;
    threadId?: string | null;
    channelId?: string | null;
  }): Promise<ScheduleItem> {
    const dto = await this.request<ScheduleItemDto>(
      `/api/v1/workspaces/${input.workspaceId}/reminders`,
      {
        method: 'POST',
        body: JSON.stringify({
          idempotencyKey: input.idempotencyKey,
          targetKind: input.targetKind,
          remindAtLocal: input.remindAtLocal,
          timeZone: input.timeZone,
          note: input.note ?? null,
          messageId: input.messageId ?? null,
          threadId: input.threadId ?? null,
          channelId: input.channelId ?? null,
        }),
      },
    );
    return mapScheduleItem(dto);
  }

  async updateReminder(
    workspaceId: string,
    id: string,
    patch: { note?: string | null; remindAtLocal?: string; timeZone?: string },
  ): Promise<ScheduleItem> {
    const dto = await this.request<ScheduleItemDto>(
      `/api/v1/workspaces/${workspaceId}/reminders/${id}`,
      { method: 'PATCH', body: JSON.stringify(patch) },
    );
    return mapScheduleItem(dto);
  }

  async cancelReminder(workspaceId: string, id: string): Promise<void> {
    await this.request(`/api/v1/workspaces/${workspaceId}/reminders/${id}`, {
      method: 'DELETE',
    });
  }

  async toggleReaction(
    channelId: string,
    messageId: string,
    emoji: string,
  ): Promise<{ messageId: string; channelId: string; emoji: string; added: boolean; reactions: ReactionSummary[] }> {
    const dto = await this.request<ToggleReactionDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}/reactions`,
      {
        method: 'PUT',
        body: JSON.stringify({ emoji }),
      },
    );
    return {
      messageId: dto.messageId,
      channelId: dto.channelId,
      emoji: dto.emoji,
      added: dto.added,
      reactions: (dto.reactions ?? []).map((r) => ({
        emoji: r.emoji,
        count: r.count,
        me: !!r.me,
      })),
    };
  }

  async getReactionUsers(
    channelId: string,
    messageId: string,
    emoji: string,
  ): Promise<{ emoji: string; users: Array<{ userId: string; displayName: string }>; total: number }> {
    const dto = await this.request<{
      emoji: string;
      users: Array<{ userId: string; displayName: string }>;
      total: number;
    }>(
      `/api/v1/channels/${channelId}/messages/${messageId}/reactions/${encodeURIComponent(emoji)}/users`,
    );
    return {
      emoji: dto.emoji,
      users: (dto.users ?? []).map((user) => ({
        userId: String(user.userId),
        displayName: user.displayName,
      })),
      total: dto.total ?? dto.users?.length ?? 0,
    };
  }

  async upsertReadCursor(
    channelId: string,
    lastReadSequence: number,
    options?: { allowRetrograde?: boolean },
  ): Promise<void> {
    await this.request(`/api/v1/channels/${channelId}/read-cursor`, {
      method: 'PUT',
      body: JSON.stringify({
        lastReadSequence,
        allowRetrograde: options?.allowRetrograde ?? false,
      }),
    });
  }

  async getWorkspaceChannelUnreads(
    workspaceId: string,
  ): Promise<
    Array<{
      channelId: string;
      unreadCount: number;
      mentionCount: number;
      lastReadSeq: number;
      pendingAnnouncementCount: number;
    }>
  > {
    const rows = await this.request<
      Array<{
        channelId: string;
        unreadCount: number;
        mentionCount: number;
        lastReadSeq: number;
        pendingAnnouncementCount?: number;
      }>
    >(`/api/v1/workspaces/${workspaceId}/channels/unread`);
    return (rows ?? []).map((row) => ({
      channelId: String(row.channelId),
      unreadCount: row.unreadCount ?? 0,
      mentionCount: row.mentionCount ?? 0,
      lastReadSeq: row.lastReadSeq ?? 0,
      pendingAnnouncementCount: row.pendingAnnouncementCount ?? 0,
    }));
  }

  async getUnreadCount(channelId: string): Promise<{ unreadCount: number; mentionCount: number; pendingAnnouncementCount: number }> {
    const row = await this.request<{ unreadCount: number; mentionCount: number; pendingAnnouncementCount?: number }>(
      `/api/v1/channels/${channelId}/unread-count`,
    );
    return {
      unreadCount: row.unreadCount ?? 0,
      mentionCount: row.mentionCount ?? 0,
      pendingAnnouncementCount: row.pendingAnnouncementCount ?? 0,
    };
  }

  async acknowledgeAnnouncement(channelId: string, messageId: string): Promise<AnnouncementSummary | null> {
    const dto = await this.request<AnnouncementDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}/acknowledgements`,
      { method: 'POST' },
    );
    return this.mapAnnouncement(dto);
  }

  async closeAnnouncement(channelId: string, messageId: string): Promise<AnnouncementSummary | null> {
    const dto = await this.request<AnnouncementDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}/acknowledgements/close`,
      { method: 'POST' },
    );
    return this.mapAnnouncement(dto);
  }

  async getAnnouncementReport(
    channelId: string,
    messageId: string,
  ): Promise<AnnouncementReportItem[]> {
    const dto = await this.request<{ items?: AnnouncementReportItem[] }>(
      `/api/v1/channels/${channelId}/messages/${messageId}/acknowledgements`,
    );
    return (dto.items ?? []).map((item) => ({
      userId: String(item.userId),
      displayName: item.displayName,
      acknowledgedAt: item.acknowledgedAt,
    }));
  }

  async getPendingAnnouncements(workspaceId: string): Promise<PendingAnnouncement[]> {
    const rows = await this.request<PendingAnnouncement[]>(
      `/api/v1/workspaces/${workspaceId}/announcements/pending`,
    );
    return (rows ?? []).map((row) => ({
      messageId: String(row.messageId),
      channelId: String(row.channelId),
      channelName: row.channelName,
      authorName: row.authorName,
      bodyPreview: row.bodyPreview,
      createdAt: row.createdAt,
      acknowledgeBy: row.acknowledgeBy ?? null,
    }));
  }

  private mapThread(t: ThreadDto): ChatThread {
    const me = this.auth.profile()?.id;
    const followSource = t.followSource ?? null;
    return {
      id: t.id,
      channelId: t.channelId,
      parentMessageId: t.parentMessageId,
      createdBy: t.createdBy,
      createdAt: t.createdAt,
      replyCount: t.replyCount ?? 0,
      parentMessage: t.parentMessage ? this.mapMessage(t.parentMessage, me) : null,
      following: !!t.following,
      followSource:
        followSource === 'Manual' || followSource === 'Author' || followSource === 'Reply' || followSource === 'Mention'
          ? followSource
          : null,
    };
  }

  private mapMessage(m: MessageDto, me?: string): ChatMessage {
    const conversationId = m.conversationId || m.channelId;
    return {
      id: m.id,
      conversationId,
      channelId: m.channelId,
      authorUserId: m.authorId,
      authorName: m.authorName || m.authorId,
      authorIsBot: !!m.authorIsBot,
      body: m.deletedAt ? '' : m.body,
      createdAt: m.createdAt,
      editedAt: m.editedAt,
      deletedAt: m.deletedAt,
      seq: m.sequence,
      status: 'persisted',
      mine: !!me && me.toLowerCase() === String(m.authorId ?? '').toLowerCase(),
      attachments: (m.attachments ?? []).map((a) => mapAttachment(a)),
      threadId: m.threadId ?? null,
      replyToMessageId: m.replyToMessageId ?? null,
      replyTo: m.replyTo
        ? {
            messageId: String(m.replyTo.messageId),
            authorName: m.replyTo.authorName ?? '',
            preview: m.replyTo.preview ?? '',
            deleted: !!m.replyTo.deleted,
          }
        : null,
      forwardedFromMessageId: m.forwardedFromMessageId ?? null,
      forwardedFromChannelId: m.forwardedFromChannelId ?? null,
      forwardedFrom: m.forwardedFrom
        ? {
            messageId: String(m.forwardedFrom.messageId),
            channelId: String(m.forwardedFrom.channelId),
            channelName: m.forwardedFrom.channelName ?? '',
            authorName: m.forwardedFrom.authorName ?? '',
            createdAt: m.forwardedFrom.createdAt,
            isDirect: !!m.forwardedFrom.isDirect,
          }
        : null,
      replyCount: m.replyCount ?? 0,
      reactions: (m.reactions ?? []).map((r) => ({
        emoji: r.emoji,
        count: r.count,
        me: !!r.me,
      })),
      linkPreview: this.mapLinkPreview(m.linkPreview),
      isPinned: !!m.isPinned,
      poll: this.mapPoll(m.poll),
      announcement: this.mapAnnouncement(m.announcement), ...mapMoveFields(m),
    };
  }

  private mapPoll(poll?: PollDto | null): PollSummary | null {
    return mapPollSummary(poll);
  }

  private mapAnnouncement(row?: AnnouncementDto | null): AnnouncementSummary | null {
    if (!row?.messageId) return null;
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

  private mapLinkPreview(preview?: LinkPreviewDto | null): MessageLinkPreview | null {
    if (!preview?.id || !preview.url) return null;
    return {
      id: String(preview.id),
      url: preview.url,
      title: preview.title ?? null,
      description: preview.description ?? null,
      siteName: preview.siteName ?? null,
      hasImage: !!preview.hasImage,
      status: preview.status ?? 'Ready',
    };
  }
}
