using System.Net;
using System.Net.Sockets;
using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>Instance singleton for process kill switches, knobs and VAPID (B-187). No tenant_id.</summary>
public sealed class ProcessSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool AiEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public bool MessageRetentionEnabled { get; set; }
    public bool PushEnabled { get; set; }
    public bool LinkPreviewEnabled { get; set; } = true;

    public string OpenRouterBaseUrl { get; set; } = ProcessSettingsDefaults.OpenRouterBaseUrl;

    public int RetentionDefaultDays { get; set; } = ProcessSettingsDefaults.RetentionDefaultDays;
    public int RetentionBatchSize { get; set; } = ProcessSettingsDefaults.RetentionBatchSize;
    public int RetentionIntervalMinutes { get; set; } = ProcessSettingsDefaults.RetentionIntervalMinutes;
    public int LinkPreviewTimeoutMs { get; set; } = ProcessSettingsDefaults.LinkPreviewTimeoutMs;

    public string VapidPublicKey { get; set; } = string.Empty;
    public EncryptedSecretEnvelope VapidPrivateKey { get; set; } = new();
    public string VapidSubject { get; set; } = ProcessSettingsDefaults.VapidSubject;

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class ProcessSettingsDefaults
{
    public const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";
    public const int RetentionDefaultDays = 90;
    public const int RetentionBatchSize = 500;
    public const int RetentionIntervalMinutes = 60;
    public const int LinkPreviewTimeoutMs = 8000;
    public const string VapidSubject = "mailto:ops@localhost";
    public const string SourceDatabase = "database";
    public const string SourceDefault = "default";

    /// <summary>32-byte UTF-8 "VibeChatLabKeyringDemoOnly!!!!!!" — lab only, never production.</summary>
    public const string LabDemoKeyBase64 = "VmliZUNoYXRMYWJLZXlyaW5nRGVtb09ubHkhISEhISE=";
}

/// <summary>AAD scope for instance-level envelopes (VAPID).</summary>
public static class ProcessSecretScope
{
    public static readonly TenantId TenantId = new(Guid.Empty);
    public const string EntityId = "process";
}

/// <summary>https + public host; no localhost / private IP (B-187).</summary>
public static class OpenRouterBaseUrlPolicies
{
    public static bool IsValid(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host == "127.0.0.1"
            || uri.Host == "::1"
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var address) && IsPrivateOrLoopback(address))
        {
            return false;
        }

        return true;
    }

    public static string Normalize(string url) => url.Trim().TrimEnd('/');

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Teredo)
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsPrivateOrLoopback(address.MapToIPv4());
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes[0] == 0 || bytes[0] == 10 || bytes[0] == 127)
        {
            return true;
        }

        if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
        {
            return true;
        }

        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        return bytes[0] == 192 && bytes[1] == 168;
    }
}
