namespace VibeChat.Administration;

/// <summary>Outbound webhooks are planned (B-048); admin surface reserved without delivery.</summary>
public static class WebhooksSettingsStatus
{
    public const string Planned = "planned";
    public const string Message = "Outbound webhooks (B-048) — admin-only; delivery not implemented yet.";
}
