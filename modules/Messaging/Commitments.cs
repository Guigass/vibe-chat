using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>
/// Decision or action item linked to a source message (B-119).
/// Curated fields only — the message body is never stored.
/// </summary>
public sealed class Commitment
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public ChannelId ChannelId { get; set; }
    public CommitmentKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public UserId? OwnerUserId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public CommitmentState State { get; set; }
    public MessageId SourceMessageId { get; set; }
    public Guid? SourceThreadId { get; set; }
    public long SourceVersion { get; set; }
    public string ExcerptHash { get; set; } = string.Empty;
    public string Method { get; set; } = CommitmentPolicies.ConvertMethod;
    public UserId CreatedBy { get; set; }
    public string AuthzSnapshot { get; set; } = "{}";
    public CommitmentVisibility Visibility { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public UserId? ClosedBy { get; set; }
    public bool SourceRedacted { get; set; }
}

/// <summary>Auditable field changes. Never stores message body.</summary>
public sealed class CommitmentChange
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid CommitmentId { get; set; }
    public UserId ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
}

public enum CommitmentKind
{
    Decision = 0,
    ActionItem = 1,
}

public enum CommitmentState
{
    Open = 0,
    Closed = 1,
}

public enum CommitmentVisibility
{
    Channel = 0,
    Restricted = 1,
}

public static class CommitmentPolicies
{
    public const string ConvertMethod = "convert";
    public const int MaxTitleLength = 120;
    public const int MaxDescriptionLength = 500;
    public const int MaxIdempotencyKeyLength = 80;
    public const int MaxAuthzSnapshotLength = 500;
    public const int ExcerptLength = 140;
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 50;
    public const int MaxScan = 200;
    public const int MaxDueYears = 5;

    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    public static bool IsValidTitle(string title) =>
        title.Length is >= 1 and <= MaxTitleLength;

    public static bool IsValidDescription(string? description) =>
        description is null || description.Length <= MaxDescriptionLength;

    public static bool IsValidIdempotencyKey(string key) =>
        key.Length is >= 1 and <= MaxIdempotencyKeyLength;

    public static bool IsValidDue(DateTimeOffset? dueAt, DateTimeOffset now) =>
        dueAt is null || dueAt <= now.AddYears(MaxDueYears);

    public static bool TryParseKind(string? token, out CommitmentKind kind)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "decision":
                kind = CommitmentKind.Decision;
                return true;
            case "action_item":
            case "action":
                kind = CommitmentKind.ActionItem;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string KindToken(CommitmentKind kind) => kind switch
    {
        CommitmentKind.Decision => "decision",
        CommitmentKind.ActionItem => "action_item",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseState(string? token, out CommitmentState? state)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "open":
                state = CommitmentState.Open;
                return true;
            case "closed":
                state = CommitmentState.Closed;
                return true;
            case "all":
                state = null;
                return true;
            default:
                state = null;
                return false;
        }
    }

    public static string StateToken(CommitmentState state) => state switch
    {
        CommitmentState.Open => "open",
        CommitmentState.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static bool TryParseVisibility(string? token, out CommitmentVisibility visibility)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "channel":
                visibility = CommitmentVisibility.Channel;
                return true;
            case "restricted":
                visibility = CommitmentVisibility.Restricted;
                return true;
            default:
                visibility = default;
                return false;
        }
    }

    public static string VisibilityToken(CommitmentVisibility visibility) => visibility switch
    {
        CommitmentVisibility.Channel => "channel",
        CommitmentVisibility.Restricted => "restricted",
        _ => throw new ArgumentOutOfRangeException(nameof(visibility)),
    };

    /// <summary>SHA-256 of a bounded excerpt. The excerpt itself is not retained.</summary>
    public static string ExcerptHash(string? body)
    {
        var text = body ?? string.Empty;
        if (text.StartsWith("<system:", StringComparison.Ordinal))
        {
            text = string.Empty;
        }

        if (text.Length > ExcerptLength)
        {
            text = text[..ExcerptLength];
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsConvertible(string? body) =>
        !string.IsNullOrEmpty(body) && !body.StartsWith("<system:", StringComparison.Ordinal);

    public static string EventType(CommitmentKind kind, string action) =>
        $"{(kind == CommitmentKind.Decision ? "decision" : "action_item")}.{action}";

    public static bool IsCommitmentEvent(string? type) =>
        type is not null
        && (type.StartsWith("decision.", StringComparison.Ordinal)
            || type.StartsWith("action_item.", StringComparison.Ordinal));

    public static bool CanSee(Commitment item, UserId viewer, bool canReadChannel) =>
        canReadChannel
        && (item.Visibility == CommitmentVisibility.Channel
            || item.CreatedBy == viewer
            || item.OwnerUserId == viewer);

    public static bool CanMutate(Commitment item, UserId actor, bool isWorkspaceAdmin) =>
        item.CreatedBy == actor
        || item.OwnerUserId == actor
        || (isWorkspaceAdmin && item.Visibility == CommitmentVisibility.Channel);
}

public static class CommitmentChangeActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Closed = "closed";
    public const string Reopened = "reopened";
}
