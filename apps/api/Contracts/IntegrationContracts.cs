using System.Text.Json;

public sealed record CreateIntegrationBotRequest(string Name, Guid[]? ChannelIds, bool AllowDms);

public sealed record UpdateIntegrationBotRequest(string Name, Guid[]? ChannelIds, bool AllowDms, bool Enabled);

public sealed record IntegrationBotResponse(
    Guid Id,
    string Name,
    bool Enabled,
    bool AllowDms,
    Guid[] ChannelIds,
    bool TokenConfigured,
    string? TokenLast4,
    DateTimeOffset CreatedAt);

public sealed record IntegrationBotSecretResponse(
    Guid Id,
    string Name,
    bool Enabled,
    bool AllowDms,
    Guid[] ChannelIds,
    string Token,
    string TokenLast4,
    DateTimeOffset CreatedAt);

public sealed record IntegrationSendRequest(string Body, string IdempotencyKey, Guid? ThreadId, Guid[]? AttachmentIds = null);

public sealed record PluginHistoryGrantResponse(
    Guid Id,
    Guid ChannelId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    DateTimeOffset? RevokedAt);

public sealed record PluginQuotaSnapshot(string Capability, int Used, int Limit);

public sealed record GrantPluginHistoryRequest(Guid ChannelId, DateTimeOffset WindowStart, DateTimeOffset WindowEnd);

public sealed record PostPluginInteractiveRequest(string Body, string[]? Actions, string IdempotencyKey, int? ExpiresInSeconds);

public sealed record PluginInteractiveResponse(Guid MessageId, Guid ChannelId, long Sequence, DateTimeOffset ExpiresAt, bool Idempotent);

public sealed record InvokePluginActionRequest(string Action, string IdempotencyKey);

public sealed record PluginActionResponse(string Action, string Status, bool Idempotent);

public sealed record PluginHistoryItem(Guid Id, long Sequence, string Body, DateTimeOffset CreatedAt, Guid AuthorId, bool AuthorIsBot);

public sealed record PluginHistoryResponse(PluginHistoryItem[] Messages);

public sealed record PluginAttachmentRequest(string FileName, string ContentType, long SizeBytes, string IdempotencyKey);

public sealed record IntegrationDirectMessageRequest(Guid UserId, string Body, string IdempotencyKey, Guid? ThreadId);

public sealed record InstallPluginRequest(string? BuiltinId, JsonElement? Manifest, Guid[]? ChannelIds, bool AllowDms);

public sealed record UpdateInstalledPluginRequest(bool Enabled);

public sealed record InstalledPluginResponse(
    Guid Id,
    string PluginId,
    string Name,
    string Version,
    string[] Capabilities,
    bool Enabled,
    Guid BotId,
    bool AllowDms,
    Guid[] ChannelIds,
    bool TokenConfigured,
    string? TokenLast4,
    DateTimeOffset InstalledAt,
    DateTimeOffset UpdatedAt,
    string[] Hooks,
    string[] SubscribedEvents,
    bool CallbackConfigured,
    DateTimeOffset? LastUsedAt,
    PluginHistoryGrantResponse[] HistoryGrants,
    PluginQuotaSnapshot[] Quotas);

public sealed record InstalledPluginSecretResponse(
    Guid Id,
    string PluginId,
    string Name,
    string Version,
    string[] Capabilities,
    bool Enabled,
    Guid BotId,
    bool AllowDms,
    Guid[] ChannelIds,
    string Token,
    string TokenLast4,
    DateTimeOffset InstalledAt,
    DateTimeOffset UpdatedAt,
    string[] Hooks,
    string[] SubscribedEvents,
    bool CallbackConfigured,
    string? CallbackSecret,
    DateTimeOffset? LastUsedAt,
    PluginHistoryGrantResponse[] HistoryGrants,
    PluginQuotaSnapshot[] Quotas);

public sealed record BuiltinPluginResponse(
    string Id,
    string Name,
    string Version,
    string[] Capabilities,
    string[] Commands);

public sealed record InvokePluginCommandRequest(string Name, string? Args, string IdempotencyKey);

public sealed record PluginCommandResponse(
    string Name,
    bool Posted,
    Guid? MessageId,
    long? Sequence,
    bool CallbackDelivered);

public sealed record IntegrationMessageResponse(
    Guid MessageId,
    Guid ChannelId,
    long Sequence,
    DateTimeOffset CreatedAt,
    bool Idempotent);
