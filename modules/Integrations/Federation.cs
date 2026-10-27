using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// Server-to-server federation (B-065 / ADR-063). The API stores trust and
/// rooms and exchanges signed envelopes. It does not open a socket to the peer.
/// </summary>
public sealed class FederationOptions
{
    public const string SectionName = "Integrations:Federation";

    /// <summary>Off by default. Federation routes return 404 until an operator turns them on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Stable id of this installation. Echoes of this id are loops.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Instance allowlist. A workspace can trust only a domain on this list.</summary>
    public string[] AllowedDomains { get; set; } = [];

    /// <summary>How long an envelope stays acceptable after <c>expiresAt</c>.</summary>
    public int ClockSkewSeconds { get; set; } = 300;

    /// <summary>Maximum lifetime a peer may put on <c>expiresAt</c>.</summary>
    public int MaxTtlSeconds { get; set; } = 600;
}

public static class FederationErrors
{
    public const string Disabled = "FederationDisabled";
    public const string InstanceMissing = "FederationInstanceMissing";
    public const string NotFound = "FederationNotFound";
    public const string DomainUnpinned = "FederationDomainUnpinned";
    public const string EgressBlocked = "FederationEgressBlocked";
    public const string VersionUnsupported = "FederationVersionUnsupported";
    public const string CapabilityUnknown = "FederationCapabilityUnknown";
    public const string MessagesRequired = "FederationMessagesRequired";
    public const string EnvelopeInvalid = "FederationEnvelopeInvalid";
    public const string SignatureInvalid = "FederationSignatureInvalid";
    public const string Expired = "FederationExpired";
    public const string DestinationMismatch = "FederationDestinationMismatch";
    public const string DomainMismatch = "FederationDomainMismatch";
    public const string ConsentRequired = "FederationConsentRequired";
    public const string TrafficBlocked = "FederationTrafficBlocked";
    public const string ScopeMismatch = "FederationScopeMismatch";
    public const string IdempotencyConflict = "FederationIdempotencyConflict";
    public const string ChannelUnsupported = "FederationChannelUnsupported";
    public const string ChannelTaken = "FederationChannelTaken";
    public const string RoomTaken = "FederationRoomTaken";
    public const string StateConflict = "FederationStateConflict";
    public const string RotationPending = "FederationRotationPending";
    public const string KeyUnknown = "FederationKeyUnknown";
}

public static class FederationRules
{
    public const string EnvelopeSchema = "vibechat.federation.envelope.v1";
    public const int SchemaVersion = 1;
    public const string DeleteLimitation = "remote-delete-and-retention-best-effort";
    public const string CopyPolicy = "explicit-consent";
    public const string CopiedData = "channel-messages";
    public const string StatusPending = "pending";
    public const string StatusActive = "active";
    public const string StatusPaused = "paused";
    public const string StatusRevoked = "revoked";
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
    public const string Message = "message";
    public const string Delete = "delete";
    public const string Applied = "applied";
    public const string Skipped = "skipped";
    public const string Limited = "limited";
    public const string Loop = "loop";
    public const int MaxPull = 50;
    public const string InitialKeyId = "k1";

    public static readonly string[] Capabilities = [Message, Delete];

    public static bool DeleteRemoteGuaranteed => false;

    public static bool AllowsTraffic(string? status) => status == StatusActive;

    public static bool TryNegotiate(
        IReadOnlyCollection<string>? requested,
        IReadOnlyCollection<string>? offered,
        int remoteSchemaVersion,
        out string[] granted,
        out string error)
    {
        granted = [];
        error = FederationErrors.VersionUnsupported;
        if (remoteSchemaVersion != SchemaVersion)
        {
            return false;
        }

        error = FederationErrors.CapabilityUnknown;
        var asked = NormalizeCapabilities(requested);
        var remote = NormalizeCapabilities(offered);
        if (asked is null || remote is null)
        {
            return false;
        }

        granted = asked.Intersect(remote, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!granted.Contains(Message, StringComparer.Ordinal))
        {
            error = FederationErrors.MessagesRequired;
            granted = [];
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool DomainAllowed(string? domain, IReadOnlyCollection<string>? allowed)
    {
        if (!TryDomain(domain, out var value) || allowed is null || allowed.Count == 0)
        {
            return false;
        }

        return allowed.Any(item => TryDomain(item, out var pin) && string.Equals(pin, value, StringComparison.Ordinal));
    }

    public static bool EgressBlocked(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        return text.Contains("://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || text.Contains('@')
            || text.Contains('/');
    }

    public static bool TryDomain(string? value, out string domain)
    {
        domain = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (domain.Length is < 1 or > 80 || EgressBlocked(domain) || domain.Contains(':'))
        {
            return false;
        }

        if (!char.IsAsciiLetterOrDigit(domain[0]) || !char.IsAsciiLetterOrDigit(domain[^1]))
        {
            return false;
        }

        return domain.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-');
    }

    public static bool TryInstance(string? value, out string instanceId)
    {
        instanceId = (value ?? string.Empty).Trim();
        return instanceId.Length is >= 1 and <= 64
            && instanceId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');
    }

    public static bool TryRoom(string? value, out string room) => BridgeRules.TryRoom(value, out room);

    public static bool TrySubject(string? value, out string subject) => BridgeRules.TrySubject(value, out subject);

    public static bool TryEventId(string? value, out string eventId) => BridgeRules.TryEventId(value, out eventId);

    public static bool TryDisplayName(string? value, out string displayName) => BridgeRules.TryDisplayName(value, out displayName);

    public static bool TryKeyId(string? value, out string keyId)
    {
        keyId = (value ?? string.Empty).Trim();
        return keyId.Length is >= 2 and <= 16
            && keyId.All(character => char.IsAsciiLetterOrDigit(character));
    }

    public static string NextKeyId(string? current)
    {
        if (current is not null && current.StartsWith('k') && int.TryParse(current[1..], out var generation) && generation is >= 1 and < 999)
        {
            return "k" + (generation + 1);
        }

        return InitialKeyId;
    }

    public static bool IsLoop(string? originInstanceId, string localInstanceId) =>
        string.Equals(originInstanceId?.Trim(), localInstanceId, StringComparison.Ordinal);

    public static bool ExpiryAcceptable(long expiresAtUnix, DateTimeOffset now, int skewSeconds, int maxTtlSeconds)
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix);
        if (expiresAt < now.AddSeconds(-Math.Max(0, skewSeconds)))
        {
            return false;
        }

        return expiresAt <= now.AddSeconds(Math.Max(1, maxTtlSeconds));
    }

    public static string Canonical(
        string eventType,
        string eventId,
        string originInstanceId,
        string originDomain,
        string destinationInstanceId,
        string remoteRoom,
        Guid channelId,
        string remoteSubject,
        string displayName,
        string? body,
        long cursor,
        long expiresAt,
        string keyId,
        string correlationId)
    {
        return "{"
            + "\"schema\":" + Quote(EnvelopeSchema)
            + ",\"schemaVersion\":" + SchemaVersion
            + ",\"eventType\":" + Quote(eventType)
            + ",\"eventId\":" + Quote(eventId)
            + ",\"originInstanceId\":" + Quote(originInstanceId)
            + ",\"originDomain\":" + Quote(originDomain)
            + ",\"destinationInstanceId\":" + Quote(destinationInstanceId)
            + ",\"remoteRoom\":" + Quote(remoteRoom)
            + ",\"channelId\":" + Quote(channelId.ToString("D"))
            + ",\"remoteSubject\":" + Quote(remoteSubject)
            + ",\"displayName\":" + Quote(displayName)
            + ",\"body\":" + (body is null ? "null" : Quote(body))
            + ",\"cursor\":" + cursor
            + ",\"expiresAt\":" + expiresAt
            + ",\"keyId\":" + Quote(keyId)
            + ",\"copyPolicy\":" + Quote(CopyPolicy)
            + ",\"deleteRemoteGuaranteed\":false"
            + ",\"correlationId\":" + Quote(correlationId)
            + "}";
    }

    public static string Sign(string key, string canonical)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(canonical));
        return "hmac-sha256=" + Convert.ToHexString(mac).ToLowerInvariant();
    }

    public static bool SignatureMatches(string? presented, string key, string canonical)
    {
        if (string.IsNullOrWhiteSpace(presented) || !presented.StartsWith("hmac-sha256=", StringComparison.Ordinal))
        {
            return false;
        }

        var expected = Sign(key, canonical);
        var left = Encoding.UTF8.GetBytes(presented.Trim());
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static string LocalEventId(Guid messageId) => "local:" + messageId.ToString("N") + ":message";

    public static string BotName(string domain)
    {
        var name = "fed:" + domain;
        return name.Length <= 80 ? name : name[..80];
    }

    private static string[]? NormalizeCapabilities(IReadOnlyCollection<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>(values.Count);
        foreach (var value in values)
        {
            var item = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (!Capabilities.Contains(item, StringComparer.Ordinal) || normalized.Contains(item, StringComparer.Ordinal))
            {
                return null;
            }

            normalized.Add(item);
        }

        return normalized.ToArray();
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

public sealed class FederationTrust
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid BotId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string RemoteInstanceId { get; set; } = string.Empty;
    public string Status { get; set; } = FederationRules.StatusPending;
    public string[] Capabilities { get; set; } = [];
    public string CurrentKeyId { get; set; } = FederationRules.InitialKeyId;
    public string? PreviousKeyId { get; set; }
    public Guid CurrentTokenId { get; set; }
    public Guid? PreviousTokenId { get; set; }
    public bool DeleteRemoteGuaranteed { get; set; }
    public DateTimeOffset? ConsentAt { get; set; }
    public Guid? ConsentedByUserId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FederationRoom
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid TrustId { get; set; }
    public ChannelId ChannelId { get; set; }
    public string RemoteRoom { get; set; } = string.Empty;
    public long LocalCheckpoint { get; set; }
    public long RemoteCheckpoint { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FederationIdentity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid TrustId { get; set; }
    public string RemoteSubject { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RemoteInstanceId { get; set; } = string.Empty;
    public Guid LocalMarker { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FederationEvent
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid TrustId { get; set; }
    public Guid RoomId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string RemoteEventId { get; set; } = string.Empty;
    public Guid? LocalMessageId { get; set; }
    public string RemoteSubject { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long Cursor { get; set; }
    public string Outcome { get; set; } = FederationRules.Applied;
    public DateTimeOffset CreatedAt { get; set; }
}
