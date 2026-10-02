import { ApiService } from '../api/api.service';
import { ui } from '../i18n/strings';
import { ChatMessage, PollSummary } from '../../shared/models/chat.models';
import { idsEqual, replyPreviewText } from './message-sync';
import {
  applyAnnouncementToMessages,
  applyPollToMessages,
  patchMessageByClientId,
  toggleLocalReaction,
} from './message-patches';

export interface MessageCommandDeps {
  api: ApiService;
  isDemo: () => boolean;
  isOfflineDemo: () => boolean;
  activeChannel: () => { id: string; isDirect?: boolean } | null | undefined;
  profile: () => { id?: string; name?: string } | null | undefined;
  messages: () => readonly ChatMessage[];
  updateMessages: (fn: (list: ChatMessage[]) => ChatMessage[]) => void;
  replyTarget: () => ChatMessage | null;
  setReplyTarget: (message: ChatMessage | null) => void;
  setEditing: (message: ChatMessage | null) => void;
  setSending: (value: boolean) => void;
  normalize: (message: ChatMessage) => ChatMessage;
  ingestRemote: (message: ChatMessage) => void;
  applyEdit: (patch: {
    id: string;
    channelId: string;
    body: string;
    editedAt: string;
    seq?: number;
  }) => void;
  applyDelete: (patch: { id: string; channelId: string; deletedAt: string; seq?: number }) => void;
  applyReactions: (
    messageId: string,
    reactions: Array<{ emoji: string; count: number; me: boolean }>,
  ) => void;
  clearLinkPreview: (messageId: string) => void;
  clearThreadLinkPreview: (messageId: string) => void;
  recordSuccessfulSend: () => void;
  forgetPendingAnnouncement: (messageId: string) => void;
  refreshPendingAnnouncements: () => void;
}

export async function sendChannelMessage(
  deps: MessageCommandDeps,
  body: string,
  attachmentIds: string[] = [],
  announcement?: { requiresAcknowledgement: boolean; acknowledgeBy?: string | null },
): Promise<boolean> {
  const channel = deps.activeChannel();
  const profile = deps.profile();
  const text = body.trim();
  const hasAttachments = attachmentIds.length > 0;
  if (!channel || (!text && !hasAttachments)) return false;

  const replyTarget = deps.replyTarget();
  const replyToMessageId = replyTarget?.id ?? null;
  const replyTo =
    replyTarget && !replyTarget.deletedAt
      ? {
          messageId: replyTarget.id,
          authorName: replyTarget.authorName,
          preview: replyPreviewText(replyTarget.body),
          deleted: false,
        }
      : null;

  const clientMessageId = crypto.randomUUID();
  const idempotencyKey = crypto.randomUUID();
  const optimistic: ChatMessage = {
    id: clientMessageId,
    clientMessageId,
    conversationId: channel.id,
    channelId: channel.id,
    authorUserId: profile?.id ?? 'me',
    authorName: profile?.name ?? ui.you,
    body: text,
    createdAt: new Date().toISOString(),
    status: 'sending',
    mine: true,
    replyToMessageId,
    replyTo,
    attachments: hasAttachments
      ? attachmentIds.map((id) => ({
          id,
          fileName: ui.attachmentFallback,
          contentType: 'application/octet-stream',
          sizeBytes: 0,
          status: 'PendingUpload',
        }))
      : [],
  };

  deps.updateMessages((list) => [...list, optimistic]);
  deps.setReplyTarget(null);
  deps.setSending(true);

  try {
    if (deps.isDemo() || deps.isOfflineDemo()) {
      await new Promise((r) => setTimeout(r, 450));
      deps.updateMessages((list) =>
        patchMessageByClientId(list, clientMessageId, {
          status: 'sent',
          id: crypto.randomUUID(),
          attachments: hasAttachments
            ? attachmentIds.map((id) => ({
                id,
                fileName: ui.attachmentFallback,
                contentType: 'application/octet-stream',
                sizeBytes: 0,
                status: 'Ready',
              }))
            : [],
        }),
      );
      return true;
    }

    const persisted = await deps.api.sendMessage({
      channelId: channel.id,
      body: optimistic.body,
      clientMessageId,
      idempotencyKey,
      attachmentIds,
      replyToMessageId: replyToMessageId ?? undefined,
      requiresAcknowledgement: announcement?.requiresAcknowledgement ?? false,
      acknowledgeBy: announcement?.acknowledgeBy ?? null,
    });

    deps.updateMessages((list) =>
      patchMessageByClientId(list, clientMessageId, {
        ...deps.normalize(persisted),
        status: 'persisted',
        mine: true,
        clientMessageId,
      }),
    );
    deps.recordSuccessfulSend();
    return true;
  } catch {
    deps.updateMessages((list) => patchMessageByClientId(list, clientMessageId, { status: 'failed' }));
    if (replyTarget) deps.setReplyTarget(replyTarget);
    return false;
  } finally {
    deps.setSending(false);
  }
}

export async function createChannelPoll(
  deps: MessageCommandDeps,
  input: {
    question: string;
    options: string[];
    allowMultiple: boolean;
    anonymous: boolean;
    closesAt?: string | null;
  },
): Promise<boolean> {
  const channel = deps.activeChannel();
  if (!channel || channel.isDirect) return false;
  if (deps.isDemo() || deps.isOfflineDemo()) return false;

  deps.setSending(true);
  try {
    const persisted = await deps.api.createPoll({
      channelId: channel.id,
      clientMessageId: crypto.randomUUID(),
      idempotencyKey: crypto.randomUUID(),
      question: input.question,
      options: input.options,
      allowMultiple: input.allowMultiple,
      anonymous: input.anonymous,
      closesAt: input.closesAt,
    });
    deps.ingestRemote(deps.normalize(persisted));
    return true;
  } catch {
    return false;
  } finally {
    deps.setSending(false);
  }
}

export async function voteChannelPoll(
  deps: MessageCommandDeps,
  poll: PollSummary,
  optionId: string,
): Promise<void> {
  if (!poll.canVote || poll.closedAt) return;
  const nextIds = poll.allowMultiple
    ? poll.options.some((option) => option.id === optionId && option.votedByMe)
      ? poll.options.filter((option) => option.votedByMe && option.id !== optionId).map((option) => option.id)
      : [...poll.options.filter((option) => option.votedByMe).map((option) => option.id), optionId]
    : [optionId];

  try {
    const updated =
      poll.allowMultiple && nextIds.length === 0
        ? await deps.api.unvotePoll(poll.id)
        : await deps.api.votePoll(poll.id, nextIds);
    deps.updateMessages((list) => applyPollToMessages(list, poll.messageId, updated));
  } catch {
    /* keep current card */
  }
}

export async function closeChannelPoll(
  deps: MessageCommandDeps,
  pollId: string,
  messageId: string,
): Promise<void> {
  try {
    const updated = await deps.api.closePoll(pollId);
    deps.updateMessages((list) => applyPollToMessages(list, messageId, updated));
  } catch {
    /* keep current card */
  }
}

export async function acknowledgeChannelAnnouncement(
  deps: MessageCommandDeps,
  message: ChatMessage,
): Promise<boolean> {
  const card = message.announcement;
  if (!card || message.status !== 'persisted') return false;
  try {
    const updated = await deps.api.acknowledgeAnnouncement(message.channelId, message.id);
    if (!updated) return false;
    deps.updateMessages((list) => applyAnnouncementToMessages(list, message.id, updated));
    deps.forgetPendingAnnouncement(message.id);
    void deps.refreshPendingAnnouncements();
    return true;
  } catch {
    return false;
  }
}

export async function closeChannelAnnouncement(
  deps: MessageCommandDeps,
  message: ChatMessage,
): Promise<boolean> {
  if (!message.announcement || message.status !== 'persisted') return false;
  try {
    const updated = await deps.api.closeAnnouncement(message.channelId, message.id);
    if (!updated) return false;
    deps.updateMessages((list) => applyAnnouncementToMessages(list, message.id, updated));
    void deps.refreshPendingAnnouncements();
    return true;
  } catch {
    return false;
  }
}

export async function editChannelMessage(
  deps: MessageCommandDeps,
  messageId: string,
  body: string,
): Promise<void> {
  const channel = deps.activeChannel();
  if (!channel || !body.trim()) return;

  if (deps.isDemo() || deps.isOfflineDemo()) {
    deps.updateMessages((list) =>
      list.map((m) =>
        m.id === messageId ? { ...m, body: body.trim(), editedAt: new Date().toISOString() } : m,
      ),
    );
    deps.setEditing(null);
    return;
  }

  const updated = await deps.api.editMessage(channel.id, messageId, body.trim());
  deps.applyEdit({
    id: updated.id,
    channelId: updated.channelId,
    body: updated.body,
    editedAt: updated.editedAt ?? new Date().toISOString(),
    seq: updated.seq,
  });
  deps.setEditing(null);
}

export async function removeChannelMessage(deps: MessageCommandDeps, messageId: string): Promise<void> {
  const channel = deps.activeChannel();
  if (!channel) return;

  if (deps.isDemo() || deps.isOfflineDemo()) {
    deps.applyDelete({
      id: messageId,
      channelId: channel.id,
      deletedAt: new Date().toISOString(),
    });
    return;
  }

  await deps.api.deleteMessage(channel.id, messageId);
  deps.applyDelete({
    id: messageId,
    channelId: channel.id,
    deletedAt: new Date().toISOString(),
  });
}

export async function removeChannelLinkPreview(
  deps: MessageCommandDeps,
  messageId: string,
): Promise<void> {
  const channel = deps.activeChannel();
  const existing = deps.messages().find((m) => idsEqual(m.id, messageId));
  const channelId = existing?.channelId ?? channel?.id;
  if (!channelId || !messageId) return;

  if (deps.isDemo() || deps.isOfflineDemo()) {
    deps.clearLinkPreview(messageId);
    deps.clearThreadLinkPreview(messageId);
    return;
  }

  await deps.api.deleteMessageLinkPreview(channelId, messageId);
  deps.clearLinkPreview(messageId);
  deps.clearThreadLinkPreview(messageId);
}

export async function toggleChannelReaction(
  deps: MessageCommandDeps,
  messageId: string,
  emoji: string,
): Promise<void> {
  const channel = deps.activeChannel();
  if (!channel || !emoji) return;

  if (deps.isDemo() || deps.isOfflineDemo()) {
    deps.updateMessages((list) => toggleLocalReaction(list, messageId, emoji));
    return;
  }

  const result = await deps.api.toggleReaction(channel.id, messageId, emoji);
  deps.applyReactions(result.messageId, result.reactions);
}
