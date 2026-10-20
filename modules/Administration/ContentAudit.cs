using System.Text.Json;
using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>
/// Tenant switch for privileged plaintext (B-169). Missing row means enabled,
/// so existing tenants keep auditor and export access until an admin opts out.
/// </summary>
public sealed class ContentAuditSettings
{
    public TenantId TenantId { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class ContentAuditRules
{
    public const string BlocksE2ee = "ContentAuditBlocksE2ee";
    public const string BlockedByLegalHold = "ContentAuditBlockedByLegalHold";
    public const string ConfirmationRequired = "ContentAuditConfirmationRequired";
    public const string E2eeUnavailable = "ConfidentialE2eeUnavailable";
    public const string WireType = "ConfidentialE2EE";

    public static bool IsEnabled(bool? stored) => stored ?? true;

    public static bool IsConfidentialE2eeType(string? type) =>
        string.Equals(type?.Trim(), WireType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Audit on blocks the channel. Audit off still refuses persistence: the
    /// crypto protocol is B-064, and a plaintext row labeled confidential
    /// would leak. B-064 replaces <see cref="E2eeUnavailable"/>.
    /// </summary>
    public static string E2eeCreationError(bool contentAuditEnabled) =>
        contentAuditEnabled ? BlocksE2ee : E2eeUnavailable;

    public static string? DisableError(bool currentEnabled, bool requestedEnabled, bool confirmDisable, bool activeBodyHold)
    {
        if (!currentEnabled || requestedEnabled)
        {
            return null;
        }

        if (!confirmDisable)
        {
            return ConfirmationRequired;
        }

        return activeBodyHold ? BlockedByLegalHold : null;
    }

    public static string DeleteMetadataJson(
        Guid channelId,
        Guid? threadId,
        long sequence,
        Guid authorId,
        DateTimeOffset? deletedAt,
        string? body,
        bool includeBody)
    {
        if (includeBody)
        {
            return JsonSerializer.Serialize(new
            {
                channelId,
                threadId,
                sequence,
                authorId,
                deletedAt,
                body
            });
        }

        return JsonSerializer.Serialize(new
        {
            channelId,
            threadId,
            sequence,
            authorId,
            deletedAt
        });
    }
}
