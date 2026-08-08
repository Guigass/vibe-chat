using VibeChat.SharedKernel;

namespace VibeChat.Files;

public enum AttachmentStatus
{
    PendingUpload = 0,
    Ready = 1,
    Failed = 2
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

public static class AttachmentPolicies
{
    public const long DefaultMaxSizeBytes = 10 * 1024 * 1024;
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
}
