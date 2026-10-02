import { ApiService } from '../api/api.service';
import { Channel, PendingAnnouncement } from '../../shared/models/chat.models';
import { idsEqual } from './message-sync';

export interface ChannelUnreadDeps {
  api: ApiService;
  isDemo: () => boolean;
  workspaceId: () => string | null | undefined;
  channels: () => readonly Channel[];
  updateChannels: (fn: (list: Channel[]) => Channel[]) => void;
  patchChannel: (channelId: string, patch: Partial<Channel>) => void;
  activeChannelId: () => string | null;
  setOpenedUnreadCount: (count: number) => void;
  setPending: (rows: PendingAnnouncement[]) => void;
  refreshPending: () => Promise<void>;
}

export async function refreshChannelUnreads(deps: ChannelUnreadDeps): Promise<void> {
  if (deps.isDemo()) return;
  const workspaceId = deps.workspaceId();
  if (!workspaceId) return;
  try {
    const rows = await deps.api.getWorkspaceChannelUnreads(workspaceId);
    const byId = new Map(rows.map((row) => [row.channelId.toLowerCase(), row]));
    deps.updateChannels((list) =>
      list.map((channel) => {
        const row = byId.get(channel.id.toLowerCase());
        if (!row) return channel;
        return {
          ...channel,
          unreadCount: row.unreadCount,
          mentionCount: row.mentionCount,
          pendingAnnouncementCount: row.pendingAnnouncementCount,
        };
      }),
    );
    await deps.refreshPending();
  } catch {
    // keep current badges; next reconnect can retry
  }
}

export async function refreshPendingAnnouncementInbox(
  deps: Pick<ChannelUnreadDeps, 'api' | 'isDemo' | 'workspaceId' | 'setPending'>,
): Promise<void> {
  if (deps.isDemo()) return;
  const workspaceId = deps.workspaceId();
  if (!workspaceId) return;
  try {
    const rows = await deps.api.getPendingAnnouncements(workspaceId);
    deps.setPending(rows);
  } catch {
    // inbox degrades; the channel badge still comes from unread
  }
}

export function forgetPendingAnnouncement(
  list: readonly PendingAnnouncement[],
  messageId: string,
): PendingAnnouncement[] {
  return list.filter((item) => !idsEqual(item.messageId, messageId));
}

export async function syncOneChannelUnread(deps: ChannelUnreadDeps, channelId: string): Promise<void> {
  if (deps.isDemo()) return;
  try {
    const counts = await deps.api.getUnreadCount(channelId);
    deps.patchChannel(channelId, {
      unreadCount: counts.unreadCount,
      mentionCount: counts.mentionCount,
      pendingAnnouncementCount: counts.pendingAnnouncementCount,
    });
    if (idsEqual(channelId, deps.activeChannelId())) {
      deps.setOpenedUnreadCount(counts.unreadCount);
    }
  } catch {
    // best-effort
  }
}

export function bumpChannelUnread(list: readonly Channel[], channelId: string): Channel[] {
  return list.map((c) =>
    idsEqual(c.id, channelId) ? { ...c, unreadCount: c.unreadCount + 1 } : c,
  );
}

export function bumpChannelMention(list: readonly Channel[], channelId: string): Channel[] {
  return list.map((c) =>
    idsEqual(c.id, channelId)
      ? { ...c, mentionCount: (c.mentionCount ?? 0) + 1, unreadCount: c.unreadCount + 1 }
      : c,
  );
}
