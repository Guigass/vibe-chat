using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// Tenant outbound webhook endpoint (B-048 / ADR-020). Signing secret prefers AES-GCM envelope;
/// legacy plaintext <see cref="Secret"/> remains for dual-read until contract migration.
/// API responses never return the clear value (masked via <see cref="SecretMasking"/>).
/// </summary>
public sealed class OutboundWebhookEndpoint
{
    public TenantId TenantId { get; set; }
    public bool Enabled { get; set; }
    public string Url { get; set; } = string.Empty;

    /// <summary>Legacy plaintext secret — nullable during expand; cleared after envelope migration.</summary>
    public string? Secret { get; set; }

    public EncryptedSecretEnvelope SigningSecret { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
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
}

public interface IOutboundWebhookDispatcher
{
    /// <summary>Best-effort fan-out; must not throw to callers that already published realtime.</summary>
    Task TryDispatchAsync(
        TenantId tenantId,
        string eventName,
        Guid deliveryId,
        string payloadJson,
        CancellationToken cancellationToken);
}
