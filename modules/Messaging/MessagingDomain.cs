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
    public MessageId? ForwardedFromMessageId { get; set; }
    public ChannelId? ForwardedFromChannelId { get; set; }
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

public sealed class PinnedMessage
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public ChannelId ChannelId { get; set; }
    public MessageId MessageId { get; set; }
    public UserId PinnedByUserId { get; set; }
    public DateTimeOffset PinnedAt { get; set; }
}

public static class PinPolicies
{
    public const int MaxPinnedPerChannel = 20;
}

/// <summary>Personal bookmark (B-093); not shared with the channel.</summary>
public sealed class SavedMessage
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public MessageId MessageId { get; set; }
    public ChannelId ChannelId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class SavedMessagePolicies
{
    public const int MaxNoteLength = 280;
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 100;
}

public static class SystemEventTokens
{
    public const string PinPrefix = "<system:pin:";
    public const string UnpinPrefix = "<system:unpin:";

    public static string PinBody(MessageId messageId) => $"{PinPrefix}{messageId.Value}>";

    public static string UnpinBody(MessageId messageId) => $"{UnpinPrefix}{messageId.Value}>";

    public static bool TryParse(string body, out bool pinned, out Guid targetMessageId)
    {
        pinned = false;
        targetMessageId = Guid.Empty;
        if (string.IsNullOrEmpty(body))
        {
            return false;
        }

        if (body.StartsWith(PinPrefix, StringComparison.Ordinal) && body.EndsWith('>'))
        {
            var raw = body[PinPrefix.Length..^1];
            if (Guid.TryParse(raw, out targetMessageId) && targetMessageId != Guid.Empty)
            {
                pinned = true;
                return true;
            }
        }

        if (body.StartsWith(UnpinPrefix, StringComparison.Ordinal) && body.EndsWith('>'))
        {
            var raw = body[UnpinPrefix.Length..^1];
            if (Guid.TryParse(raw, out targetMessageId) && targetMessageId != Guid.Empty)
            {
                pinned = false;
                return true;
            }
        }

        return false;
    }
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

public sealed record PinChangedEvent(
    TenantId TenantId,
    ChannelId ChannelId,
    MessageId MessageId,
    UserId ByUserId,
    bool Pinned) : IntegrationEvent(TenantId);

public interface IConversationSequenceStore
{
    Task<long> NextAsync(TenantId tenantId, ChannelId conversationId, CancellationToken cancellationToken);
}

public interface IMessageWriter
{
    Task<MessageSendResult> SendAsync(SendMessageCommand command, CancellationToken cancellationToken);
    Task<ForwardMessageResult> ForwardAsync(ForwardMessageCommand command, CancellationToken cancellationToken);
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

public sealed record ForwardMessageCommand(
    TenantId TenantId,
    UserId UserId,
    WorkspaceId WorkspaceId,
    MessageId SourceMessageId,
    string IdempotencyKey,
    IReadOnlyList<ChannelId> TargetChannelIds,
    string? Comment);

public sealed record ForwardedMessageResult(
    MessageId MessageId,
    ChannelId ChannelId,
    long Sequence,
    DateTimeOffset CreatedAt);

public sealed record ForwardMessageResult(
    IReadOnlyList<ForwardedMessageResult> Messages,
    bool Idempotent);

public static class MessageForwardPolicies
{
    public const int MaxTargets = 5;
}

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

    public static string ComputeForwardRequestHash(ForwardMessageCommand command)
    {
        var targets = string.Join(',', command.TargetChannelIds.Select(x => x.Value).OrderBy(x => x));
        var comment = MessageBodyPolicies.Normalize(command.Comment);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{command.SourceMessageId}:{command.WorkspaceId}:{targets}:{comment}")));
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

public enum LinkPreviewStatus
{
    Pending = 0,
    Ready = 1,
    Failed = 2,
    Blocked = 3
}

/// <summary>Cached Open Graph / oEmbed metadata for a URL within one tenant (B-091).</summary>
public sealed class LinkPreview
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string UrlHash { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? SiteName { get; set; }
    public string? ImageKey { get; set; }
    public string? ImageContentType { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public LinkPreviewStatus Status { get; set; }
}

/// <summary>Junction message ↔ link preview; soft-removed when author dismisses the card.</summary>
public sealed class MessageLinkPreview
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public MessageId MessageId { get; set; }
    public ChannelId ChannelId { get; set; }
    public Guid LinkPreviewId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

/// <summary>Per-tenant kill switch for link preview (admin). Env <c>LinkPreview:Enabled</c> is the process gate.</summary>
public sealed class TenantLinkPreviewSettings
{
    public TenantId TenantId { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class LinkPreviewPolicies
{
    public const int CacheTtlDays = 7;
    public const int MaxRedirects = 3;
    public const int DefaultTimeoutMs = 8000;
    /// <summary>HTML cap — YouTube and similar place OG tags past 512 KB.</summary>
    public const int MaxHtmlBodyBytes = 1536 * 1024;
    /// <summary>Thumbnail download cap (SSRF budget).</summary>
    public const int MaxImageBodyBytes = 512 * 1024;
    public const int MaxTitleLength = 240;
    public const int MaxDescriptionLength = 480;
    public const int MaxSiteNameLength = 120;
    public const string UserAgent = "VibeChat-LinkPreview/1.0";

    private static readonly Regex HttpUrlRegex = new(
        @"https?://[^\s<>""')\]]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? ExtractFirstUrl(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var match = HttpUrlRegex.Match(body);
        if (!match.Success)
        {
            return null;
        }

        return NormalizeUrl(match.Value.TrimEnd('.', ',', ';', '!', '?', ')', ']'));
    }

    public static string? NormalizeUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };
        return builder.Uri.AbsoluteUri;
    }

    public static string ComputeUrlHash(string normalizedUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string BuildImageKey(TenantId tenantId, Guid previewId) =>
        $"tenants/{tenantId.Value:N}/link-previews/{previewId:N}/image";

    public static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    public static bool IsBlockedIp(System.Net.IPAddress address)
    {
        if (System.Net.IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Teredo)
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlockedIp(address.MapToIPv4());
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes[0] == 0 || bytes[0] == 10 || bytes[0] == 127)
        {
            return true;
        }

        if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
        {
            return true;
        }

        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
        {
            return true;
        }

        if (bytes[0] >= 224)
        {
            return true;
        }

        return false;
    }

    public static bool TryParseHostAsIp(string host, out System.Net.IPAddress address) =>
        System.Net.IPAddress.TryParse(host, out address!);
}
