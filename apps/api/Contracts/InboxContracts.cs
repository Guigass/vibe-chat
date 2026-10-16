public sealed record InboxNavigationResponse(Guid ChannelId, Guid? MessageId, Guid? ThreadId, long? Sequence, Guid? TopicId = null);

public sealed record InboxItemResponse(
    string ItemKey,
    string Kind,
    int Priority,
    string Reason,
    Guid ChannelId,
    string ChannelName,
    Guid? MessageId,
    Guid? ThreadId,
    long? Sequence,
    string BodyPreview,
    bool MessageRemoved,
    DateTimeOffset SortAt,
    DateTimeOffset? SnoozedUntil,
    InboxNavigationResponse Navigation,
    Guid? TopicId = null);

public sealed record InboxCountsResponse(int Announcement, int Mention, int Direct, int Thread, int Saved, int Topic, int Commitment, int Tasks, int Total);

public sealed record InboxPageResponse(InboxItemResponse[] Items, string? NextCursor, InboxCountsResponse Counts);

public sealed record InboxItemRequest(string ItemKey);

public sealed record InboxSnoozeRequest(string ItemKey, DateTimeOffset Until);

public sealed record InboxSnoozeResponse(string ItemKey, DateTimeOffset SnoozedUntil);

public sealed record InboxReadResponse(string ItemKey, string Kind, Guid? ChannelId, Guid? ThreadId, long LastReadSequence);
