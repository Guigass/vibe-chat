using VibeChat.Infrastructure;
using VibeChat.Messaging;

public sealed record AuditEventResponse(
    Guid Id,
    string Action,
    string EntityType,
    string? EntityId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string MetadataJson);
public sealed record AuditEventsResponse(AuditEventResponse[] Items);
public sealed record AdminConversationResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Type,
    Guid? SpaceId,
    Guid? PeerUserId,
    string? PeerDisplayName);
public sealed record AdminConversationsResponse(AdminConversationResponse[] Items);
public sealed record AdminConversationMessageResponse(
    Guid Id,
    Guid ChannelId,
    Guid ConversationId,
    long Sequence,
    Guid AuthorId,
    string AuthorName,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    Guid? DeletedBy,
    string? DeletedByName,
    Guid? ThreadId,
    Guid? ReplyToMessageId,
    int ReplyCount,
    AttachmentResponse[] Attachments,
    PollDto? Poll = null);
public sealed record AdminConversationMessagesResponse(AdminConversationMessageResponse[] Items);
public sealed record AdminHealthResponse(string Postgres, string Redis, string Storage);
public sealed record AdminDashboardResponse(
    int Users,
    int OnlineUsers,
    int Workspaces,
    int Channels,
    int Messages,
    int RealtimeConnections,
    int OutboxPending,
    int ProcessingFailures,
    AdminHealthResponse Health,
    string AppVersion,
    string GrafanaUrl);
public sealed record UpdateAiSensitiveSettingsRequest(
    bool? WorkspaceEnabled = null,
    string? Provider = null,
    string? ApiKey = null,
    bool? ProcessEnabled = null,
    string? OpenRouterBaseUrl = null);
public sealed record UpdateEmailSensitiveSettingsRequest(
    bool? Enabled = null,
    string? SmtpHost = null,
    int? SmtpPort = null,
    string? SmtpUsername = null,
    string? SmtpPassword = null,
    string? SmtpFrom = null,
    bool? UseStartTls = null,
    bool? ProcessEnabled = null);
public sealed record UpdateWebhooksSensitiveSettingsRequest(
    bool? Enabled = null,
    string? Url = null,
    string? Secret = null);
public sealed record UpdateRetentionSensitiveSettingsRequest(
    bool? Enabled = null,
    int? RetentionDays = null,
    bool? ProcessEnabled = null,
    int? DefaultRetentionDays = null,
    int? BatchSize = null,
    int? IntervalMinutes = null);
public sealed record UpdateLinkPreviewSettingsRequest(
    bool? Enabled = null,
    bool? ProcessEnabled = null,
    int? TimeoutMs = null);
public sealed record UpdatePushSettingsRequest(
    bool? ProcessEnabled = null,
    string? VapidPrivateKey = null);
public sealed record UpdateSensitiveSettingsRequest(
    Guid? WorkspaceId = null,
    UpdateAiSensitiveSettingsRequest? Ai = null,
    UpdateEmailSensitiveSettingsRequest? Email = null,
    UpdateWebhooksSensitiveSettingsRequest? Webhooks = null,
    UpdateRetentionSensitiveSettingsRequest? Retention = null,
    UpdateLinkPreviewSettingsRequest? LinkPreview = null,
    UpdateFilesSettingsRequest? Files = null,
    UpdateRateLimitSettingsRequest? RateLimit = null,
    UpdatePushSettingsRequest? Push = null);
public sealed record RotateCredentialRequest(
    Guid? WorkspaceId = null,
    string? Value = null);
public sealed record RotateVapidRequest(
    Guid? WorkspaceId = null,
    string? PublicKey = null,
    string? PrivateKey = null,
    string? Subject = null);
