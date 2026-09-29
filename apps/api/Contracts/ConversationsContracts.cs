public sealed record ChannelResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Type,
    Guid? PeerUserId = null,
    string? PeerDisplayName = null,
    Guid? SpaceId = null,
    string? Topic = null,
    int? ParticipantCount = null,
    string[]? ParticipantNames = null,
    Guid[]? ParticipantUserIds = null,
    bool HasGuests = false);
public sealed record ChannelMemberResponse(Guid UserId, string DisplayName, string Email);
public sealed record UpdateChannelTopicRequest(string Topic);
public sealed record OpenDirectMessageRequest(Guid UserId);
public sealed record CreateSpaceRequest(string Name, int? Order = null);
public sealed record CreateChannelRequest(string Name, string Type, Guid? SpaceId = null);
public sealed record ThreadResponse(
    Guid Id,
    Guid ChannelId,
    Guid ParentMessageId,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    int ReplyCount,
    MessageResponse? ParentMessage = null,
    bool Following = false,
    string? FollowSource = null);
public sealed record UpsertReadCursorRequest(long LastReadSequence, bool AllowRetrograde = false);
public sealed record ThreadSubscriptionResponse(Guid ThreadId, bool Following, string Source, long LastReadSeq);
public sealed record UpsertThreadReadCursorRequest(long LastReadSequence, bool AllowRetrograde = false);
public sealed record FollowedThreadResponse(
    Guid ThreadId,
    Guid ChannelId,
    string ChannelName,
    string ChannelType,
    string RootPreview,
    bool RootDeleted,
    long UnreadCount,
    DateTimeOffset LastActivityAt);
public sealed record FollowedThreadsPageResponse(FollowedThreadResponse[] Items, string? NextCursor);
public sealed record ShareToChannelRequest(string IdempotencyKey);
public sealed record ShareToChannelResponse(Guid MessageId, Guid ChannelId, long Sequence, DateTimeOffset CreatedAt);
public sealed record ReadCursorResponse(Guid ChannelId, Guid UserId, long LastReadSequence, DateTimeOffset UpdatedAt);
public sealed record ChannelUnreadSummaryResponse(
    Guid ChannelId,
    int UnreadCount,
    int MentionCount,
    long LastReadSeq);
