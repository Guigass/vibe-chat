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
    double Rank);
public sealed record SearchMessagesResponse(
    string Query,
    int Limit,
    SearchMessageHitResponse[] Items,
    int Total = 0,
    string? Cursor = null);
