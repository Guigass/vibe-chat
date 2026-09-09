public sealed record PushPublicKeyResponse(bool Enabled, string? PublicKey);
public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string? UserAgent);
public sealed record PushSubscriptionResponse(
    Guid Id,
    string Endpoint,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

public sealed record ChannelNotificationOverrideResponse(Guid ChannelId, string Level, DateTimeOffset? MutedUntil);

public sealed record NotificationPreferencesResponse(
    string Level,
    bool HidePreview,
    bool DndEnabled,
    TimeOnly? DndStart,
    TimeOnly? DndEnd,
    short DndDays,
    string? TimeZone,
    bool DigestEnabled,
    Guid[] PriorityContactUserIds,
    ChannelNotificationOverrideResponse[] ChannelOverrides,
    Guid[]? FollowAllThreadsChannelIds = null);

public sealed record UpdateNotificationPreferencesRequest(
    string Level,
    bool HidePreview,
    bool DndEnabled,
    TimeOnly? DndStart,
    TimeOnly? DndEnd,
    short DndDays,
    string? TimeZone,
    bool DigestEnabled,
    Guid[]? PriorityContactUserIds);

/// <summary>Duration: "OneHour" | "EightHours" | "UntilTomorrow" | "Indefinite" | null (no mute, e.g. an "All" override).</summary>
public sealed record UpdateChannelNotificationPreferenceRequest(string Level, string? Duration);
public sealed record UpdateChannelFollowAllThreadsRequest(bool Enabled);
