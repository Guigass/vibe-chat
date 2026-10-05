using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>Process kill switch for edit history and message moves (B-114 / ADR-029). Default off.</summary>
public sealed class MessageHistoryOptions
{
    public const string SectionName = "Messaging:History";

    public bool Enabled { get; set; }
}

/// <summary>Previous body captured before an edit or a move tombstone (B-114).</summary>
public sealed class MessageVersion
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public MessageId MessageId { get; set; }
    public int VersionNumber { get; set; }
    public string Body { get; set; } = string.Empty;
    public UserId ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Idempotent move of a message (or thread root) inside one tenant (B-114).</summary>
public sealed class MessageMove
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public MessageId SourceMessageId { get; set; }
    public ChannelId SourceChannelId { get; set; }
    public MessageId DestinationMessageId { get; set; }
    public ChannelId DestinationChannelId { get; set; }
    public long DestinationSequence { get; set; }
    public UserId ActorUserId { get; set; }
    public string Scope { get; set; } = MessageHistoryPolicies.ScopeMessage;
    public bool LeaveTombstone { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class MessageHistoryPolicies
{
    public const string TombstoneBody = "<system:moved>";
    public const string ScopeMessage = "message";
    public const string ScopeThread = "thread";
    public const int MaxIdempotencyKeyLength = 200;

    public const string Disabled = "HistoryDisabled";
    public const string Hidden = "HistoryHidden";
    public const string AlreadyMoved = "AlreadyMoved";
    public const string MessageHasThread = "MessageHasThread";
    public const string NoThread = "NoThread";
    public const string SameChannel = "SameChannel";
    public const string NotMovable = "MessageNotMovable";
    public const string InvalidScope = "InvalidMoveScope";
    public const string IdempotencyConflict = "IdempotencyConflict";

    public static bool IsTombstone(string? body) =>
        string.Equals(body, TombstoneBody, StringComparison.Ordinal);

    public static string NormalizeScope(string? scope) =>
        string.Equals(scope, ScopeThread, StringComparison.OrdinalIgnoreCase)
            ? ScopeThread
            : string.IsNullOrWhiteSpace(scope) || string.Equals(scope, ScopeMessage, StringComparison.OrdinalIgnoreCase)
                ? ScopeMessage
                : string.Empty;
}
