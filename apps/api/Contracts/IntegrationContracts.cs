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

public sealed record IntegrationMessageResponse(
    Guid MessageId,
    Guid ChannelId,
    long Sequence,
    DateTimeOffset CreatedAt,
    bool Idempotent);
