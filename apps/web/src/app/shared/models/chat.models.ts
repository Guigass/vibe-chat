import type { MessagingPolicy, MessagingPolicyInput } from '../messaging/messaging-policy';

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
  pendingAnnouncementCount?: number;
  isPrivate?: boolean;
  isDirect?: boolean;
  isGroupDm?: boolean;
  type?: string;
  spaceId?: string | null;
  peerUserId?: string;
  peerDisplayName?: string;
  participantCount?: number;
  participantNames?: string[];
  participantUserIds?: string[];
  hasGuests?: boolean;
}

/** B-109 admin bot. `token` is present only on create/rotate. */
export interface InstalledPlugin {
  id: string;
  pluginId: string;
  name: string;
  version: string;
  capabilities: string[];
  enabled: boolean;
  botId: string;
  allowDms: boolean;
  channelIds: string[];
  tokenConfigured: boolean;
  tokenLast4?: string | null;
  installedAt: string;
  updatedAt: string;
  token?: string | null;
}

export interface IntegrationBot {
  id: string;
  name: string;
  enabled: boolean;
  allowDms: boolean;
  channelIds: string[];
  tokenConfigured: boolean;
  tokenLast4?: string | null;
  createdAt: string;
  token?: string | null;
}

export interface WorkspaceMember {
  userId: string;
  displayName: string;
  email: string;
  role: string;
  avatarUrl?: string | null;
}

/** B-166. A contact group organizes people. It does not grant channel or DM access. */
export interface ContactGroup {
  id: string;
  workspaceId: string;
  kind: 'department' | 'personal';
  name: string;
  order: number;
  ownerUserId: string | null;
  memberUserIds: string[];
}

export interface ContactSection {
  groupId: string | null;
  name: string | null;
  kind: 'department' | 'personal' | null;
  members: WorkspaceMember[];
}

export interface ChannelRosterMember {
  userId: string;
  displayName: string;
  email: string;
  joinedAt?: string | null;
  presence?: 'online' | 'away' | 'offline' | null;
  isGuest: boolean;
}

export interface ChannelRosterPage {
  items: ChannelRosterMember[];
  nextCursor: string | null;
  total: number;
  canManage: boolean;
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
  kind?: 'File' | 'Audio' | 'Video';
  durationMs?: number;
  waveform?: number[];
  /** B-090 — Pending | Ready | Failed */
  thumbnailStatus?: string | null;
  width?: number | null;
  height?: number | null;
  pageCount?: number | null;
}

/** B-091 — Pending | Ready | Failed | Blocked */
export interface MessageLinkPreview {
  id: string;
  url: string;
  title?: string | null;
  description?: string | null;
  siteName?: string | null;
  hasImage: boolean;
  status: string;
}

export interface ReactionSummary {
  emoji: string;
  count: number;
  me: boolean;
}

export interface MessageReplyTo {
  messageId: string;
  authorName: string;
  preview: string;
  deleted: boolean;
}

export interface MessageForwardedFrom {
  messageId: string;
  channelId: string;
  channelName: string;
  authorName: string;
  createdAt: string;
  isDirect?: boolean;
}

export const REACTION_EMOJI_OPTIONS = ['👍', '❤️', '😂', '🎉', '👀', '✅', '🔥', '🙌'] as const;

export interface ChatMessage {
  id: string;
  clientMessageId?: string;
  conversationId: string;
  channelId: string;
  authorUserId: string;
  authorName: string;
  /** B-109 — discreet bot marker on the timeline. */
  authorIsBot?: boolean;
  body: string;
  createdAt: string;
  editedAt?: string | null;
  deletedAt?: string | null;
  movedToMessageId?: string | null;
  movedToChannelId?: string | null;
  movedToSequence?: number | null;
  movedToAccessible?: boolean;
  movedFromMessageId?: string | null;
  movedFromChannelId?: string | null;
  movedFromSequence?: number | null;
  seq?: number;
  status: MessageStatus;
  mine: boolean;
  attachments?: MessageAttachment[];
  threadId?: string | null;
  replyToMessageId?: string | null;
  /** Resolved cite preview (B-084); prefer over raw replyToMessageId in UI. */
  replyTo?: MessageReplyTo | null;
  forwardedFromMessageId?: string | null;
  forwardedFromChannelId?: string | null;
  /** Resolved forward header (B-085). */
  forwardedFrom?: MessageForwardedFrom | null;
  /** Thread anchor id when this MessageCreated is a thread reply (hub). */
  parentMessageId?: string | null;
  replyCount?: number;
  reactions?: ReactionSummary[];
  mentionsMe?: boolean;
  /** B-091 link unfurl card; null after author removes. */
  linkPreview?: MessageLinkPreview | null;
  /** B-092 pinned marker in channel. */
  isPinned?: boolean;
  /** B-093 personal saved marker. */
  isSaved?: boolean;
  /** B-096 channel poll card. */
  poll?: PollSummary | null;
  /** B-112 announcement on a read-only channel message. */
  announcement?: AnnouncementSummary | null;
}

export interface PollVoter {
  userId: string;
  displayName: string;
}

export interface PollOptionSummary {
  id: string;
  text: string;
  position: number;
  voteCount: number;
  percent: number;
  votedByMe: boolean;
  voters?: PollVoter[] | null;
}

export interface AnnouncementSummary {
  messageId: string;
  requiresAcknowledgement: boolean;
  acknowledgeBy?: string | null;
  closedAt?: string | null;
  acknowledgedByMe: boolean;
  acknowledgementCount: number;
  canAcknowledge: boolean;
  canViewReport: boolean;
}

export interface PendingAnnouncement {
  messageId: string;
  channelId: string;
  channelName: string;
  authorName: string;
  bodyPreview: string;
  createdAt: string;
  acknowledgeBy?: string | null;
}

export interface AnnouncementReportItem {
  userId: string;
  displayName: string;
  acknowledgedAt: string;
}

export interface PollSummary {
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
  options: PollOptionSummary[];
}

export interface PinnedMessageItem {
  messageId: string;
  channelId: string;
  sequence: number;
  bodyPreview: string;
  authorName: string;
  pinnedByUserId: string;
  pinnedByName: string;
  pinnedAt: string;
  limit?: number;
}

export interface ScheduleItem {
  kind: 'scheduled_message' | 'reminder';
  id: string;
  status: string;
  dueAtUtc: string;
  timeZone: string;
  body: string | null;
  note: string | null;
  targetKind: string | null;
  channelId: string | null;
  channelName: string | null;
  messageId: string | null;
  threadId: string | null;
  sentMessageId: string | null;
  failureCode: string | null;
  canOpen: boolean;
  createdAt: string;
}

export interface SavedMessageItem {
  messageId: string;
  channelId: string;
  channelName: string;
  channelType: string;
  sequence: number;
  authorUserId: string;
  authorName: string;
  bodyPreview: string;
  note: string | null;
  completedAt: string | null;
  createdAt: string;
  messageRemoved: boolean;
}

export interface ChatThread {
  id: string;
  channelId: string;
  parentMessageId: string;
  createdBy: string;
  createdAt: string;
  replyCount: number;
  parentMessage?: ChatMessage | null;
  following: boolean;
  followSource: 'Manual' | 'Author' | 'Reply' | 'Mention' | null;
}

/** B-102 — an entry in "Threads seguidas". */
export interface FollowedThreadItem {
  threadId: string;
  channelId: string;
  channelName: string;
  channelType: string;
  rootPreview: string;
  rootDeleted: boolean;
  unreadCount: number;
  lastActivityAt: string;
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
  kind?: 'message';
}

export interface SearchChannelHit {
  kind: 'channel';
  channelId: string;
  channelName: string;
  channelType: string;
  rank: number;
}

export interface SearchPersonHit {
  kind: 'person';
  userId: string;
  displayName: string;
  rank: number;
}

export interface SearchAttachmentHit {
  kind: 'attachment';
  attachmentId: string;
  fileName: string;
  messageId: string;
  channelId: string;
  channelName: string;
  channelType: string;
  sequence: number;
  rank: number;
}

export interface SearchMessagesResult {
  query: string;
  limit: number;
  items: SearchMessageHit[];
  total?: number;
  cursor?: string | null;
  channels?: SearchChannelHit[];
  people?: SearchPersonHit[];
  attachments?: SearchAttachmentHit[];
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
    openRouterBaseUrl?: string;
    apiKeyConfigured: boolean;
    apiKeyMask: string | null;
    apiKeySource?: string;
    apiKeyKeyVersion?: number | null;
    apiKeyRotatedAt?: string | null;
    secretsWritable: boolean;
  };
  email: {
    processEnabled?: boolean;
    processSource?: string;
    enabled: boolean;
    source: string;
    smtpHost: string;
    smtpPort: number;
    smtpUsername: string;
    smtpUsernameConfigured: boolean;
    smtpPasswordConfigured: boolean;
    smtpPasswordMask: string | null;
    smtpPasswordSource?: string;
    smtpPasswordKeyVersion?: number | null;
    smtpPasswordRotatedAt?: string | null;
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
    secretSource?: string;
    secretKeyVersion?: number | null;
    secretRotatedAt?: string | null;
    secretsWritable: boolean;
    message: string;
    maxEndpoints?: number;
    endpoints?: WebhookEndpointSettings[];
  };
  retention: {
    processEnabled: boolean;
    processSource: string;
    enabled: boolean;
    retentionDays: number;
    defaultRetentionDays: number;
    batchSize?: number;
    intervalMinutes?: number;
    message: string;
  };
  linkPreview: {
    processEnabled: boolean;
    processSource: string;
    enabled: boolean;
    timeoutMs: number;
    message: string;
  };
  push?: {
    processEnabled: boolean;
    processSource: string;
    vapidPublicKey: string | null;
    vapidConfigured: boolean;
    vapidMask: string | null;
    vapidSource?: string;
    vapidKeyVersion?: number | null;
    vapidRotatedAt?: string | null;
    vapidSubject?: string;
    secretsWritable: boolean;
  };
  files: {
    source: string;
    maxSizeBytes: number;
    maxAttachmentsPerMessage: number;
    presignUploadTtlSeconds: number;
    presignDownloadTtlSeconds: number;
    allowedContentTypes: string[];
    audioMaxSizeBytes: number;
    audioMaxDurationMs: number;
    ceilingMaxSizeBytes: number;
    ceilingMaxAttachmentsPerMessage: number;
  };
  rateLimit: {
    source: string;
    sendPerMinute: number;
    hubPerMinute: number;
    ceilingSendPerMinute: number;
    ceilingHubPerMinute: number;
  };
  messaging: MessagingPolicy;
  encryption: {
    databaseOverridesEnabled: boolean;
    encryptionAvailable: boolean;
    activeKeyVersion: number | null;
    credentialsUsingActiveKey: number;
  };
}

export interface WebhookEndpointSettings {
  id: string;
  name: string;
  enabled: boolean;
  url: string;
  subscribedEvents: string[];
  channelFilter: string[];
  secretConfigured: boolean;
  secretMask: string | null;
  secretSource?: string;
  lastDeliveryAt?: string | null;
  lastStatusCode?: number | null;
  lastError?: string | null;
  updatedAt?: string;
}

export interface UpsertWebhookEndpointInput {
  workspaceId?: string;
  name?: string;
  url?: string;
  enabled?: boolean;
  subscribedEvents?: string[];
  channelFilter?: string[];
  secret?: string;
}

export interface UpdateSensitiveSettingsInput {
  workspaceId?: string;
  ai?: {
    workspaceEnabled?: boolean;
    provider?: string;
    processEnabled?: boolean;
    openRouterBaseUrl?: string;
  };
  email?: {
    enabled?: boolean;
    processEnabled?: boolean;
    smtpHost?: string;
    smtpPort?: number;
    smtpUsername?: string;
    smtpFrom?: string;
    useStartTls?: boolean;
  };
  webhooks?: {
    enabled?: boolean;
    url?: string;
  };
  retention?: {
    enabled?: boolean;
    retentionDays?: number;
    processEnabled?: boolean;
    defaultRetentionDays?: number;
    batchSize?: number;
    intervalMinutes?: number;
  };
  linkPreview?: {
    enabled?: boolean;
    processEnabled?: boolean;
    timeoutMs?: number;
  };
  push?: {
    processEnabled?: boolean;
  };
  files?: {
    maxSizeBytes?: number;
    maxAttachmentsPerMessage?: number;
    presignUploadTtlSeconds?: number;
    presignDownloadTtlSeconds?: number;
    allowedContentTypes?: string[];
    audioMaxSizeBytes?: number;
    audioMaxDurationMs?: number;
  };
  rateLimit?: {
    sendPerMinute?: number;
    hubPerMinute?: number;
  };
  messaging?: MessagingPolicyInput;
}

export interface RotateCredentialInput {
  workspaceId?: string;
  value: string;
}

export interface RotateVapidInput {
  workspaceId?: string;
  publicKey: string;
  privateKey: string;
  subject?: string;
}

export interface CredentialRotateResult {
  configured: boolean;
  mask: string | null;
  keyVersion: number | null;
  rotatedAt: string | null;
}

export interface ReencryptSettingsResult {
  reencrypted: number;
  settings: SensitiveSettings;
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

export interface PushPublicKey {
  enabled: boolean;
  publicKey?: string | null;
}

export interface PushDevice {
  id: string;
  endpoint: string;
  userAgent?: string | null;
  createdAt: string;
  lastSeenAt: string;
}

export type NotificationLevel = 'All' | 'MentionsAndDms' | 'None';

/** "OneHour" | "EightHours" | "UntilTomorrow" | "Indefinite" | null (no mute, e.g. an "All" override). */
export type ChannelMuteDuration = 'OneHour' | 'EightHours' | 'UntilTomorrow' | 'Indefinite';

export interface ChannelNotificationOverride {
  channelId: string;
  level: NotificationLevel;
  mutedUntil?: string | null;
}

export type ChannelMuteAction =
  | { kind: 'mute'; duration: ChannelMuteDuration }
  | { kind: 'all' }
  | { kind: 'default' }
  | { kind: 'follow-all-threads'; enabled: boolean };

export interface NotificationPreferences {
  level: NotificationLevel;
  hidePreview: boolean;
  dndEnabled: boolean;
  dndStart?: string | null;
  dndEnd?: string | null;
  dndDays: number;
  timeZone?: string | null;
  digestEnabled: boolean;
  priorityContactUserIds: string[];
  channelOverrides: ChannelNotificationOverride[];
  /** B-102 — channels where the caller auto-follows every new thread. */
  followAllThreadsChannelIds: string[];
}

export interface WorkspaceTemplateSummary {
  id: string;
  version: number;
  spaceCount: number;
  channelCount: number;
  checklist: string[];
  builtin: boolean;
}

export interface WorkspaceTemplateCatalog {
  builtins: WorkspaceTemplateSummary[];
  custom: WorkspaceTemplateSummary[];
}

export interface TemplatePlanItem {
  kind: string;
  action: string;
  key: string;
  name?: string | null;
  detail?: string | null;
  resourceId?: string | null;
}

export interface TemplatePlan {
  templateId: string;
  version: number;
  hasConflicts: boolean;
  dryRun: boolean;
  idempotent?: boolean;
  items: TemplatePlanItem[];
}

export interface OnboardingItem {
  key: string;
  state: string;
}

export interface WorkspaceOnboardingState {
  status: string;
  templateId?: string | null;
  templateVersion?: number | null;
  items: OnboardingItem[];
}
