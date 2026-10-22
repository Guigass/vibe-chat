using VibeChat.Integrations;

namespace VibeChat.Sdk;

public sealed record SdkVerification(bool Accepted, string Reason);

/// <summary>
/// Packaged surface of the in-repo SDK (B-136). Behavior is the Integrations
/// contract. This assembly does not log tokens and is not published to a feed.
/// </summary>
public static class VibeChatPluginSdk
{
    public const string Version = PluginSdkCatalog.Version;
    public const string License = PluginSdkCatalog.License;
    public const string SandboxSecret = PluginSdkCatalog.SandboxSecret;

    public static string ComputeSignature(string secret, string body) =>
        WebhookDelivery.ComputeSignature(secret, body);

    public static SdkVerification Verify(
        string secret,
        string body,
        string? signature,
        string? deliveryId,
        DateTimeOffset deliveredAt,
        DateTimeOffset now,
        IReadOnlyCollection<string>? seenDeliveryIds)
    {
        var result = PluginSdkVerifier.Verify(secret, body, signature, deliveryId, deliveredAt, now, seenDeliveryIds);
        return new SdkVerification(result.Accepted, result.Reason);
    }

    public static bool TryNormalizeIdempotencyKey(string? key, out string normalized) =>
        PluginSdkIdempotency.TryNormalize(key, out normalized);

    public static string Scrub(string? value) => PluginSdkRedaction.Scrub(value);

    public static HttpRequestMessage SandboxSend(Guid workspaceId, Guid channelId, string body, string idempotencyKey) =>
        PluginSdkClient.SandboxSend(workspaceId, channelId, body, idempotencyKey);
}
