using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Files;

/// <summary>B-145. Off by default. Bytes stay in the existing S3-compatible store.</summary>
public sealed class StorageLifecycleOptions
{
    public const string SectionName = "Features:StorageLifecycle";

    public bool Enabled { get; set; }

    public bool CdnEnabled { get; set; }

    /// <summary>HMAC for CDN tokens. Empty, short or CHANGE_ME keeps the CDN off.</summary>
    public string CdnKey { get; set; } = string.Empty;

    public int CdnTtlSeconds { get; set; } = 120;

    public int GraceHours { get; set; } = 24;
}

public static class StorageObjectKinds
{
    public const string Attachment = "attachment";
    public const string Preview = "preview";
    public const string Export = "export";
    public const string Recording = "recording";

    public static readonly string[] All = [Attachment, Preview, Export, Recording];

    public static bool IsKnown(string? kind) =>
        kind is not null && All.Contains(kind, StringComparer.Ordinal);
}

public static class StorageObjectStates
{
    public const string Hot = "hot";
    public const string Archived = "archived";
    public const string PurgePending = "purge-pending";
    public const string Purged = "purged";
}

public static class StorageLifecycleErrors
{
    public const string Disabled = "StorageLifecycleDisabled";
    public const string CdnDisabled = "StorageCdnDisabled";
    public const string CdnExpired = "StorageCdnExpired";
    public const string CdnUnavailable = "StorageCdnUnavailable";
    public const string HardLimit = "StorageQuotaHardLimit";
    public const string Hold = "StorageHoldActive";
    public const string Referenced = "StorageReferenced";
    public const string Grace = "StorageGracePending";
    public const string State = "StorageLifecycleState";
    public const string AlreadyPurged = "StorageAlreadyPurged";
    public const string NotFound = "StorageNotFound";
    public const string KindUnknown = "StorageKindUnknown";
    public const string InvalidLimit = "StorageQuotaInvalid";
    public const string IdempotencyRequired = "StorageIdempotencyRequired";
    public const string IdempotencyConflict = "StorageIdempotencyConflict";

    public static int Status(string error) => error switch
    {
        Disabled or CdnDisabled or NotFound or CdnUnavailable => 404,
        CdnExpired => 410,
        HardLimit or Hold or Referenced or Grace or State or AlreadyPurged or IdempotencyConflict => 409,
        _ => 400
    };
}

public static class StorageLifecycleLimits
{
    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxStorageKeyLength = 512;
    public const int MinCdnKeyLength = 16;
}

public sealed class StorageLedger
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long ReservedBytes { get; set; }
    public long UsedBytes { get; set; }
    public long PreviousUsedBytes { get; set; }
    public long SoftLimit { get; set; }
    public long HardLimit { get; set; }
}

public sealed class StorageObjectRecord
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string State { get; set; } = StorageObjectStates.Hot;
    public long Bytes { get; set; }
    public int Version { get; set; } = 1;
    public bool Referenced { get; set; }
    public bool Clean { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public Guid? AttachmentId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid AuthorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PurgeAfter { get; set; }
    public DateTimeOffset? PurgedAt { get; set; }
}

public sealed class StorageTombstone
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid ObjectId { get; set; }
    public DateTimeOffset PurgedAt { get; set; }
}

public sealed class StorageLifecycleJob
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ResultJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public readonly record struct StorageMeasure(bool Referenced, string State, long Bytes);

public readonly record struct CdnTokenRead(bool Ok, bool Expired, Guid TenantId, Guid ObjectId, DateTimeOffset ExpiresAt);

/// <summary>Pure lifecycle decisions. Persistence applies them under RLS.</summary>
public static class StorageLifecycleRules
{
    public const string ChangedEvent = "storage.lifecycle.changed";

    public static bool CdnKeyUsable(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Trim().Length >= StorageLifecycleLimits.MinCdnKeyLength
        && !key.Contains("CHANGE_ME", StringComparison.Ordinal);

    public static bool TryLimits(long softLimit, long hardLimit) =>
        softLimit >= 0 && hardLimit >= 1 && softLimit <= hardLimit;

    public static bool TryReserve(long used, long reserved, long hard, long delta) =>
        delta > 0 && hard >= 1 && used >= 0 && reserved >= 0 && used + reserved + delta <= hard;

    public static string Trend(long used, long previous) =>
        used > previous ? "up" : used < previous ? "down" : "flat";

    public static string Action(long used, long soft, long hard) =>
        hard >= 1 && used >= hard ? "blocked" : soft >= 0 && used >= soft && soft > 0 ? "warning" : "none";

    public static bool CanPurge(bool referenced, bool held, string state, DateTimeOffset? purgeAfter, DateTimeOffset now) =>
        !referenced
        && !held
        && state == StorageObjectStates.PurgePending
        && purgeAfter is not null
        && purgeAfter <= now;

    public static bool IsGcCandidate(bool referenced, bool held, string state, DateTimeOffset? purgeAfter, DateTimeOffset now) =>
        !referenced
        && !held
        && state is StorageObjectStates.Hot or StorageObjectStates.Archived or StorageObjectStates.PurgePending
        && purgeAfter is not null
        && purgeAfter <= now;

    public static (long Used, long Reserved) Measure(IEnumerable<StorageMeasure> rows)
    {
        long used = 0;
        long reserved = 0;
        foreach (var row in rows)
        {
            if (row.State == StorageObjectStates.Purged || row.Bytes <= 0)
            {
                continue;
            }

            if (row.Referenced)
            {
                used += row.Bytes;
            }
            else
            {
                reserved += row.Bytes;
            }
        }

        return (used, reserved);
    }

    public static bool RestoreBlocked(bool tombstoneExists) => tombstoneExists;

    public static string IssueCdnToken(Guid tenantId, Guid objectId, DateTimeOffset expiresAt, string key)
    {
        var payload = $"{tenantId:N}.{objectId:N}.{expiresAt.ToUnixTimeSeconds()}";
        var sig = Sign(payload, key);
        return Base64UrlEncode(Encoding.UTF8.GetBytes($"{payload}.{sig}"));
    }

    public static CdnTokenRead ReadCdnToken(string? token, string key, DateTimeOffset now)
    {
        if (!CdnKeyUsable(key) || string.IsNullOrWhiteSpace(token))
        {
            return default;
        }

        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(Base64UrlDecode(token.Trim()));
        }
        catch (FormatException)
        {
            return default;
        }

        var parts = raw.Split('.');
        if (parts.Length != 4
            || !Guid.TryParseExact(parts[0], "N", out var tenantId)
            || !Guid.TryParseExact(parts[1], "N", out var objectId)
            || !long.TryParse(parts[2], out var unix))
        {
            return default;
        }

        var payload = $"{parts[0]}.{parts[1]}.{parts[2]}";
        var expected = Sign(payload, key);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[3])))
        {
            return default;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(unix);
        if (expires <= now)
        {
            return new CdnTokenRead(false, true, tenantId, objectId, expires);
        }

        return new CdnTokenRead(true, false, tenantId, objectId, expires);
    }

    public static bool EnumeratesKey(string url, string storageKey) =>
        !string.IsNullOrEmpty(storageKey) && url.Contains(storageKey, StringComparison.Ordinal);

    private static string Sign(string payload, string key)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

public sealed record StorageCommandResult(int Status, object? Body, string? Error)
{
    public static StorageCommandResult Ok(object body, int status = 200) => new(status, body, null);

    public static StorageCommandResult Fail(string error) => new(StorageLifecycleErrors.Status(error), null, error);
}
