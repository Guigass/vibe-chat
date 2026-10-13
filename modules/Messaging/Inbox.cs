using System.Globalization;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>
/// Unified inbox kinds. The numeric value is the deterministic priority
/// (lower comes first): announcement, mention, direct, thread, saved.
/// </summary>
public enum InboxItemKind
{
    Announcement = 1,
    Mention = 2,
    Direct = 3,
    Thread = 4,
    Saved = 5,
}

public enum InboxStateFilter
{
    Open = 0,
    Snoozed = 1,
    All = 2,
}

/// <summary>Personal snooze. Not a read cursor.</summary>
public sealed class InboxSnooze
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public UserId UserId { get; set; }
    public string ItemKey { get; set; } = string.Empty;
    public DateTimeOffset SnoozeUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record InboxCursor(int Priority, DateTimeOffset SortAt, string ItemKey);

public sealed record InboxCandidate(
    InboxItemKind Kind,
    Guid SourceId,
    Guid ChannelId,
    string ChannelName,
    Guid? MessageId,
    Guid? ThreadId,
    long? Sequence,
    string BodyPreview,
    bool MessageRemoved,
    DateTimeOffset SortAt,
    DateTimeOffset? SnoozedUntil)
{
    public string ItemKey => InboxPolicies.ItemKey(Kind, SourceId);

    public int Priority => InboxPolicies.Priority(Kind);

    public string Reason => InboxPolicies.Reason(Kind);
}

public static class InboxPolicies
{
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 50;
    public const int MaxItemsPerKind = 200;
    public const int MaxSnoozeDays = 30;
    public const int PreviewLength = 140;
    public const int MaxItemKeyLength = 80;

    public static int Priority(InboxItemKind kind) => (int)kind;

    public static string Token(InboxItemKind kind) => kind switch
    {
        InboxItemKind.Announcement => "announcement",
        InboxItemKind.Mention => "mention",
        InboxItemKind.Direct => "dm",
        InboxItemKind.Thread => "thread",
        InboxItemKind.Saved => "saved",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Reason(InboxItemKind kind) => kind switch
    {
        InboxItemKind.Announcement => "pending_ack",
        InboxItemKind.Mention => "mention",
        InboxItemKind.Direct => "unread_dm",
        InboxItemKind.Thread => "followed_thread",
        InboxItemKind.Saved => "saved",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseKind(string? token, out InboxItemKind kind)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "announcement":
                kind = InboxItemKind.Announcement;
                return true;
            case "mention":
                kind = InboxItemKind.Mention;
                return true;
            case "dm":
            case "direct":
                kind = InboxItemKind.Direct;
                return true;
            case "thread":
                kind = InboxItemKind.Thread;
                return true;
            case "saved":
                kind = InboxItemKind.Saved;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static bool TryParseState(string? token, out InboxStateFilter state)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "open":
                state = InboxStateFilter.Open;
                return true;
            case "snoozed":
                state = InboxStateFilter.Snoozed;
                return true;
            case "all":
                state = InboxStateFilter.All;
                return true;
            default:
                state = default;
                return false;
        }
    }

    public static string ItemKey(InboxItemKind kind, Guid sourceId) =>
        $"{Token(kind)}:{sourceId:D}";

    public static bool TryParseItemKey(string? raw, out InboxItemKind kind, out Guid sourceId)
    {
        kind = default;
        sourceId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var separator = raw.IndexOf(':');
        if (separator <= 0 || separator >= raw.Length - 1)
        {
            return false;
        }

        if (!TryParseKind(raw[..separator], out kind))
        {
            return false;
        }

        return Guid.TryParseExact(raw[(separator + 1)..], "D", out sourceId) && sourceId != Guid.Empty;
    }

    public static bool IsSnoozeActive(DateTimeOffset? until, DateTimeOffset now) =>
        until is not null && until > now;

    public static bool IsSnoozeUntilAllowed(DateTimeOffset until, DateTimeOffset now) =>
        until > now && until <= now.AddDays(MaxSnoozeDays);

    public static bool UsesReadCursor(InboxItemKind kind) =>
        kind is InboxItemKind.Mention or InboxItemKind.Direct or InboxItemKind.Thread;

    public static string Preview(string? body)
    {
        if (string.IsNullOrEmpty(body) || body.StartsWith("<system:", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var plain = MentionTokens.FormatPlainText(body).Trim();
        if (plain.Length <= PreviewLength)
        {
            return plain;
        }

        return plain[..PreviewLength].TrimEnd() + "…";
    }

    /// <summary>
    /// Message-scoped kinds share a message id. Keep the higher-priority kind
    /// so the same message is not listed twice. DMs and threads stay.
    /// </summary>
    public static List<InboxCandidate> Dedup(IReadOnlyList<InboxCandidate> items)
    {
        var passthrough = new List<InboxCandidate>();
        var bestByMessage = new Dictionary<Guid, InboxCandidate>();
        foreach (var item in items)
        {
            if (item.Kind is InboxItemKind.Direct or InboxItemKind.Thread || item.MessageId is null)
            {
                passthrough.Add(item);
                continue;
            }

            var messageId = item.MessageId.Value;
            if (!bestByMessage.TryGetValue(messageId, out var existing) || item.Priority < existing.Priority)
            {
                bestByMessage[messageId] = item;
            }
        }

        passthrough.AddRange(bestByMessage.Values);
        return passthrough;
    }

    public static List<InboxCandidate> Sort(IEnumerable<InboxCandidate> items) =>
        items
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.SortAt)
            .ThenBy(item => item.ItemKey, StringComparer.Ordinal)
            .ToList();

    public static List<InboxCandidate> Filter(
        IEnumerable<InboxCandidate> items,
        InboxItemKind? kind,
        InboxStateFilter state)
    {
        return items.Where(item =>
        {
            if (kind is not null && item.Kind != kind.Value)
            {
                return false;
            }

            var snoozed = item.SnoozedUntil is not null;
            return state switch
            {
                InboxStateFilter.Open => !snoozed,
                InboxStateFilter.Snoozed => snoozed,
                _ => true,
            };
        }).ToList();
    }

    public static (IReadOnlyList<InboxCandidate> Page, string? NextCursor) Page(
        IReadOnlyList<InboxCandidate> sorted,
        string? cursor,
        int limit)
    {
        var start = 0;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!TryDecodeCursor(cursor, out var decoded))
            {
                throw new ArgumentException("InvalidCursor");
            }

            start = -1;
            for (var i = 0; i < sorted.Count; i++)
            {
                if (CompareAfter(sorted[i], decoded) > 0)
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
            {
                start = sorted.Count;
            }
        }

        var pageSize = Math.Clamp(limit, 1, MaxPageSize);
        var page = sorted.Skip(start).Take(pageSize).ToList();
        string? next = null;
        if (page.Count > 0 && start + page.Count < sorted.Count)
        {
            next = EncodeCursor(page[^1]);
        }

        return (page, next);
    }

    public static string EncodeCursor(InboxCandidate item) =>
        EncodeCursor(item.Priority, item.SortAt, item.ItemKey);

    public static string EncodeCursor(int priority, DateTimeOffset sortAt, string itemKey)
    {
        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{priority}|{sortAt.ToUniversalTime().Ticks}|{itemKey}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecodeCursor(string raw, out InboxCursor cursor)
    {
        cursor = new InboxCursor(0, DateTimeOffset.UnixEpoch, string.Empty);
        try
        {
            var padded = raw.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var parts = text.Split('|', 3);
            if (parts.Length != 3
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var priority)
                || priority is < 1 or > 5
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !TryParseItemKey(parts[2], out _, out _))
            {
                return false;
            }

            cursor = new InboxCursor(priority, new DateTimeOffset(ticks, TimeSpan.Zero), parts[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Positive when <paramref name="item"/> sorts after the cursor.</summary>
    public static int CompareAfter(InboxCandidate item, InboxCursor cursor)
    {
        var priority = item.Priority.CompareTo(cursor.Priority);
        if (priority != 0)
        {
            return priority;
        }

        var time = cursor.SortAt.CompareTo(item.SortAt);
        if (time != 0)
        {
            return time;
        }

        return string.CompareOrdinal(item.ItemKey, cursor.ItemKey);
    }
}
