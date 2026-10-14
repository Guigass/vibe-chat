using System.Globalization;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Conversations;

/// <summary>
/// Channel presentation. Stream is the historical timeline. Topics is an explicit
/// forum mode (B-118); existing channels stay Stream.
/// </summary>
public enum ChannelLayout
{
    Stream = 0,
    Topics = 1
}

public enum TopicState
{
    Open = 0,
    Closed = 1
}

/// <summary>
/// Forum topic metadata. <see cref="Id"/> is the conversation id used by messages,
/// sequences and read cursors. It is not a second message aggregate and it does
/// not replace a <c>MessageThread</c>.
/// </summary>
public sealed class ChannelTopic
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public ChannelId ChannelId { get; set; }
    /// <summary>Same value as <see cref="Id"/>. Lets search compare conversation ids without a client cast.</summary>
    public ChannelId ConversationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public TopicState State { get; set; }
    public string Tags { get; set; } = string.Empty;
    public string ParticipantIds { get; set; } = string.Empty;
    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public UserId? ClosedBy { get; set; }
    public ChannelId? MovedFromChannelId { get; set; }
}

/// <summary>Personal follow. Unread comes from the topic read cursor, not a second counter.</summary>
public sealed class TopicSubscription
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public Guid TopicId { get; set; }
    public ChannelId ChannelId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TopicChannelOptions
{
    public const string SectionName = "Conversations:Topics";

    public bool Enabled { get; set; }
}

public static class TopicPolicies
{
    public const int MaxTitleLength = 120;
    public const int MaxSlugLength = 80;
    public const int MaxTags = 8;
    public const int MaxTagLength = 32;
    public const int MaxTopicsPerChannel = 200;
    public const int MaxParticipants = 8;
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 50;

    public static bool TryParseLayout(string? value, out ChannelLayout layout)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Stream", StringComparison.OrdinalIgnoreCase))
        {
            layout = ChannelLayout.Stream;
            return true;
        }

        if (value.Equals("Topics", StringComparison.OrdinalIgnoreCase))
        {
            layout = ChannelLayout.Topics;
            return true;
        }

        layout = default;
        return false;
    }

    public static bool TryNormalizeTitle(string? value, out string title)
    {
        title = (value ?? string.Empty).Trim();
        return title.Length is >= 1 and <= MaxTitleLength;
    }

    public static bool TryNormalizeTags(IEnumerable<string>? tags, out string stored, out string error)
    {
        var list = new List<string>();
        foreach (var raw in tags ?? [])
        {
            var tag = raw.Trim().ToLowerInvariant();
            if (tag.Length == 0)
            {
                continue;
            }

            if (tag.Length > MaxTagLength || tag.Contains(',', StringComparison.Ordinal) || tag.Contains(' ', StringComparison.Ordinal))
            {
                stored = string.Empty;
                error = "InvalidTopicTag";
                return false;
            }

            if (!list.Contains(tag, StringComparer.Ordinal))
            {
                list.Add(tag);
            }
        }

        if (list.Count > MaxTags)
        {
            stored = string.Empty;
            error = "TooManyTopicTags";
            return false;
        }

        stored = string.Join(',', list);
        error = string.Empty;
        return true;
    }

    public static string[] ReadTags(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Slugify(string title)
    {
        var decomposed = title.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingHyphen = false;
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingHyphen = false;
                builder.Append(ch);
                continue;
            }

            pendingHyphen = true;
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            slug = "topico";
        }

        return slug.Length <= MaxSlugLength ? slug : slug[..MaxSlugLength].Trim('-');
    }

    public static string NextSlug(string title, IReadOnlySet<string> taken)
    {
        var baseSlug = Slugify(title);
        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var n = 2; n < 1000; n++)
        {
            var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
            var room = Math.Max(1, MaxSlugLength - suffix.Length);
            var candidate = baseSlug[..Math.Min(baseSlug.Length, room)].Trim('-') + suffix;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return baseSlug[..Math.Min(baseSlug.Length, 40)] + "-" + Guid.NewGuid().ToString("N")[..8];
    }

    public static bool IsOverQuota(int existing) => existing >= MaxTopicsPerChannel;

    public static string RememberParticipant(string? stored, Guid userId)
    {
        var ids = new List<Guid>();
        if (!string.IsNullOrWhiteSpace(stored))
        {
            foreach (var part in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Guid.TryParse(part, out var id) && id != userId && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
        }

        ids.Add(userId);
        if (ids.Count > MaxParticipants)
        {
            ids = ids[^MaxParticipants..];
        }

        return string.Join(',', ids);
    }

    public static Guid[] ReadParticipants(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }

        var ids = new List<Guid>();
        foreach (var part in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id))
            {
                ids.Add(id);
            }
        }

        return ids.ToArray();
    }

    public static string EncodeCursor(DateTimeOffset activity, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            activity.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + id.ToString("D")));

    public static bool TryDecodeCursor(string? cursor, out DateTimeOffset activity, out Guid id)
    {
        activity = default;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var separator = raw.IndexOf(':');
            if (separator <= 0 || !long.TryParse(raw[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            {
                return false;
            }

            activity = new DateTimeOffset(ticks, TimeSpan.Zero);
            return Guid.TryParseExact(raw[(separator + 1)..], "D", out id);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class TopicEvents
{
    public const string Created = "topic.created";
    public const string Renamed = "topic.renamed";
    public const string Closed = "topic.closed";
    public const string Reopened = "topic.reopened";
    public const string Moved = "topic.moved";
    public const string Followed = "topic.followed";
}
