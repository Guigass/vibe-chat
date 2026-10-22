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

public sealed record IntegrationSendRequest(string Body, string IdempotencyKey, Guid? ThreadId);

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
    bool CallbackConfigured);

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
    string? CallbackSecret);

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
