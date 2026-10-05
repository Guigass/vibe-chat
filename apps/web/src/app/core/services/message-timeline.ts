import { ChatMessage } from '../../shared/models/chat.models';
import { gapFillAfterSeq, idsEqual, maxSeqForChannel, minSeqForChannel, mergeMessagesById } from './message-sync';
import {
  ChannelPaginationState,
  defaultPagination,
  demoChannelMessages,
  mergeChannelPage,
} from './message-patches';

export interface MessagePage {
  messages: ChatMessage[];
  hasMoreBefore: boolean;
  hasMoreAfter: boolean;
}

export interface MessageTimelineDeps {
  isDemo: () => boolean;
  isOfflineDemo: () => boolean;
  activeChannelId: () => string | null | undefined;
  activeChannel: () => { id: string } | null | undefined;
  messages: () => readonly ChatMessage[];
  updateMessages: (fn: (list: ChatMessage[]) => ChatMessage[]) => void;
  setMessages: (list: ChatMessage[]) => void;
  pagination: () => Record<string, ChannelPaginationState>;
  patchPagination: (channelId: string, patch: Partial<ChannelPaginationState>) => void;
  setLoading: (value: boolean) => void;
  joinChannel: (channelId: string) => Promise<void>;
  getMessages: (
    channelId: string,
    options?: { take?: number; after?: number; before?: number; around?: number },
  ) => Promise<MessagePage>;
  normalize: (message: ChatMessage) => ChatMessage;
  requestScroll: (channelId: string, messageId: string) => number;
  cancelScroll: (requestId?: number) => void;
  highlight: (messageId: string) => void;
  setViewingLatest: (value: boolean) => void;
}

export interface MessageTimeline {
  loadOlderMessages(channelId?: string): Promise<boolean>;
  loadChannel(channelId: string): Promise<void>;
  gapFillChannel(channelId: string): Promise<void>;
  gapFillActive(): Promise<void>;
  jumpToSequence(
    channelId: string,
    seq: number,
    messageId: string,
  ): Promise<'ok' | 'deleted' | 'missing'>;
}

export function createMessageTimeline(getDeps: () => MessageTimelineDeps): MessageTimeline {
  const gapFillInFlight = new Set<string>();

  const applyChannelPage = (
    deps: MessageTimelineDeps,
    channelId: string,
    page: MessagePage,
    mode: { replace?: boolean; prepend?: boolean } = {},
  ): void => {
    const normalized = page.messages.map((m) => deps.normalize(m));
    deps.updateMessages((current) => mergeChannelPage(current, channelId, normalized, mode));
    deps.patchPagination(channelId, {
      hasMoreBefore: page.hasMoreBefore,
      hasMoreAfter: page.hasMoreAfter,
      loadingOlder: false,
      loadError: null,
    });
  };

  return {
    async loadOlderMessages(channelId?: string): Promise<boolean> {
      const deps = getDeps();
      const id = channelId ?? deps.activeChannelId();
      if (!id || deps.isDemo() || deps.isOfflineDemo()) return false;

      const state = deps.pagination()[id] ?? defaultPagination();
      if (!state.hasMoreBefore || state.loadingOlder) return false;

      const minSeq = minSeqForChannel(deps.messages(), id);
      if (minSeq <= 0) return false;

      deps.patchPagination(id, { loadingOlder: true, loadError: null });
      try {
        const page = await deps.getMessages(id, { before: minSeq, take: 50 });
        if (!page.messages.length) {
          deps.patchPagination(id, { hasMoreBefore: false, loadingOlder: false });
          return false;
        }
        applyChannelPage(deps, id, page, { prepend: true });
        return true;
      } catch {
        deps.patchPagination(id, { loadingOlder: false, loadError: 'retry' });
        return false;
      }
    },

    async loadChannel(channelId: string): Promise<void> {
      const deps = getDeps();
      const hasCached = deps.messages().some((message) => idsEqual(message.channelId, channelId));
      // Keep a cached timeline mounted so channel switches do not destroy
      // `.timeline__list` and drop the sticky-bottom pin (OPS-E2E-B097).
      if (!hasCached) {
        deps.setLoading(true);
      }
      try {
        await deps.joinChannel(channelId);
        if (deps.isDemo()) {
          deps.setMessages(demoChannelMessages(channelId));
          deps.patchPagination(channelId, {
            hasMoreBefore: false,
            hasMoreAfter: false,
            loadError: null,
          });
        } else {
          const page = await deps.getMessages(channelId, { take: 50 });
          applyChannelPage(deps, channelId, page, { replace: true });
        }
      } catch {
        deps.updateMessages((current) => current.filter((message) => !idsEqual(message.channelId, channelId)));
        deps.patchPagination(channelId, { ...defaultPagination(), loadError: 'retry' });
      } finally {
        deps.setLoading(false);
      }
    },

    async gapFillChannel(channelId: string): Promise<void> {
      const deps = getDeps();
      if (!channelId || deps.isDemo() || deps.isOfflineDemo()) return;
      if (gapFillInFlight.has(channelId)) return;
      gapFillInFlight.add(channelId);
      try {
        const localMax = maxSeqForChannel(deps.messages(), channelId);
        const after = gapFillAfterSeq(localMax);
        const page = await deps.getMessages(channelId, { after, take: 100 });
        if (!page.messages.length) return;
        deps.updateMessages((current) =>
          mergeMessagesById(
            current,
            page.messages.map((m) => deps.normalize(m)),
          ),
        );
        deps.patchPagination(channelId, {
          hasMoreAfter: page.hasMoreAfter,
        });
      } catch {
        // best-effort; live events or next reconnect can retry
      } finally {
        gapFillInFlight.delete(channelId);
      }
    },

    async gapFillActive(): Promise<void> {
      const channelId = getDeps().activeChannel()?.id;
      if (channelId) {
        await this.gapFillChannel(channelId);
      }
    },

    async jumpToSequence(
      channelId: string,
      seq: number,
      messageId: string,
    ): Promise<'ok' | 'deleted' | 'missing'> {
      const deps = getDeps();
      if (!channelId || seq <= 0 || !messageId) return 'missing';
      let requestId = deps.requestScroll(channelId, messageId);
      if (deps.isDemo() || deps.isOfflineDemo()) {
        deps.highlight(messageId);
        return 'ok';
      }

      deps.setLoading(true);
      try {
        await deps.joinChannel(channelId);
        const page = await deps.getMessages(channelId, { around: seq, take: 50 });
        const target =
          page.messages.find((m) => idsEqual(m.id, messageId)) ??
          page.messages.find((m) => m.seq === seq);
        if (!target) {
          deps.cancelScroll(requestId);
          return 'missing';
        }
        if (target.deletedAt) {
          deps.cancelScroll(requestId);
          return 'deleted';
        }

        if (!idsEqual(target.id, messageId)) {
          deps.cancelScroll(requestId);
          requestId = deps.requestScroll(channelId, target.id);
        }

        applyChannelPage(deps, channelId, page, { replace: true });
        deps.highlight(target.id);
        deps.setViewingLatest(false);
        return 'ok';
      } catch {
        deps.cancelScroll(requestId);
        return 'missing';
      } finally {
        deps.setLoading(false);
      }
    },
  };
}
