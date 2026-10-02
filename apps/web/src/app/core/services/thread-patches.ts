import { WritableSignal } from '@angular/core';
import { ChatMessage, ChatThread } from '../../shared/models/chat.models';
import { idsEqual, markReplyQuotesDeleted, upsertRemoteMessage, findMessageByCorrelators } from './message-sync';

export interface ThreadPatchState {
  messages: WritableSignal<ChatMessage[]>;
  active: WritableSignal<ChatThread | null>;
  editing: WritableSignal<ChatMessage | null>;
  profileId: () => string | undefined;
}

export function normalizeThreadMessage(message: ChatMessage, me: string | undefined): ChatMessage {
  return {
    ...message,
    channelId: message.channelId || message.conversationId,
    status: message.status ?? 'persisted',
    mine: idsEqual(message.authorUserId, me),
    authorName: message.authorName || 'Membro',
    body: message.deletedAt ? '' : message.body,
    reactions: message.reactions ?? [],
    replyTo: message.replyTo ?? null,
  };
}

export function ingestThreadMessage(state: ThreadPatchState, message: ChatMessage): void {
  const active = state.active();
  if (!active || !message.threadId || !idsEqual(message.threadId, active.id)) return;
  if (idsEqual(message.conversationId, message.channelId)) return;

  const normalized = normalizeThreadMessage(message, state.profileId());
  const mine = idsEqual(normalized.authorUserId, state.profileId());
  const remote: ChatMessage = { ...normalized, mine, status: 'persisted' };
  const existing = findMessageByCorrelators(state.messages(), remote);

  if (existing?.clientMessageId) {
    patchThreadByClientId(state, existing.clientMessageId, {
      ...remote,
      clientMessageId: existing.clientMessageId,
      mine,
    });
    return;
  }

  state.messages.update((list) => upsertRemoteMessage(list, remote));
}

export function applyThreadDelete(state: ThreadPatchState, messageId: string): void {
  state.messages.update((list) => {
    const deleted = list.map((m) =>
      idsEqual(m.id, messageId)
        ? { ...m, body: '', deletedAt: m.deletedAt ?? new Date().toISOString() }
        : m,
    );
    return markReplyQuotesDeleted(deleted, messageId);
  });
  const active = state.active();
  if (active?.parentMessage && idsEqual(active.parentMessage.id, messageId)) {
    state.active.set({
      ...active,
      parentMessage: {
        ...active.parentMessage,
        body: '',
        deletedAt: active.parentMessage.deletedAt ?? new Date().toISOString(),
      },
    });
  }
  const editing = state.editing();
  if (editing && idsEqual(editing.id, messageId)) {
    state.editing.set(null);
  }
}

export function applyThreadEdit(
  state: ThreadPatchState,
  patch: { id: string; channelId: string; body: string; editedAt: string; seq?: number },
): void {
  const patchList = (list: ChatMessage[]): ChatMessage[] =>
    list.map((m) =>
      idsEqual(m.id, patch.id)
        ? {
            ...m,
            body: patch.body,
            editedAt: patch.editedAt,
            seq: patch.seq ?? m.seq,
            deletedAt: null,
          }
        : m,
    );

  state.messages.update(patchList);
  const active = state.active();
  if (active?.parentMessage && idsEqual(active.parentMessage.id, patch.id)) {
    state.active.set({
      ...active,
      parentMessage: {
        ...active.parentMessage,
        body: patch.body,
        editedAt: patch.editedAt,
        seq: patch.seq ?? active.parentMessage.seq,
        deletedAt: null,
      },
    });
  }
}

export function patchThreadByClientId(
  state: ThreadPatchState,
  clientMessageId: string,
  patch: Partial<ChatMessage>,
): void {
  state.messages.update((list) =>
    list.map((m) => (idsEqual(m.clientMessageId, clientMessageId) ? { ...m, ...patch } : m)),
  );
}

export function applyThreadReactions(
  state: ThreadPatchState,
  messageId: string,
  reactions: Array<{ emoji: string; count: number; me: boolean }>,
): void {
  state.messages.update((list) =>
    list.map((m) => (idsEqual(m.id, messageId) ? { ...m, reactions: [...reactions] } : m)),
  );
  const active = state.active();
  if (active?.parentMessage && idsEqual(active.parentMessage.id, messageId)) {
    state.active.set({
      ...active,
      parentMessage: { ...active.parentMessage, reactions: [...reactions] },
    });
  }
}

export function applyThreadThumbnail(
  state: ThreadPatchState,
  event: {
    attachmentId: string;
    channelId: string;
    thumbnailStatus: string | null;
    width?: number | null;
    height?: number | null;
    pageCount?: number | null;
  },
): void {
  const patchAttachments = (list: ChatMessage[]): ChatMessage[] =>
    list.map((m) => {
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

  state.messages.update(patchAttachments);
  const active = state.active();
  if (active?.parentMessage) {
    const [parent] = patchAttachments([active.parentMessage]);
    if (parent !== active.parentMessage) {
      state.active.set({ ...active, parentMessage: parent });
    }
  }
}

export function clearThreadLinkPreview(state: ThreadPatchState, messageId: string): void {
  const clear = (list: ChatMessage[]): ChatMessage[] =>
    list.map((m) => (idsEqual(m.id, messageId) ? { ...m, linkPreview: null } : m));

  state.messages.update(clear);
  const active = state.active();
  if (active?.parentMessage && idsEqual(active.parentMessage.id, messageId)) {
    state.active.set({
      ...active,
      parentMessage: { ...active.parentMessage, linkPreview: null },
    });
  }
}

export function applyThreadLinkPreview(
  state: ThreadPatchState,
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
): void {
  const patch = (list: ChatMessage[]): ChatMessage[] =>
    list.map((m) => {
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

  state.messages.update(patch);
  const active = state.active();
  if (active?.parentMessage) {
    const [parent] = patch([active.parentMessage]);
    if (parent !== active.parentMessage) {
      state.active.set({ ...active, parentMessage: parent });
    }
  }
}

export function toggleThreadReactionLocal(
  state: ThreadPatchState,
  messageId: string,
  emoji: string,
): void {
  const toggle = (list: ChatMessage[]): ChatMessage[] =>
    list.map((m) => {
      if (!idsEqual(m.id, messageId)) return m;
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

  state.messages.update(toggle);
  const active = state.active();
  if (active?.parentMessage && idsEqual(active.parentMessage.id, messageId)) {
    const [updated] = toggle([active.parentMessage]);
    state.active.set({ ...active, parentMessage: updated });
  }
}

export function demoThread(
  channelId: string,
  messageId: string,
  createdBy: string,
): ChatThread {
  return {
    id: `thread-${messageId}`,
    channelId,
    parentMessageId: messageId,
    createdBy,
    createdAt: new Date().toISOString(),
    replyCount: 0,
    following: false,
    followSource: null,
    parentMessage: {
      id: messageId,
      conversationId: channelId,
      channelId,
      authorUserId: 'u-alice',
      authorName: 'Alice Mendes',
      body: 'Mensagem de origem da thread (demo).',
      createdAt: new Date().toISOString(),
      status: 'persisted',
      mine: false,
      threadId: `thread-${messageId}`,
      replyCount: 0,
    },
  };
}
