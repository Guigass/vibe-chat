using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// Explicit history window for one plugin and one channel (B-111).
/// Channel membership of the bot does not imply this grant.
/// </summary>
public sealed class PluginHistoryGrant
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid InstalledPluginId { get; set; }
    public ChannelId ChannelId { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset WindowEnd { get; set; }
    public UserId GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Buttons stored as data on a bot message. The browser never loads plugin code.
/// </summary>
public sealed class PluginInteractiveOffer
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid InstalledPluginId { get; set; }
    public ChannelId ChannelId { get; set; }
    public MessageId MessageId { get; set; }
    public string PluginSlug { get; set; } = string.Empty;
    public string PluginName { get; set; } = string.Empty;
    public string[] Actions { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One interactive click. The same idempotency key replays this row.
/// </summary>
public sealed class PluginActionReceipt
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid InstalledPluginId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public MessageId MessageId { get; set; }
    public ChannelId ChannelId { get; set; }
    public UserId ActorUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>UTC-day counter for one plugin capability.</summary>
public sealed class PluginQuotaCounter
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid InstalledPluginId { get; set; }
    public string Capability { get; set; } = string.Empty;
    public DateOnly Day { get; set; }
    public int Count { get; set; }
}

public static class PluginActionStatus
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Failed = "Failed";
}

public static class PluginGovernedRules
{
    public static readonly TimeSpan MaxHistoryWindow = TimeSpan.FromDays(7);
    public const int MaxHistoryMessages = 50;
    public const int DefaultActionTtlSeconds = 86_400;
    public const int MinActionTtlSeconds = 60;
    public const int MaxActionTtlSeconds = 604_800;
    public const int MaxActions = 4;
    public const string InvalidWindow = "InvalidHistoryWindow";

    public static int DailyLimit(string capability) => capability switch
    {
        PluginManifestRules.CapabilityMessagesInteract => 200,
        PluginManifestRules.CapabilityHistoryRead => 100,
        PluginManifestRules.CapabilityAttachmentsSend => 20,
        _ => 0
    };

    public static bool TryValidateWindow(DateTimeOffset start, DateTimeOffset end, out string error)
    {
        error = InvalidWindow;
        if (end <= start || end - start > MaxHistoryWindow)
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool WindowCovers(
        DateTimeOffset grantStart,
        DateTimeOffset grantEnd,
        DateTimeOffset from,
        DateTimeOffset to) =>
        to >= from && from >= grantStart && to <= grantEnd;

    public static bool IsBlockedAttachmentName(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty).Trim().ToLowerInvariant();
        return ext is ".exe" or ".dll" or ".js" or ".mjs" or ".html" or ".htm";
    }

    public static bool IsBlockedAttachmentType(string? contentType)
    {
        var type = (contentType ?? string.Empty).Trim();
        return type.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || type.Equals("text/javascript", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/x-msdownload", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/x-msdos-program", StringComparison.OrdinalIgnoreCase);
    }
}
