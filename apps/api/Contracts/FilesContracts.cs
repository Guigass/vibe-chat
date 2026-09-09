public sealed record CreateAttachmentUploadRequest(
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Kind = null,
    int? DurationMs = null,
    int[]? Waveform = null,
    int? Width = null,
    int? Height = null);
public sealed record AttachmentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    string Kind = "File",
    int? DurationMs = null,
    int[]? Waveform = null,
    string? ThumbnailStatus = null,
    int? Width = null,
    int? Height = null,
    int? PageCount = null);
public sealed record AttachmentUploadResponse(
    Guid AttachmentId,
    string UploadUrl,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    long MaxSizeBytes,
    string FileName,
    string ContentType);
public sealed record AttachmentDownloadResponse(
    Guid AttachmentId,
    string DownloadUrl,
    DateTimeOffset ExpiresAt,
    string FileName,
    string ContentType,
    long SizeBytes);
public sealed record AttachmentThumbnailResponse(
    Guid AttachmentId,
    string DownloadUrl,
    DateTimeOffset ExpiresAt,
    string ContentType,
    int? Width,
    int? Height,
    int? PageCount);
