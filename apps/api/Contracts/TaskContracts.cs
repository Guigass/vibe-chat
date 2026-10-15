public sealed record TaskChecklistInput(string Text, bool Done);

public sealed record CreateTaskRequest(
    string IdempotencyKey,
    Guid MessageId,
    string Title,
    string? Description,
    string? Priority,
    Guid[]? AssigneeUserIds,
    DateTimeOffset? DueAt,
    string? Visibility,
    TaskChecklistInput[]? Checklist);

public sealed record PatchTaskRequest(
    int Version,
    string? Title,
    string? Description,
    string? Priority,
    string? State,
    Guid[]? AssigneeUserIds,
    bool ReplaceAssignees,
    DateTimeOffset? DueAt,
    bool ClearDue,
    string? Visibility,
    TaskChecklistInput[]? Checklist,
    bool ReplaceChecklist);

public sealed record TaskVersionRequest(int Version);

public sealed record TaskAssigneeResponse(Guid UserId, string DisplayName);

public sealed record TaskChecklistResponse(Guid Id, int Position, string Text, bool Done);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    string Priority,
    string State,
    int Version,
    DateTimeOffset? DueAt,
    bool Overdue,
    TaskAssigneeResponse[] Assignees,
    TaskChecklistResponse[] Checklist,
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
    DateTimeOffset? CompletedAt);

public sealed record TaskPageResponse(TaskResponse[] Items, string? NextCursor);
