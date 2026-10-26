using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>B-172. Off by default. Chat packages complement infra dumps and the compliance export.</summary>
public sealed class ChatBackupOptions
{
    public const string SectionName = "Features:ChatBackup";

    public bool Enabled { get; set; }

    /// <summary>Google Drive stays off unless an operator opts in. The lab uses <see cref="DriveEndpoint"/>.</summary>
    public bool DriveEnabled { get; set; }

    /// <summary>Local substitute for Drive. Empty never calls Google.</summary>
    public string DriveEndpoint { get; set; } = string.Empty;
}

public static class ChatBackupKinds
{
    public const string Sftp = "sftp";
    public const string Ftp = "ftp";
    public const string Smb = "smb";
    public const string S3 = "s3";
    public const string Drive = "drive";
    public const string WebDav = "webdav";

    public static readonly string[] All = [Sftp, Ftp, Smb, S3, Drive, WebDav];

    public static bool Known(string? kind) =>
        kind is not null && All.Contains(kind, StringComparer.Ordinal);

    public static int DefaultPort(string kind) => kind switch
    {
        Sftp => 22,
        Ftp => 21,
        Smb => 445,
        S3 => 9000,
        WebDav => 443,
        Drive => 443,
        _ => 0
    };
}

public static class ChatBackupRunStatuses
{
    public const string Success = "success";
    public const string Failed = "failed";
}

public static class ChatBackupCircuits
{
    public const string Closed = "closed";
    public const string Open = "open";
}

public static class ChatBackupErrors
{
    public const string Disabled = "ChatBackupDisabled";
    public const string NotFound = "ChatBackupNotFound";
    public const string Invalid = "ChatBackupInvalid";
    public const string DriveDisabled = "ChatBackupDriveDisabled";
    public const string EncryptionUnavailable = "ChatBackupEncryptionUnavailable";
    public const string Limit = "ChatBackupLimit";
    public const string InUse = "ChatBackupDestinationInUse";
    public const string IdempotencyRequired = "ChatBackupIdempotencyRequired";
    public const string IdempotencyConflict = "ChatBackupIdempotencyConflict";
    public const string ConnectionFailed = "ChatBackupConnectionFailed";
    public const string DestinationFailed = "ChatBackupDestinationFailed";
    public const string CircuitOpen = "ChatBackupCircuitOpen";
    public const string TooLarge = "ChatBackupTooLarge";
    public const string ChecksumMismatch = "ChatBackupChecksumMismatch";
    public const string UnsafePath = "ChatBackupUnsafePath";
    public const string ConfirmationRequired = "ChatBackupConfirmationRequired";
    public const string CrossTenant = "ChatBackupCrossTenant";
    public const string PackageInvalid = "ChatBackupPackageInvalid";

    public static int Status(string error) => error switch
    {
        Disabled or NotFound => 404,
        CrossTenant => 403,
        IdempotencyConflict or InUse or CircuitOpen => 409,
        EncryptionUnavailable => 503,
        _ => 400
    };
}

public static class ChatBackupLimits
{
    public const string Format = "vibechat.chat.backup.v1";
    public const string CredentialKind = "chat.backup.destination";
    public const string JobRole = "chatbackup";
    public const int MaxNameLength = 80;
    public const int MaxHostLength = 253;
    public const int MaxPathLength = 240;
    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxDestinations = 20;
    public const int MaxJobs = 20;
    public const int MaxRunsListed = 20;
    public const int MinIntervalMinutes = 5;
    public const int MaxIntervalMinutes = 10080;
    public const int MaxRetentionCount = 100;
    public const int MaxRetentionDays = 3650;
    public const int MaxPackageBytes = 8 * 1024 * 1024;
    public const int MaxMessages = 5000;
    public const int MaxBodyLength = 8000;
    public const int CircuitAfter = 3;
    public const int ClaimLeaseMinutes = 15;
    public static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(8);
}

public sealed class ChatBackupDestination
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string RemotePath { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public bool UseSsl { get; set; }
    public byte[]? SecretCiphertext { get; set; }
    public byte[]? SecretNonce { get; set; }
    public byte[]? SecretTag { get; set; }
    public int? SecretKeyVersion { get; set; }
    public short SecretFormatVersion { get; set; }
    public string SecretMask { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? CircuitOpenUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChatBackupJob
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid DestinationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Paused { get; set; }
    public bool IncludeAttachments { get; set; }
    public int IntervalMinutes { get; set; }
    public string Cron { get; set; } = string.Empty;
    public int RetentionCount { get; set; } = 7;
    public int RetentionDays { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChatBackupRun
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid? JobId { get; set; }
    public Guid? DestinationId { get; set; }
    public string Status { get; set; } = ChatBackupRunStatuses.Failed;
    public long SizeBytes { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string RemoteKey { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public static class ChatBackupRules
{
    public static bool IsOpen(DateTimeOffset? openUntil, DateTimeOffset now) =>
        openUntil is not null && openUntil > now;

    public static string Circuit(DateTimeOffset? openUntil, DateTimeOffset now) =>
        IsOpen(openUntil, now) ? ChatBackupCircuits.Open : ChatBackupCircuits.Closed;

    public static (int Failures, DateTimeOffset? OpenUntil) RecordFailure(int failures, DateTimeOffset now)
    {
        var next = failures + 1;
        return next >= ChatBackupLimits.CircuitAfter
            ? (next, now.AddSeconds(60))
            : (next, null);
    }

    public static string? ValidateName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is 0 or > ChatBackupLimits.MaxNameLength)
        {
            return ChatBackupErrors.Invalid;
        }

        return value.Any(char.IsControl) ? ChatBackupErrors.Invalid : null;
    }

    public static string? ValidateHost(string? host)
    {
        var value = (host ?? string.Empty).Trim();
        if (value.Length is 0 or > ChatBackupLimits.MaxHostLength || value.Any(char.IsControl) || value.Contains(' ', StringComparison.Ordinal))
        {
            return ChatBackupErrors.Invalid;
        }

        return string.Equals(value, "169.254.169.254", StringComparison.OrdinalIgnoreCase)
            ? ChatBackupErrors.Invalid
            : null;
    }

    public static string? ValidateRemotePath(string? path)
    {
        var value = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (value.Length > ChatBackupLimits.MaxPathLength || value.Contains("..", StringComparison.Ordinal) || value.Contains('\0'))
        {
            return ChatBackupErrors.UnsafePath;
        }

        return null;
    }

    public static string NormalizePath(string? path)
    {
        var value = (path ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        return value;
    }

    public static string RemoteObjectKey(string? prefix, Guid runId)
    {
        var folder = NormalizePath(prefix);
        var name = runId.ToString("N") + ".zip";
        return folder.Length == 0 ? name : folder + "/" + name;
    }

    public static bool ScheduleValid(int intervalMinutes, string? cron)
    {
        var expression = (cron ?? string.Empty).Trim();
        if (intervalMinutes == 0)
        {
            return expression.Length > 0 && NextCron(expression, DateTimeOffset.UnixEpoch) is not null;
        }

        return expression.Length == 0
            && intervalMinutes is >= ChatBackupLimits.MinIntervalMinutes and <= ChatBackupLimits.MaxIntervalMinutes;
    }

    public static bool RetentionValid(int count, int days) =>
        count is >= 1 and <= ChatBackupLimits.MaxRetentionCount
        && days is >= 0 and <= ChatBackupLimits.MaxRetentionDays;

    public static DateTimeOffset NextRun(int intervalMinutes, string? cron, DateTimeOffset after)
    {
        if (intervalMinutes > 0)
        {
            return after.AddMinutes(intervalMinutes);
        }

        return NextCron((cron ?? string.Empty).Trim(), after) ?? after.AddHours(1);
    }

    public static DateTimeOffset? NextCron(string cron, DateTimeOffset after)
    {
        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts.Any(part => part is not "*" && !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return null;
        }

        var cursor = new DateTimeOffset(after.UtcDateTime.AddMinutes(1).Ticks - (after.UtcDateTime.Ticks % TimeSpan.TicksPerMinute), TimeSpan.Zero);
        for (var i = 0; i < 366 * 24 * 60; i++)
        {
            if (Matches(parts, cursor))
            {
                return cursor;
            }

            cursor = cursor.AddMinutes(1);
        }

        return null;
    }

    public static IReadOnlyList<Guid> ExpiredRuns(
        IReadOnlyList<(Guid Id, DateTimeOffset FinishedAt)> runs,
        int retentionCount,
        int retentionDays,
        DateTimeOffset now)
    {
        var ordered = runs.OrderByDescending(x => x.FinishedAt).ToArray();
        var expired = new List<Guid>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var aged = retentionDays > 0 && ordered[i].FinishedAt < now.AddDays(-retentionDays);
            if (i >= retentionCount || aged)
            {
                expired.Add(ordered[i].Id);
            }
        }

        return expired;
    }

    public static string Sanitize(string? message, string secret)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return ChatBackupErrors.DestinationFailed;
        }

        if (!string.IsNullOrEmpty(secret) && message.Contains(secret, StringComparison.Ordinal))
        {
            return ChatBackupErrors.DestinationFailed;
        }

        return message.Length > 80 ? ChatBackupErrors.DestinationFailed : message;
    }

    public static string RequestHash(params string[] parts)
    {
        var raw = string.Join('\n', parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool Matches(string[] parts, DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        return Field(parts[0], utc.Minute)
            && Field(parts[1], utc.Hour)
            && Field(parts[2], utc.Day)
            && Field(parts[3], utc.Month)
            && Field(parts[4], (int)utc.DayOfWeek);
    }

    private static bool Field(string part, int value) =>
        part == "*" || (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed == value);
}
