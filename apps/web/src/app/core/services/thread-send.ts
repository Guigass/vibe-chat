import { ApiService } from '../api/api.service';
import { ui } from '../i18n/strings';
import { ChatMessage, ChatThread } from '../../shared/models/chat.models';
import { replyPreviewText } from './message-sync';
import { normalizeThreadMessage, patchThreadByClientId, ThreadPatchState } from './thread-patches';

export interface ThreadSendDeps {
  api: ApiService;
  isDemo: () => boolean;
  isOfflineDemo: () => boolean;
  profile: () => { id?: string; name?: string } | null | undefined;
  state: ThreadPatchState;
  replyTarget: () => ChatMessage | null;
  setReplyTarget: (message: ChatMessage | null) => void;
  setSending: (value: boolean) => void;
  bumpReplyCount: (threadId: string) => void;
}

export async function sendThreadMessage(deps: ThreadSendDeps, body: string): Promise<boolean> {
  const thread = deps.state.active();
  const profile = deps.profile();
  const text = body.trim();
  if (!thread || !text) return false;

  const replyTarget = deps.replyTarget();
  const replyToMessageId = replyTarget?.id ?? thread.parentMessageId;
  const replyTo = threadReplyQuote(thread, replyTarget);

  const clientMessageId = crypto.randomUUID();
  const idempotencyKey = crypto.randomUUID();
  const optimistic: ChatMessage = {
    id: clientMessageId,
    clientMessageId,
    conversationId: thread.id,
    channelId: thread.channelId,
    authorUserId: profile?.id ?? 'me',
    authorName: profile?.name ?? ui.you,
    body: text,
    createdAt: new Date().toISOString(),
    status: 'sending',
    mine: true,
    threadId: thread.id,
    replyToMessageId,
    replyTo,
    parentMessageId: thread.parentMessageId,
  };

  deps.state.messages.update((list) => [...list, optimistic]);
  deps.setReplyTarget(null);
  deps.setSending(true);

  try {
    if (deps.isDemo() || deps.isOfflineDemo()) {
      await new Promise((r) => setTimeout(r, 350));
      patchThreadByClientId(deps.state, clientMessageId, {
        status: 'sent',
        id: crypto.randomUUID(),
        seq: deps.state.messages().length,
      });
      deps.bumpReplyCount(thread.id);
      return true;
    }

    const persisted = await deps.api.sendThreadMessage({
      threadId: thread.id,
      body: text,
      clientMessageId,
      idempotencyKey,
      replyToMessageId,
    });

    patchThreadByClientId(deps.state, clientMessageId, {
      ...normalizeThreadMessage(persisted, profile?.id),
      status: 'persisted',
      mine: true,
      clientMessageId,
    });
    // replyCount is bumped when the outbox/hub event arrives (or by MessageStore)
    return true;
  } catch {
    patchThreadByClientId(deps.state, clientMessageId, { status: 'failed' });
    return false;
  } finally {
    deps.setSending(false);
  }
}

function threadReplyQuote(thread: ChatThread, replyTarget: ChatMessage | null): ChatMessage['replyTo'] {
  if (replyTarget && !replyTarget.deletedAt) {
    return {
      messageId: replyTarget.id,
      authorName: replyTarget.authorName,
      preview: replyPreviewText(replyTarget.body),
      deleted: false,
    };
  }
  if (replyTarget) return null;
  if (thread.parentMessage && !thread.parentMessage.deletedAt) {
    return {
      messageId: thread.parentMessageId,
      authorName: thread.parentMessage.authorName,
      preview: replyPreviewText(thread.parentMessage.body),
      deleted: false,
    };
  }
  return {
    messageId: thread.parentMessageId,
    authorName: '',
    preview: '',
    deleted: false,
  };
}
