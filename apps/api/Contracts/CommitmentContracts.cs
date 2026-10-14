public sealed record CreateCommitmentRequest(
    string IdempotencyKey,
    Guid MessageId,
    string Kind,
    string Title,
    string? Description,
    Guid? OwnerUserId,
    DateTimeOffset? DueAt,
    string? Visibility);

public sealed record PatchCommitmentRequest(
    string? Title,
    string? Description,
    Guid? OwnerUserId,
    bool ClearOwner,
    DateTimeOffset? DueAt,
    bool ClearDue,
    string? Visibility);

public sealed record CommitmentResponse(
    Guid Id,
    string Kind,
    string Title,
    string? Description,
    Guid? OwnerUserId,
    string? OwnerName,
    DateTimeOffset? DueAt,
    string State,
    Guid ChannelId,
    string ChannelName,
    Guid SourceMessageId,
    Guid? SourceThreadId,
    long SourceSequence,
    bool SourceRedacted,
    string Visibility,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    string ExcerptHash);

public sealed record CommitmentPageResponse(
    CommitmentResponse[] Items,
    string? NextCursor);

public sealed record CommitmentChangeResponse(
    string Action,
    Guid ActorUserId,
    string ActorName,
    DateTimeOffset At);

public sealed record CommitmentHistoryResponse(CommitmentChangeResponse[] Items);
