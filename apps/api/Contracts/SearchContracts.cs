public sealed record SearchMessageHitResponse(
    Guid MessageId,
    Guid ChannelId,
    string ChannelName,
    string ChannelType,
    long Sequence,
    Guid AuthorUserId,
    string AuthorDisplayName,
    string BodyPreview,
    DateTimeOffset CreatedAt,
    double Rank,
    string Kind = "message");

public sealed record SearchChannelHitResponse(
    string Kind,
    Guid ChannelId,
    string ChannelName,
    string ChannelType,
    double Rank);

public sealed record SearchPersonHitResponse(
    string Kind,
    Guid UserId,
    string DisplayName,
    double Rank);

public sealed record SearchAttachmentHitResponse(
    string Kind,
    Guid AttachmentId,
    string FileName,
    Guid MessageId,
    Guid ChannelId,
    string ChannelName,
    string ChannelType,
    long Sequence,
    double Rank);

public sealed record SearchMessagesResponse(
    string Query,
    int Limit,
    SearchMessageHitResponse[] Items,
    int Total = 0,
    string? Cursor = null,
    SearchChannelHitResponse[]? Channels = null,
    SearchPersonHitResponse[]? People = null,
    SearchAttachmentHitResponse[]? Attachments = null);
