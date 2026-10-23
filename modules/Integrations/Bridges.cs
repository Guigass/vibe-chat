using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// External bridge contract (B-138 / ADR-054). The API stores the plumb and
/// exchanges envelopes. It does not open a socket to the remote network.
/// </summary>
public sealed class BridgeOptions
{
    public const string SectionName = "Integrations:Bridges";

    /// <summary>Off by default. Bridge routes return 404 until an operator turns them on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Stable id of this installation. Echoes of this id are loops.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Exact host pins. Anything else, including http(s) URLs, is rejected.</summary>
    public string[] PinnedHosts { get; set; } = [];
}

public static class BridgeErrors
{
    public const string Disabled = "BridgesDisabled";
    public const string InstanceMissing = "BridgeInstanceMissing";
    public const string NotFound = "BridgeNotFound";
    public const string ChannelTaken = "BridgeChannelTaken";
    public const string ChannelUnsupported = "BridgeChannelUnsupported";
    public const string DestinationUnpinned = "BridgeDestinationUnpinned";
    public const string EgressBlocked = "BridgeEgressBlocked";
    public const string CapabilityUnknown = "BridgeCapabilityUnknown";
    public const string CapabilityDenied = "BridgeCapabilityDenied";
    public const string MessagesRequired = "BridgeMessagesRequired";
    public const string EnvelopeInvalid = "BridgeEnvelopeInvalid";
    public const string ConsentRequired = "BridgeConsentRequired";
    public const string TrafficBlocked = "BridgeTrafficBlocked";
    public const string ScopeMismatch = "BridgeScopeMismatch";
    public const string IdempotencyConflict = "BridgeIdempotencyConflict";
    public const string TargetRequired = "BridgeTargetRequired";
    public const string StateConflict = "BridgeStateConflict";
}

public static class BridgeRules
{
    public const string EnvelopeSchema = "vibechat.bridge.envelope.v1";
    public const int SchemaVersion = 1;
    public const string DeleteLimitation = "remote-delete-best-effort";
    public const string CopyPolicy = "explicit-consent";
    public const string StatusPending = "pending";
    public const string StatusActive = "active";
    public const string StatusPaused = "paused";
    public const string StatusDisconnected = "disconnected";
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
    public const string Message = "message";
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string Reaction = "reaction";
    public const string File = "file";
    public const string Applied = "applied";
    public const string Skipped = "skipped";
    public const string Limited = "limited";
    public const string Loop = "loop";
    public const int MaxPull = 50;

    public static readonly string[] Capabilities = [Message, Edit, Delete, Reaction, File];

    public static bool DeleteRemoteGuaranteed => false;

    public static bool AllowsTraffic(string? status) => status == StatusActive;

    public static bool TryNegotiate(
        IReadOnlyCollection<string>? requested,
        IReadOnlyCollection<string>? offered,
        out string[] granted,
        out string error)
    {
        granted = [];
        error = BridgeErrors.CapabilityUnknown;
        var asked = NormalizeCapabilities(requested);
        var remote = NormalizeCapabilities(offered);
        if (asked is null || remote is null)
        {
            return false;
        }

        granted = asked.Intersect(remote, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!granted.Contains(Message, StringComparer.Ordinal))
        {
            error = BridgeErrors.MessagesRequired;
            granted = [];
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool HostPinned(string? host, IReadOnlyCollection<string>? pinned)
    {
        if (string.IsNullOrWhiteSpace(host) || pinned is null || pinned.Count == 0)
        {
            return false;
        }

        var value = host.Trim();
        if (value.Contains("://", StringComparison.Ordinal) || value.Contains('/') || value.Contains('@') || value.Contains(':'))
        {
            return false;
        }

        return pinned.Any(item => string.Equals(item?.Trim(), value, StringComparison.OrdinalIgnoreCase));
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
            || text.Contains('@');
    }

    public static bool TryNetwork(string? value, out string network)
    {
        network = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (network.Length is < 1 or > 40)
        {
            return false;
        }

        if (!char.IsAsciiLetterOrDigit(network[0]))
        {
            return false;
        }

        return network.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    public static bool TryRoom(string? value, out string room)
    {
        room = (value ?? string.Empty).Trim();
        return room.Length is >= 1 and <= 80 && room.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');
    }

    public static bool TrySubject(string? value, out string subject)
    {
        subject = (value ?? string.Empty).Trim();
        return subject.Length is >= 1 and <= 120
            && subject.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '@' or '-');
    }

    public static bool TryEventId(string? value, out string eventId)
    {
        eventId = (value ?? string.Empty).Trim();
        return eventId.Length is >= 8 and <= 120
            && eventId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');
    }

    public static bool TryDisplayName(string? value, out string displayName)
    {
        displayName = (value ?? string.Empty).Trim();
        if (displayName.Length is < 1 or > 80)
        {
            return false;
        }

        return displayName.All(character => !char.IsControl(character));
    }

    public static bool TryFileName(string? value, out string fileName)
    {
        fileName = (value ?? string.Empty).Trim();
        return fileName.Length is >= 1 and <= 120
            && !fileName.Contains('/', StringComparison.Ordinal)
            && !fileName.Contains('\\', StringComparison.Ordinal)
            && !fileName.Contains("://", StringComparison.Ordinal)
            && fileName.All(character => !char.IsControl(character));
    }

    public static string? SafeLink(string? link, string destinationHost)
    {
        if (string.IsNullOrWhiteSpace(link) || !Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        if (!string.Equals(uri.Host, destinationHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    public static bool IsLoop(string? originInstanceId, string localInstanceId, string remoteEventId, IReadOnlySet<string> outboundEventIds) =>
        string.Equals(originInstanceId?.Trim(), localInstanceId, StringComparison.Ordinal)
        || outboundEventIds.Contains(remoteEventId);

    public static string Canonical(
        string eventType,
        string eventId,
        string originInstanceId,
        string destinationHost,
        string remoteRoom,
        Guid channelId,
        string remoteSubject,
        string displayName,
        string? body,
        long cursor,
        string correlationId)
    {
        return "{"
            + "\"schema\":" + Quote(EnvelopeSchema)
            + ",\"schemaVersion\":" + SchemaVersion
            + ",\"eventType\":" + Quote(eventType)
            + ",\"eventId\":" + Quote(eventId)
            + ",\"originInstanceId\":" + Quote(originInstanceId)
            + ",\"destinationHost\":" + Quote(destinationHost)
            + ",\"remoteRoom\":" + Quote(remoteRoom)
            + ",\"channelId\":" + Quote(channelId.ToString("D"))
            + ",\"remoteSubject\":" + Quote(remoteSubject)
            + ",\"displayName\":" + Quote(displayName)
            + ",\"body\":" + (body is null ? "null" : Quote(body))
            + ",\"cursor\":" + cursor
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

    public static string LocalEventId(Guid messageId) => "local:" + messageId.ToString("N") + ":message";

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

public sealed class Bridge
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public ChannelId ChannelId { get; set; }
    public Guid BotId { get; set; }
    public string Network { get; set; } = string.Empty;
    public string RemoteRoom { get; set; } = string.Empty;
    public string DestinationHost { get; set; } = string.Empty;
    public string Status { get; set; } = BridgeRules.StatusPending;
    public string[] Capabilities { get; set; } = [];
    public bool DeleteRemoteGuaranteed { get; set; }
    public DateTimeOffset? ConsentAt { get; set; }
    public Guid? ConsentedByUserId { get; set; }
    public long LocalCheckpoint { get; set; }
    public long RemoteCheckpoint { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BridgeIdentity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid BridgeId { get; set; }
    public string RemoteSubject { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Guid LocalMarker { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BridgeEvent
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid BridgeId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string RemoteEventId { get; set; } = string.Empty;
    public Guid? LocalMessageId { get; set; }
    public string RemoteSubject { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Link { get; set; }
    public long Cursor { get; set; }
    public string Outcome { get; set; } = BridgeRules.Applied;
    public DateTimeOffset CreatedAt { get; set; }
}
