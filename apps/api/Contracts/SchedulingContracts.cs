public sealed record CreateScheduledMessageRequest(
    string IdempotencyKey,
    string Body,
    string SendAtLocal,
    string TimeZone,
    Guid? ReplyToMessageId = null,
    Guid? ThreadId = null);

public sealed record UpdateScheduledMessageRequest(
    string? Body = null,
    string? SendAtLocal = null,
    string? TimeZone = null);

public sealed record CreateReminderRequest(
    string IdempotencyKey,
    string TargetKind,
    string RemindAtLocal,
    string TimeZone,
    string? Note = null,
    Guid? MessageId = null,
    Guid? ThreadId = null,
    Guid? ChannelId = null);

public sealed record UpdateReminderRequest(
    string? Note = null,
    string? RemindAtLocal = null,
    string? TimeZone = null);

public sealed record ScheduleItemResponse(
    string Kind,
    Guid Id,
    string Status,
    DateTimeOffset DueAtUtc,
    string TimeZone,
    string? Body,
    string? Note,
    string? TargetKind,
    Guid? ChannelId,
    string? ChannelName,
    Guid? MessageId,
    Guid? ThreadId,
    Guid? SentMessageId,
    string? FailureCode,
    bool CanOpen,
    DateTimeOffset CreatedAt);

public sealed record SchedulePageResponse(
    ScheduleItemResponse[] Items,
    string? NextCursor);
