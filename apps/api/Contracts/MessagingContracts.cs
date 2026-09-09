using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

public sealed record SlashCommandResponse(string Name, string Description, string Usage, string? Permission = null);
public sealed record SendMessageRequest(Guid MessageId, string IdempotencyKey, string Body, Guid? ReplyToMessageId, Guid? ThreadId, Guid[]? AttachmentIds = null);
public sealed record CreatePollRequest(
    Guid MessageId,
    string IdempotencyKey,
    string Question,
    string[] Options,
    bool AllowMultiple = false,
    bool Anonymous = false,
    DateTimeOffset? ClosesAt = null);
public sealed record CastPollVoteRequest(Guid[] OptionIds);
public sealed record EditMessageRequest(string Body);
public sealed record ReactionSummaryResponse(string Emoji, int Count, bool Me);
public sealed record ReactionUserResponse(Guid UserId, string DisplayName);
public sealed record ReactionUsersResponse(string Emoji, ReactionUserResponse[] Users, int Total);
public sealed record ToggleReactionRequest(string Emoji);
public sealed record ToggleReactionResponse(
    Guid MessageId,
    Guid ChannelId,
    string Emoji,
    bool Added,
    ReactionSummaryResponse[] Reactions);
public sealed record ReplyToResponse(
    Guid MessageId,
    string AuthorName,
    string Preview,
    bool Deleted);

public sealed record ForwardedFromResponse(
    Guid MessageId,
    Guid ChannelId,
    string ChannelName,
    string AuthorName,
    DateTimeOffset CreatedAt,
    bool IsDirect = false,
    Guid? ThreadId = null);

public sealed record ChannelMessagesResponse(
    MessageResponse[] Messages,
    bool HasMoreBefore,
    bool HasMoreAfter);

public sealed record ChannelMessageRow(
    MessageId Id,
    ChannelId ChannelId,
    long Sequence,
    UserId AuthorId,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    Guid? ThreadId,
    MessageId? ReplyToMessageId,
    MessageId? ForwardedFromMessageId,
    ChannelId? ForwardedFromChannelId,
    string AuthorName);

public sealed record MessageResponse(
    Guid Id,
    Guid ChannelId,
    long Sequence,
    Guid AuthorId,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    string AuthorName = "",
    AttachmentResponse[]? Attachments = null,
    Guid? ThreadId = null,
    Guid? ReplyToMessageId = null,
    int ReplyCount = 0,
    Guid? ConversationId = null,
    ReactionSummaryResponse[]? Reactions = null,
    ReplyToResponse? ReplyTo = null,
    Guid? ForwardedFromMessageId = null,
    Guid? ForwardedFromChannelId = null,
    ForwardedFromResponse? ForwardedFrom = null,
    LinkPreviewResponse? LinkPreview = null,
    bool IsPinned = false,
    PollDto? Poll = null);

public sealed record PinMessageResponse(Guid MessageId, Guid ChannelId, bool Pinned, int PinCount);
public sealed record PinnedMessageResponse(
    Guid MessageId,
    Guid ChannelId,
    long Sequence,
    string BodyPreview,
    string AuthorName,
    Guid PinnedByUserId,
    string PinnedByName,
    DateTimeOffset PinnedAt);
public sealed record ChannelPinsResponse(
    PinnedMessageResponse[] Pins,
    int Count,
    int Limit);

public sealed record SaveMessageRequest(Guid MessageId, string? Note = null);
public sealed record PatchSavedMessageRequest(string? Note = null, bool? Completed = null);
public sealed record SavedMessageResponse(
    Guid MessageId,
    Guid ChannelId,
    string ChannelName,
    string ChannelType,
    long Sequence,
    Guid AuthorUserId,
    string AuthorName,
    string BodyPreview,
    string? Note,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    bool MessageRemoved = false);
public sealed record SavedMessagesPageResponse(
    SavedMessageResponse[] Items,
    string? NextCursor,
    int PendingCount);

public sealed record LinkPreviewResponse(
    Guid Id,
    string Url,
    string? Title,
    string? Description,
    string? SiteName,
    bool HasImage,
    string Status);

public sealed record ForwardMessageRequest(
    Guid[] TargetChannelIds,
    string? Comment,
    string IdempotencyKey);

public sealed record ForwardMessageResponse(MessageResponse[] Messages);
