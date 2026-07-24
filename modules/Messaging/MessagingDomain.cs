using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;
using System.Security.Cryptography;
using System.Text;

namespace VibeChat.Messaging;

public sealed class Message : AggregateRoot
{
    public MessageId Id { get; set; }
    public TenantId TenantId { get; set; }
    public ChannelId ConversationId { get; set; }
    public long Sequence { get; set; }
    public UserId AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public MessageId? ReplyToMessageId { get; set; }
    public Guid? ThreadId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public UserId? DeletedBy { get; set; }

    public bool IsDeleted => DeletedAt is not null;
}

/// <summary>
/// Sub-conversation anchored on a parent channel message. Replies use ConversationId = Thread.Id for seq.
/// </summary>
public sealed class MessageThread : AggregateRoot
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public ChannelId ChannelId { get; set; }
    public MessageId ParentMessageId { get; set; }
    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Reaction
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public MessageId MessageId { get; set; }
    public UserId UserId { get; set; }
    public string Emoji { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class ReactionEmojis
{
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "👍", "❤️", "😂", "🎉", "👀", "✅"
    };

    public static bool IsAllowed(string? emoji) =>
        !string.IsNullOrWhiteSpace(emoji) && Allowed.Contains(emoji.Trim());
}

public sealed class ReadCursor
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public ChannelId ChannelId { get; set; }
    public UserId UserId { get; set; }
    public long LastReadSequence { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ConversationSequence
{
    public TenantId TenantId { get; set; }
    public ChannelId ConversationId { get; set; }
    public long LastSequence { get; set; }
}

public sealed class IdempotencyEntry
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record MessageCreatedEvent(
    TenantId TenantId,
    ChannelId ChannelId,
    MessageId MessageId,
    long Sequence,
    UserId AuthorId,
    DateTimeOffset CreatedAt) : IntegrationEvent(TenantId);

public sealed record MessageEditedEvent(
    TenantId TenantId,
    ChannelId ChannelId,
    MessageId MessageId,
    long Sequence,
    DateTimeOffset EditedAt) : IntegrationEvent(TenantId);

public sealed record MessageDeletedEvent(
    TenantId TenantId,
    ChannelId ChannelId,
    MessageId MessageId,
    long Sequence,
    DateTimeOffset DeletedAt) : IntegrationEvent(TenantId);

public sealed record ReactionChangedEvent(
    TenantId TenantId,
    ChannelId ChannelId,
    MessageId MessageId,
    UserId UserId,
    string Emoji,
    bool Added) : IntegrationEvent(TenantId);

public interface IConversationSequenceStore
{
    Task<long> NextAsync(TenantId tenantId, ChannelId conversationId, CancellationToken cancellationToken);
}

public interface IMessageWriter
{
    Task<MessageSendResult> SendAsync(SendMessageCommand command, CancellationToken cancellationToken);
}

public sealed record SendMessageCommand(
    TenantId TenantId,
    UserId UserId,
    ChannelId ChannelId,
    MessageId MessageId,
    string IdempotencyKey,
    string Body,
    MessageId? ReplyToMessageId,
    Guid? ThreadId,
    IReadOnlyList<Guid>? AttachmentIds = null);

public sealed record MessageSendResult(MessageId MessageId, long Sequence, DateTimeOffset CreatedAt, bool Idempotent);

public static class MessageIdempotency
{
    public static string ComputeRequestHash(SendMessageCommand command)
    {
        var attachmentPart = command.AttachmentIds is { Count: > 0 }
            ? string.Join(',', command.AttachmentIds.OrderBy(x => x))
            : string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{command.MessageId}:{command.ChannelId}:{command.Body}:{command.ReplyToMessageId}:{command.ThreadId}:{attachmentPart}")));
    }
}
