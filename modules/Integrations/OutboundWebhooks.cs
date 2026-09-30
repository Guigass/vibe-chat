using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// Tenant outbound webhook endpoint (B-048 / B-108 / ADR-020 / ADR-026).
/// Signing secret prefers AES-GCM envelope; legacy plaintext <see cref="Secret"/>
/// remains for dual-read. API responses never return the clear value.
/// Several endpoints may exist per tenant (application limit, not a unique key).
/// </summary>
public sealed class OutboundWebhookEndpoint
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Url { get; set; } = string.Empty;

    /// <summary>Legacy plaintext secret — nullable during expand; cleared after envelope migration.</summary>
    public string? Secret { get; set; }

    public EncryptedSecretEnvelope SigningSecret { get; set; } = new();

    /// <summary>Opt-in event names. Default for migrated rows is MessageCreated only.</summary>
    public string[] SubscribedEvents { get; set; } = [WebhookEventTypes.MessageCreated];

    /// <summary>Empty means every channel the fan-out already applies to.</summary>
    public Guid[] ChannelFilter { get; set; } = [];

    public DateTimeOffset? LastDeliveryAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class WebhookEventTypes
{
    public const string MessageCreated = "MessageCreated";
    public const string MessageEdited = "MessageEdited";
    public const string MessageDeleted = "MessageDeleted";
    public const string ReactionChanged = "ReactionChanged";
    public const string WebhookTest = "WebhookTest";

    public static readonly string[] Subscribable =
    [
        MessageCreated,
        MessageEdited,
        MessageDeleted,
        ReactionChanged
    ];

    public static readonly string[] DefaultSubscribed = [MessageCreated];

    public static bool IsSubscribable(string name) =>
        Subscribable.Contains(name, StringComparer.Ordinal);
}

public static class WebhookPolicies
{
    public const int MaxEndpointsPerTenant = 5;
    public const int MaxNameLength = 80;
    public const int MaxChannelFilter = 32;
    public const int MaxLastErrorLength = 500;
    public const int MinSecretLength = 8;
}

public static class WebhookDelivery
{
    public const string EventHeader = "X-VibeChat-Event";
    public const string SignatureHeader = "X-VibeChat-Signature";
    public const string DeliveryIdHeader = "X-VibeChat-Delivery-Id";
    public const string SignaturePrefix = "sha256=";

    public static string ComputeSignature(string secret, string body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var payload = Encoding.UTF8.GetBytes(body);
        var hash = HMACSHA256.HashData(key, payload);
        return SignaturePrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsValidHttpsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Lab/dev may use http://localhost; production consumers should prefer https.
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp
            && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host == "127.0.0.1"
                || uri.Host == "::1");
    }

    /// <summary>
    /// Channel filter empty = all channels. A non-empty filter delivers only when
    /// <paramref name="channelId"/> is listed. Unknown events never match.
    /// </summary>
    public static bool MatchesSubscription(
        IReadOnlyCollection<string> subscribedEvents,
        IReadOnlyCollection<Guid> channelFilter,
        string eventName,
        Guid channelId)
    {
        if (subscribedEvents.Count == 0
            || !subscribedEvents.Contains(eventName, StringComparer.Ordinal))
        {
            return false;
        }

        return channelFilter.Count == 0 || channelFilter.Contains(channelId);
    }
}

public interface IOutboundWebhookDispatcher
{
    /// <summary>Best-effort fan-out; must not throw to callers that already published realtime.</summary>
    Task TryDispatchAsync(
        TenantId tenantId,
        string eventName,
        Guid deliveryId,
        string payloadJson,
        Guid channelId,
        CancellationToken cancellationToken);

    /// <summary>Admin ping. Does not write a business message. Persists last status.</summary>
    Task<WebhookPingResult> PingAsync(
        TenantId tenantId,
        Guid endpointId,
        CancellationToken cancellationToken);
}

public sealed record WebhookPingResult(bool Ok, int StatusCode, string? Error, int? HttpStatus, string? LastError);
