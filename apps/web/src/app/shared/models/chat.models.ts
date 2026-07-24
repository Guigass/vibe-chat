export type MessageStatus = 'sending' | 'sent' | 'failed' | 'persisted';

export interface Workspace {
  id: string;
  name: string;
  slug: string;
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
  peerUserId?: string;
  peerDisplayName?: string;
}

export interface WorkspaceMember {
  userId: string;
  displayName: string;
  email: string;
  role: string;
}

export interface ChatUser {
  id: string;
  displayName: string;
  avatarUrl?: string;
  email?: string;
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
}

export interface TypingState {
  channelId: string;
  userId: string;
  displayName: string;
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
