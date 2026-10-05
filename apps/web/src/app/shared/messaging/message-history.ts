export const MOVED_BODY = '<system:moved>';

export interface MessageVersionItem {
  version: number;
  body: string;
  actorUserId: string;
  createdAt: string;
}

export interface MessageHistoryPayload {
  versions: MessageVersionItem[];
  currentBody: string;
  editedAt?: string | null;
}

export interface MessageMoveLink {
  accessible: boolean;
  channelId?: string | null;
  messageId?: string | null;
  sequence?: number | null;
}

export interface MessageMoveLinks {
  destination: MessageMoveLink;
  origin: MessageMoveLink;
}
