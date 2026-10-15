using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>
/// Personal or team task derived from a message (B-123).
/// Curated fields only — the message body is never stored.
/// </summary>
public sealed class MessageTask
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public ChannelId ChannelId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskPriority Priority { get; set; }
    public TaskState State { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? OverdueNotifiedAt { get; set; }
    public MessageId SourceMessageId { get; set; }
    public Guid? SourceThreadId { get; set; }
    public long SourceVersion { get; set; }
    public string ExcerptHash { get; set; } = string.Empty;
    public string Method { get; set; } = TaskPolicies.ConvertMethod;
    public UserId CreatedBy { get; set; }
    public string AuthzSnapshot { get; set; } = "{}";
    public CommitmentVisibility Visibility { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public UserId? CompletedBy { get; set; }
    public bool SourceRedacted { get; set; }
}

public sealed class MessageTaskAssignee
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid TaskId { get; set; }
    public UserId UserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}

public sealed class MessageTaskChecklistItem
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid TaskId { get; set; }
    public int Position { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool Done { get; set; }
}

public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3,
}

public enum TaskState
{
    Open = 0,
    InProgress = 1,
    Completed = 2,
}

public enum TaskView
{
    Workspace = 0,
    Channel = 1,
    Mine = 2,
}

public enum TaskListState
{
    Active = 0,
    Open = 1,
    InProgress = 2,
    Completed = 3,
    All = 4,
}

public static class TaskPolicies
{
    public const string ConvertMethod = "convert";
    public const string Created = "task.created";
    public const string Updated = "task.updated";
    public const string Completed = "task.completed";
    public const string Overdue = "task.overdue";
    public const int MaxTitleLength = 120;
    public const int MaxDescriptionLength = 500;
    public const int MaxChecklistTextLength = 200;
    public const int MaxAssignees = 12;
    public const int MaxChecklistItems = 20;
    public const int MaxIdempotencyKeyLength = 80;
    public const int MaxAuthzSnapshotLength = 500;
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 50;
    public const int MaxScan = 200;
    public const int MaxDueYears = 5;

    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    public static bool IsValidTitle(string title) =>
        title.Length is >= 1 and <= MaxTitleLength;

    public static bool IsValidDescription(string? description) =>
        description is null || description.Length <= MaxDescriptionLength;

    public static bool IsValidChecklistText(string text) =>
        text.Length is >= 1 and <= MaxChecklistTextLength;

    public static bool IsValidIdempotencyKey(string key) =>
        key.Length is >= 1 and <= MaxIdempotencyKeyLength;

    public static bool IsValidDue(DateTimeOffset? dueAt, DateTimeOffset now) =>
        dueAt is null || dueAt <= now.AddYears(MaxDueYears);

    public static bool TryParsePriority(string? token, out TaskPriority priority)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "normal":
                priority = TaskPriority.Normal;
                return true;
            case "low":
                priority = TaskPriority.Low;
                return true;
            case "high":
                priority = TaskPriority.High;
                return true;
            case "urgent":
                priority = TaskPriority.Urgent;
                return true;
            default:
                priority = default;
                return false;
        }
    }

    public static string PriorityToken(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => "low",
        TaskPriority.Normal => "normal",
        TaskPriority.High => "high",
        TaskPriority.Urgent => "urgent",
        _ => throw new ArgumentOutOfRangeException(nameof(priority)),
    };

    public static bool TryParseState(string? token, out TaskState state)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "open":
                state = TaskState.Open;
                return true;
            case "in_progress":
                state = TaskState.InProgress;
                return true;
            case "completed":
                state = TaskState.Completed;
                return true;
            default:
                state = default;
                return false;
        }
    }

    public static string StateToken(TaskState state) => state switch
    {
        TaskState.Open => "open",
        TaskState.InProgress => "in_progress",
        TaskState.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static bool TryParseListState(string? token, out TaskListState state)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "active":
                state = TaskListState.Active;
                return true;
            case "open":
                state = TaskListState.Open;
                return true;
            case "in_progress":
                state = TaskListState.InProgress;
                return true;
            case "completed":
                state = TaskListState.Completed;
                return true;
            case "all":
                state = TaskListState.All;
                return true;
            default:
                state = default;
                return false;
        }
    }

    public static bool TryParseView(string? token, out TaskView view)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "workspace":
                view = TaskView.Workspace;
                return true;
            case "channel":
                view = TaskView.Channel;
                return true;
            case "mine":
                view = TaskView.Mine;
                return true;
            default:
                view = default;
                return false;
        }
    }

    public static bool TryNormalizeAssignees(IEnumerable<Guid>? raw, out Guid[] assignees)
    {
        assignees = [];
        if (raw is null)
        {
            return true;
        }

        var distinct = new List<Guid>();
        foreach (var id in raw)
        {
            if (id == Guid.Empty || distinct.Contains(id))
            {
                continue;
            }

            distinct.Add(id);
            if (distinct.Count > MaxAssignees)
            {
                assignees = [];
                return false;
            }
        }

        assignees = distinct.ToArray();
        return true;
    }

    public static bool TryNormalizeChecklist(IEnumerable<(string Text, bool Done)>? raw, out (string Text, bool Done)[] items)
    {
        items = [];
        if (raw is null)
        {
            return true;
        }

        var list = new List<(string Text, bool Done)>();
        foreach (var (text, done) in raw)
        {
            var normalized = Normalize(text);
            if (!IsValidChecklistText(normalized))
            {
                items = [];
                return false;
            }

            list.Add((normalized, done));
            if (list.Count > MaxChecklistItems)
            {
                items = [];
                return false;
            }
        }

        items = list.ToArray();
        return true;
    }

    /// <summary>True when the client version is the one currently stored. The next value is current + 1.</summary>
    public static bool TryBump(int current, int expected, out int next)
    {
        if (expected != current || current < 1)
        {
            next = current;
            return false;
        }

        next = current + 1;
        return true;
    }

    public static bool IsOverdue(DateTimeOffset? dueAt, TaskState state, DateTimeOffset now) =>
        dueAt is not null && dueAt <= now && state != TaskState.Completed;

    /// <summary>Worker emits task.overdue once, while the task is still open and past due.</summary>
    public static bool ShouldEmitOverdue(DateTimeOffset? dueAt, TaskState state, DateTimeOffset? notifiedAt, DateTimeOffset now) =>
        notifiedAt is null && IsOverdue(dueAt, state, now);

    public static string ExcerptHash(string? body) => CommitmentPolicies.ExcerptHash(body);

    public static bool IsConvertible(string? body) => CommitmentPolicies.IsConvertible(body);

    public static bool IsTaskEvent(string? type) =>
        type is Created or Updated or Completed or Overdue;

    public static bool CanSee(MessageTask item, UserId viewer, bool canReadChannel, bool isAssignee) =>
        canReadChannel
        && (item.Visibility == CommitmentVisibility.Channel
            || item.CreatedBy == viewer
            || isAssignee);

    public static bool CanMutate(MessageTask item, UserId actor, bool isAssignee, bool isWorkspaceAdmin) =>
        item.CreatedBy == actor
        || isAssignee
        || (isWorkspaceAdmin && item.Visibility == CommitmentVisibility.Channel);
}

public static class TaskChangeActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Completed = "completed";
    public const string Reopened = "reopened";
}
