export type MessageStatus = 'sending' | 'sent' | 'failed' | 'persisted';
export type PresenceStatus = 'online' | 'away' | 'offline';

/** UTF-16 code units — matches server MessageBodyPolicies.MaxLength. */
export const MESSAGE_BODY_MAX_LENGTH = 8000;

/** Show character counter when draft length reaches this threshold. */
export const MESSAGE_BODY_COUNTER_THRESHOLD = 7500;

export function measureMessageBodyLength(text: string): number {
  return text.length;
}

export function isMessageBodyTooLong(text: string): boolean {
  return measureMessageBodyLength(text) > MESSAGE_BODY_MAX_LENGTH;
}

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
  mentionCount?: number;
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
  kind?: 'File' | 'Audio';
  durationMs?: number;
  waveform?: number[];
}

export interface ReactionSummary {
  emoji: string;
  count: number;
  me: boolean;
}

export const REACTION_EMOJI_OPTIONS = ['👍', '❤️', '😂', '🎉', '👀', '✅'] as const;

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
  reactions?: ReactionSummary[];
  mentionsMe?: boolean;
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

export interface AuditEventItem {
  id: string;
  action: string;
  entityType: string;
  entityId?: string | null;
  actorUserId?: string | null;
  occurredAt: string;
  metadataJson: string;
}

export interface AdminConversationItem {
  id: string;
  workspaceId: string;
  name: string;
  type: string;
  spaceId?: string | null;
  peerUserId?: string | null;
  peerDisplayName?: string | null;
}

export interface AdminConversationMessageItem {
  id: string;
  channelId: string;
  conversationId: string;
  sequence: number;
  authorId: string;
  authorName: string;
  body: string;
  createdAt: string;
  editedAt?: string | null;
  deletedAt?: string | null;
  deletedBy?: string | null;
  deletedByName?: string | null;
  threadId?: string | null;
  replyToMessageId?: string | null;
  replyCount: number;
  attachments: Array<{
    id: string;
    fileName: string;
    contentType: string;
    sizeBytes: number;
    status: string;
  }>;
}

export interface SensitiveSettings {
  workspaceId: string;
  ai: {
    processEnabled: boolean;
    processSource: string;
    workspaceEnabled: boolean;
    provider: string;
    apiKeyConfigured: boolean;
    apiKeyMask: string | null;
    secretsWritable: boolean;
  };
  email: {
    enabled: boolean;
    source: string;
    smtpHost: string;
    smtpPort: number;
    smtpUsername: string;
    smtpUsernameConfigured: boolean;
    smtpPasswordConfigured: boolean;
    smtpPasswordMask: string | null;
    smtpFrom: string;
    useStartTls: boolean;
    secretsWritable: boolean;
  };
  webhooks: {
    status: string;
    enabled: boolean;
    url: string;
    urlConfigured: boolean;
    secretConfigured: boolean;
    secretMask: string | null;
    secretsWritable: boolean;
    message: string;
  };
  retention: {
    processEnabled: boolean;
    processSource: string;
    enabled: boolean;
    retentionDays: number;
    defaultRetentionDays: number;
    message: string;
  };
}

export interface UpdateSensitiveSettingsInput {
  workspaceId?: string;
  ai?: {
    workspaceEnabled?: boolean;
    provider?: string;
  };
  email?: {
    enabled?: boolean;
    smtpHost?: string;
    smtpPort?: number;
    smtpUsername?: string;
    smtpFrom?: string;
    useStartTls?: boolean;
  };
  webhooks?: {
    enabled?: boolean;
    url?: string;
    secret?: string;
  };
  retention?: {
    enabled?: boolean;
    retentionDays?: number;
  };
}

export interface AiSummaryResult {
  channelId: string;
  summary: string;
  messageCount: number;
  generatedAt: string;
}

export interface AiSuggestReplyResult {
  channelId: string;
  suggestion: string;
  generatedAt: string;
}
