using VibeChat.SharedKernel;

namespace VibeChat.Realtime;

/// <summary>
/// Optional live audio/video huddle (B-147). Media does not pass through the API.
/// </summary>
public sealed class LiveOptions
{
    public const string SectionName = "Features:Live";

    /// <summary>Off by default. Routes return 404 and chat does not depend on this flag.</summary>
    public bool Enabled { get; set; }

    /// <summary>Video publish is a separate policy. Off means audio only.</summary>
    public bool VideoEnabled { get; set; }

    /// <summary><c>none</c>, <c>dev</c>, <c>down</c> or <c>livekit</c>.</summary>
    public string Provider { get; set; } = "none";

    public int MaxParticipants { get; set; } = LiveRules.DefaultMaxParticipants;

    public int MaxSessionsPerWorkspace { get; set; } = LiveRules.DefaultMaxSessions;

    public int TokenTtlSeconds { get; set; } = LiveRules.DefaultTokenTtlSeconds;

    /// <summary>Lab HMAC key for <c>Provider=dev</c>. Empty uses the built-in lab key.</summary>
    public string DevSigningKey { get; set; } = "";

    public LiveKitOptions LiveKit { get; set; } = new();
}

public sealed class LiveKitOptions
{
    public string Url { get; set; } = "";

    public string ApiKey { get; set; } = "";

    public string ApiSecret { get; set; } = "";
}

public static class LiveErrors
{
    public const string Disabled = "LiveDisabled";
    public const string ProviderUnavailable = "LiveProviderUnavailable";
    public const string NotFound = "LiveSessionNotFound";
    public const string Ended = "LiveSessionEnded";
    public const string Capacity = "LiveCapacityExceeded";
    public const string VideoDisabled = "LiveVideoDisabled";
    public const string TokenInvalid = "LiveTokenInvalid";
    public const string TokenExpired = "LiveTokenExpired";
    public const string TokenReplayed = "LiveTokenReplayed";
    public const string RoomMismatch = "LiveTokenRoomMismatch";
    public const string ReactionInvalid = "LiveReactionInvalid";
    public const string IdempotencyRequired = "IdempotencyKeyRequired";
    public const string IdempotencyConflict = "LiveIdempotencyConflict";
}

public static class LiveRules
{
    public const string EventType = "live.session.changed";
    public const int SchemaVersion = 1;
    public const string StateActive = "active";
    public const string StateEnded = "ended";
    public const string ProviderNone = "none";
    public const string ProviderDev = "dev";
    public const string ProviderDown = "down";
    public const string ProviderLiveKit = "livekit";
    public const string HttpClientName = "vibechat-livekit";
    public const string LabSigningKey = "vibechat-local-live-dev-signing-key";
    public const int DefaultMaxParticipants = 8;
    public const int DefaultMaxSessions = 2;
    public const int DefaultTokenTtlSeconds = 60;
    public const int MinTokenTtlSeconds = 15;
    public const int MaxTokenTtlSeconds = 120;
    public const int SigningKeyMinBytes = 32;

    public static readonly string[] Reactions = ["👍", "✅", "👀", "🎉"];

    public static string NormalizeProvider(string? provider) =>
        (provider ?? "").Trim().ToLowerInvariant() switch
        {
            ProviderDev => ProviderDev,
            ProviderDown => ProviderDown,
            ProviderLiveKit => ProviderLiveKit,
            _ => ProviderNone
        };

    public static int ParticipantLimit(int configured) => Math.Clamp(configured, 2, 32);

    public static int SessionLimit(int configured) => Math.Clamp(configured, 1, 20);

    public static int TokenTtl(int configured) =>
        Math.Clamp(configured <= 0 ? DefaultTokenTtlSeconds : configured, MinTokenTtlSeconds, MaxTokenTtlSeconds);

    public static string Room(TenantId tenant, ChannelId channel, Guid sessionId) =>
        $"t/{tenant.Value:N}/c/{channel.Value:N}/s/{sessionId:N}";

    public static bool RoomMatches(string? tokenRoom, string sessionRoom) =>
        !string.IsNullOrEmpty(tokenRoom)
        && string.Equals(tokenRoom, sessionRoom, StringComparison.Ordinal);

    public static bool HasCapacity(int active, int limit) => active < limit;

    public static bool TryReaction(string? raw, out string reaction)
    {
        reaction = (raw ?? "").Trim();
        return Reactions.Contains(reaction, StringComparer.Ordinal);
    }

    public static IReadOnlyList<string> PublishSources(bool video) =>
        video ? ["microphone", "camera"] : ["microphone"];

    public static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    public static string HttpProbeUrl(string signalUrl)
    {
        if (signalUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + signalUrl["wss://".Length..].TrimEnd('/');
        }

        if (signalUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + signalUrl["ws://".Length..].TrimEnd('/');
        }

        return signalUrl.TrimEnd('/');
    }
}

public sealed class LiveSession
{
    public Guid Id { get; set; }

    public TenantId TenantId { get; set; }

    public WorkspaceId WorkspaceId { get; set; }

    public ChannelId ChannelId { get; set; }

    public string State { get; set; } = LiveRules.StateActive;

    public bool VideoAllowed { get; set; }

    public UserId CreatedBy { get; set; }

    public string IdempotencyKey { get; set; } = "";

    public string RequestHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }
}

public sealed class LiveParticipant
{
    public Guid Id { get; set; }

    public TenantId TenantId { get; set; }

    public Guid SessionId { get; set; }

    public UserId UserId { get; set; }

    public bool Muted { get; set; }

    public bool CameraOn { get; set; }

    public bool AudioConnected { get; set; }

    public string? Reaction { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    public DateTimeOffset? LeftAt { get; set; }
}

public sealed class LiveJoinToken
{
    public Guid Id { get; set; }

    public TenantId TenantId { get; set; }

    public Guid SessionId { get; set; }

    public UserId UserId { get; set; }

    public string TokenHash { get; set; } = "";

    public string Room { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed record LiveProviderStatus(bool Healthy, string Detail);

public sealed record LiveTokenRequest(
    string Room,
    string Identity,
    bool CanPublishAudio,
    bool CanPublishVideo,
    DateTimeOffset ExpiresAt,
    string Jti);

public sealed record LiveAccessToken(string Token, string? SignalUrl, string Room, string Jti, DateTimeOffset ExpiresAt);

public sealed record LiveTokenClaims(
    string Jti,
    string Room,
    string Identity,
    bool CanPublishAudio,
    bool CanPublishVideo,
    DateTimeOffset ExpiresAt);

public interface ILiveMediaProvider
{
    string Name { get; }

    Task<LiveProviderStatus> StatusAsync(CancellationToken cancellationToken);

    LiveAccessToken Issue(LiveTokenRequest request);

    LiveTokenClaims? Read(string token);
}
