using VibeChat.SharedKernel;

namespace VibeChat.Files;

public enum AttachmentStatus
{
    PendingUpload = 0,
    Ready = 1,
    Failed = 2
}

public enum AttachmentKind
{
    File = 0,
    Audio = 1
}

public sealed class Attachment
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public ChannelId ChannelId { get; set; }
    public MessageId? MessageId { get; set; }
    public UserId UploadedBy { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? ChecksumSha256 { get; set; }
    public AttachmentStatus Status { get; set; } = AttachmentStatus.PendingUpload;
    public AttachmentKind Kind { get; set; } = AttachmentKind.File;
    public int? DurationMs { get; set; }
    public int[]? Waveform { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
}

public sealed record PresignedUpload(Uri Url, DateTimeOffset ExpiresAt, IReadOnlyDictionary<string, string> RequiredHeaders);

public sealed record PresignedDownload(Uri Url, DateTimeOffset ExpiresAt);

public sealed record ObjectStat(long SizeBytes, string ContentType, string? ETag);

public interface IObjectStorage
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
    Task<PresignedUpload> CreateUploadUrlAsync(string storageKey, string contentType, TimeSpan ttl, CancellationToken cancellationToken);
    Task<PresignedDownload> CreateDownloadUrlAsync(string storageKey, string fileName, TimeSpan ttl, CancellationToken cancellationToken);
    Task<ObjectStat?> StatObjectAsync(string storageKey, CancellationToken cancellationToken);
}

/// <summary>Tenant-level file/attachment limits (ADR-020). Env values act as security ceiling.</summary>
public sealed class TenantFilesSettings
{
    public TenantId TenantId { get; set; }
    public long MaxSizeBytes { get; set; } = AttachmentPolicies.DefaultMaxSizeBytes;
    public int MaxAttachmentsPerMessage { get; set; } = AttachmentPolicies.DefaultMaxAttachmentsPerMessage;
    public int PresignUploadTtlSeconds { get; set; } = AttachmentPolicies.DefaultUploadTtlSeconds;
    public int PresignDownloadTtlSeconds { get; set; } = AttachmentPolicies.DefaultDownloadTtlSeconds;
    public string[] AllowedContentTypes { get; set; } = AttachmentPolicies.DefaultAllowedContentTypes.ToArray();
    public long AudioMaxSizeBytes { get; set; } = AttachmentPolicies.DefaultAudioMaxSizeBytes;
    public int AudioMaxDurationMs { get; set; } = AttachmentPolicies.DefaultAudioMaxDurationMs;
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class AttachmentPolicies
{
    public const long DefaultMaxSizeBytes = 10 * 1024 * 1024;
    public const long DefaultAudioMaxSizeBytes = 10 * 1024 * 1024;
    public const int DefaultAudioMaxDurationMs = 300_000;
    public const int MaxWaveformPoints = 100;
    public const int DefaultMaxAttachmentsPerMessage = 10;
    public const int DefaultUploadTtlSeconds = 900;
    public const int DefaultDownloadTtlSeconds = 300;
    public const int MaxFileNameLength = 180;

    public static readonly HashSet<string> DefaultAllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/gif",
        "application/pdf",
        "text/plain"
    };

    public static readonly HashSet<string> DefaultAllowedAudioContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/webm",
        "audio/ogg",
        "audio/mp4",
        "audio/webm;codecs=opus",
        "audio/ogg;codecs=opus"
    };

    public static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            return "file";
        }

        var cleaned = new string(name.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_').ToArray());

        cleaned = cleaned.Trim('.', ' ', '_');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "file";
        }

        return cleaned.Length <= MaxFileNameLength ? cleaned : cleaned[..MaxFileNameLength];
    }

    public static string BuildStorageKey(TenantId tenantId, ChannelId channelId, Guid attachmentId, string safeFileName) =>
        $"tenants/{tenantId.Value:N}/channels/{channelId.Value:N}/{attachmentId:N}/{safeFileName}";

    public static bool IsAllowedContentType(string contentType, IEnumerable<string>? allowed) =>
        (allowed ?? DefaultAllowedContentTypes).Contains(contentType.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsWithinAttachmentCount(int count, int maxAttachments = DefaultMaxAttachmentsPerMessage) =>
        count >= 0 && count <= maxAttachments;

    public static bool IsAllowedAudioContentType(string contentType) =>
        DefaultAllowedAudioContentTypes.Contains(contentType.Trim(), StringComparer.OrdinalIgnoreCase)
        || contentType.Trim().StartsWith("audio/webm", StringComparison.OrdinalIgnoreCase)
        || contentType.Trim().StartsWith("audio/ogg", StringComparison.OrdinalIgnoreCase)
        || contentType.Trim().StartsWith("audio/mp4", StringComparison.OrdinalIgnoreCase);

    public static bool IsValidWaveform(int[]? waveform)
    {
        if (waveform is null || waveform.Length == 0)
        {
            return true;
        }

        if (waveform.Length > MaxWaveformPoints)
        {
            return false;
        }

        return waveform.All(v => v is >= 0 and <= 100);
    }

    public static int[] NormalizeWaveform(int[]? waveform)
    {
        if (waveform is null || waveform.Length == 0)
        {
            return [];
        }

        var capped = waveform.Length > MaxWaveformPoints
            ? DownsampleWaveform(waveform, MaxWaveformPoints)
            : waveform;

        return capped.Select(v => Math.Clamp(v, 0, 100)).ToArray();
    }

    public static int[] DownsampleWaveform(int[] samples, int targetPoints)
    {
        if (samples.Length <= targetPoints)
        {
            return samples;
        }

        var result = new int[targetPoints];
        var bucketSize = (double)samples.Length / targetPoints;
        for (var i = 0; i < targetPoints; i++)
        {
            var start = (int)Math.Floor(i * bucketSize);
            var end = Math.Min(samples.Length, (int)Math.Floor((i + 1) * bucketSize));
            if (end <= start)
            {
                result[i] = samples[Math.Min(start, samples.Length - 1)];
                continue;
            }

            var slice = samples[start..end];
            result[i] = (int)Math.Round(slice.Average());
        }

        return result;
    }
}
