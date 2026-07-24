export type MessageStatus = 'sending' | 'sent' | 'failed' | 'persisted';
export type PresenceStatus = 'online' | 'away' | 'offline';

export interface Workspace {
  id: string;
  name: string;
  slug: string;
  role?: string;
}

export interface Space {
  id: string;
  workspaceId: string;
  name: string;
  order: number;
}

export interface Channel {
  id: string;
  workspaceId: string;
  name: string;
  description?: string;
  unreadCount: number;
  isPrivate?: boolean;
  isDirect?: boolean;
  type?: string;
  spaceId?: string | null;
  peerUserId?: string;
  peerDisplayName?: string;
}

export interface WorkspaceMember {
  userId: string;
  displayName: string;
  email: string;
  role: string;
}

export interface SpaceGroup {
  space: Space | null;
  channels: Channel[];
}

export interface ChatUser {
  id: string;
  displayName: string;
  avatarUrl?: string;
  email?: string;
}

export interface MessageAttachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  status?: string;
}

export interface ChatMessage {
  id: string;
  clientMessageId?: string;
  conversationId: string;
  channelId: string;
  authorUserId: string;
  authorName: string;
  body: string;
  createdAt: string;
  editedAt?: string | null;
  deletedAt?: string | null;
  seq?: number;
  status: MessageStatus;
  mine: boolean;
  attachments?: MessageAttachment[];
  threadId?: string | null;
  replyToMessageId?: string | null;
  replyCount?: number;
}

export interface ChatThread {
  id: string;
  channelId: string;
  parentMessageId: string;
  createdBy: string;
  createdAt: string;
  replyCount: number;
  parentMessage?: ChatMessage | null;
}

export interface TypingState {
  channelId: string;
  userId: string;
  displayName: string;
}

export interface SearchMessageHit {
  messageId: string;
  channelId: string;
  channelName: string;
  channelType: string;
  sequence: number;
  authorUserId: string;
  authorDisplayName: string;
  bodyPreview: string;
  createdAt: string;
  rank: number;
}

export interface SearchMessagesResult {
  query: string;
  limit: number;
  items: SearchMessageHit[];
}

export interface AdminStats {
  users: number;
  onlineUsers: number;
  workspaces: number;
  channels: number;
  messages: number;
  realtimeConnections: number;
  outboxPending: number;
  processingFailures: number;
  health: {
    postgres: 'up' | 'down' | 'degraded';
    redis: 'up' | 'down' | 'degraded';
    storage: 'up' | 'down' | 'degraded';
  };
  appVersion: string;
  grafanaUrl: string;
}

export interface AiSummaryResult {
  channelId: string;
  summary: string;
  messageCount: number;
  generatedAt: string;
}
