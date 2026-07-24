namespace VibeChat.Administration;

/// <summary>Outbound webhook status for admin settings (B-048).</summary>
public static class WebhooksSettingsStatus
{
    public const string Unconfigured = "unconfigured";
    public const string Disabled = "disabled";
    public const string Active = "active";

    public const string UnconfiguredMessage =
        "Configure URL + signing secret to enable outbound MessageCreated delivery.";
    public const string DisabledMessage = "Webhook endpoint saved but delivery is disabled.";
    public const string ActiveMessage = "Outbound MessageCreated events are delivered with HMAC-SHA256.";

    public static string Resolve(bool enabled, bool urlConfigured, bool secretConfigured)
    {
        if (!urlConfigured || !secretConfigured)
        {
            return Unconfigured;
        }

        return enabled ? Active : Disabled;
    }

    public static string MessageFor(string status) => status switch
    {
        Active => ActiveMessage,
        Disabled => DisabledMessage,
        _ => UnconfiguredMessage
    };
}
