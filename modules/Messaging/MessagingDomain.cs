using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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

public enum MentionKind
{
    User = 0,
    Here = 1,
    Channel = 2
}

public sealed class MessageMention
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public MessageId MessageId { get; set; }
    public ChannelId ChannelId { get; set; }
    public UserId? MentionedUserId { get; set; }
    public MentionKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MentionAllForbiddenException : UnauthorizedAccessException
{
    public MentionAllForbiddenException() : base("MentionAllForbidden") { }
}

public static partial class MentionTokens
{
    public const string HereBodyToken = "<@here>";
    public const string ChannelBodyToken = "<@channel>";

    public static string UserBodyToken(UserId userId) => $"<@{userId.Value}>";

    public sealed record ParsedToken(MentionKind Kind, UserId? UserId, string BodyToken);

    public static IReadOnlyList<ParsedToken> ParseBody(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return [];
        }

        var tokens = new List<ParsedToken>();
        foreach (Match match in UserTokenRegex().Matches(body))
        {
            var raw = match.Groups[1].Value;
            if (string.Equals(raw, "here", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new ParsedToken(MentionKind.Here, null, HereBodyToken));
                continue;
            }

            if (string.Equals(raw, "channel", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new ParsedToken(MentionKind.Channel, null, ChannelBodyToken));
                continue;
            }

            if (Guid.TryParse(raw, out var userId) && userId != Guid.Empty)
            {
                tokens.Add(new ParsedToken(MentionKind.User, new UserId(userId), UserBodyToken(new UserId(userId))));
            }
        }

        return tokens;
    }

    [GeneratedRegex(@"<@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|here|channel)>", RegexOptions.CultureInvariant)]
    private static partial Regex UserTokenRegex();
}

/// <summary>
/// Validates Unicode emoji sequences for reactions (B-083). Accepts ZWJ chains, modifiers and flags.
/// </summary>
public static class EmojiValidator
{
    public const int MaxCodePoints = 8;

    public static bool IsValid(string? emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji))
        {
            return false;
        }

        var value = emoji.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        var runes = value.EnumerateRunes().ToArray();
        if (runes.Length == 0 || runes.Length > MaxCodePoints)
        {
            return false;
        }

        var hasEmoji = false;
        foreach (var rune in runes)
        {
            if (IsJoinerOrModifier(rune))
            {
                continue;
            }

            if (IsRegionalIndicator(rune))
            {
                hasEmoji = true;
                continue;
            }

            if (IsEmojiRune(rune))
            {
                hasEmoji = true;
                continue;
            }

            return false;
        }

        return hasEmoji;
    }

    private static bool IsJoinerOrModifier(Rune rune) =>
        rune.Value is 0x200D or 0xFE0F or 0xFE0E or 0x20E3
        || rune.Value is >= 0x1F3FB and <= 0x1F3FF;

    private static bool IsRegionalIndicator(Rune rune) =>
        rune.Value is >= 0x1F1E6 and <= 0x1F1FF;

    private static bool IsEmojiRune(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherNumber
            or UnicodeCategory.SpaceSeparator
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.OtherPunctuation
            or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol)
        {
            return false;
        }

        var value = rune.Value;
        return category is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol
            || value is >= 0x1F000 and <= 0x1FAFF
            or >= 0x2600 and <= 0x27BF
            or >= 0x2300 and <= 0x23FF;
    }
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

/// <summary>
/// Tenant-level soft-delete purge policy (B-047 / ADR-018).
/// Hard-delete only runs when process flag MessageRetention:Enabled=true AND this row is Enabled.
/// </summary>
public sealed class MessageRetentionSettings
{
    public const int DefaultRetentionDays = 90;
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 3650;

    public TenantId TenantId { get; set; }
    public bool Enabled { get; set; }
    public int RetentionDays { get; set; } = DefaultRetentionDays;
    public DateTimeOffset UpdatedAt { get; set; }
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

public static class MessageBodyPolicies
{
    public const int MaxLength = 8000;

    /// <summary>UTF-16 code units — matches JavaScript String.length and PostgreSQL varchar(n).</summary>
    public static int MeasureLength(string? body) => body?.Length ?? 0;

    public static string Normalize(string? body) => body?.Trim() ?? string.Empty;

    public static bool IsWithinLimit(string? body) => MeasureLength(body) <= MaxLength;

    public static bool IsEmpty(string? body) => string.IsNullOrWhiteSpace(body);

    public static object TooLongPayload() => new
    {
        error = "MessageBodyTooLong",
        message = "A mensagem excede o limite de 8000 caracteres.",
        maxLength = MaxLength
    };
}
