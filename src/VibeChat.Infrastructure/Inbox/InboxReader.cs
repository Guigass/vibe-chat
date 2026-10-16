using Microsoft.EntityFrameworkCore;
using VibeChat.Conversations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed record InboxCounts(int Announcement, int Mention, int Direct, int Thread, int Saved, int Topic, int Commitment, int Tasks)
{
    public int Total => Announcement + Mention + Direct + Thread + Saved + Topic + Commitment + Tasks;

    public static InboxCounts From(IEnumerable<InboxCandidate> items)
    {
        var announcement = 0;
        var mention = 0;
        var direct = 0;
        var thread = 0;
        var saved = 0;
        var topic = 0;
        var commitment = 0;
        var tasks = 0;
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case InboxItemKind.Announcement:
                    announcement++;
                    break;
                case InboxItemKind.Mention:
                    mention++;
                    break;
                case InboxItemKind.Direct:
                    direct++;
                    break;
                case InboxItemKind.Thread:
                    thread++;
                    break;
                case InboxItemKind.Saved:
                    saved++;
                    break;
                case InboxItemKind.Topic:
                    topic++;
                    break;
                case InboxItemKind.Commitment:
                    commitment++;
                    break;
                case InboxItemKind.Task:
                    tasks++;
                    break;
            }
        }

        return new InboxCounts(announcement, mention, direct, thread, saved, topic, commitment, tasks);
    }
}

public sealed record InboxListResult(
    IReadOnlyList<InboxCandidate> Items,
    string? NextCursor,
    InboxCounts Counts);

/// <summary>
/// Rebuildable inbox. Read state stays on channel cursors and thread subscriptions.
/// </summary>
public static class InboxReader
{
    public static async Task<IReadOnlyList<InboxCandidate>> CollectAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        UserId userId,
        bool isWorkspaceMember,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var channels = await LoadChannelsAsync(db, workspaceId, userId, isWorkspaceMember, cancellationToken);
        if (channels.Count == 0)
        {
            return [];
        }

        var byId = channels.ToDictionary(channel => channel.Id);
        var channelIds = channels.Select(channel => channel.Id).ToArray();
        var names = await DirectNamesAsync(db, channels, userId, cancellationToken);
        foreach (var channel in channels)
        {
            channel.DisplayName = names.TryGetValue(channel.Id, out var peer) && !string.IsNullOrWhiteSpace(peer)
                ? peer
                : ChannelLabel(channel);
        }

        var snoozes = await db.InboxSnoozes.AsNoTracking()
            .Where(row => row.UserId == userId && row.WorkspaceId == workspaceId)
            .ToListAsync(cancellationToken);
        var snoozeUntil = snoozes.ToDictionary(row => row.ItemKey, row => row.SnoozeUntil, StringComparer.Ordinal);

        var items = new List<InboxCandidate>();
        items.AddRange(await AnnouncementsAsync(db, byId, channelIds, userId, now, cancellationToken));
        items.AddRange(await MentionsAsync(db, byId, channelIds, userId, cancellationToken));
        items.AddRange(await DirectsAsync(db, channels, userId, cancellationToken));
        items.AddRange(await ThreadsAsync(db, byId, workspaceId, userId, cancellationToken));
        items.AddRange(await SavedAsync(db, byId, workspaceId, userId, cancellationToken));
        items.AddRange(await TopicsAsync(db, byId, workspaceId, userId, cancellationToken));
        items.AddRange(await CommitmentsAsync(db, byId, workspaceId, userId, now, cancellationToken));
        items.AddRange(await TasksAsync(db, byId, workspaceId, userId, now, cancellationToken));

        var deduped = InboxPolicies.Dedup(items);
        return deduped.Select(item => WithSnooze(item, snoozeUntil, now)).ToList();
    }

    public static InboxListResult Apply(
        IReadOnlyList<InboxCandidate> collected,
        InboxItemKind? kind,
        InboxStateFilter state,
        string? cursor,
        int limit)
    {
        var sorted = InboxPolicies.Sort(collected);
        var counted = InboxPolicies.Filter(sorted, kind: null, state);
        var visible = kind is null ? counted : InboxPolicies.Filter(counted, kind, state);
        var (page, next) = InboxPolicies.Page(visible, cursor, limit);
        return new InboxListResult(page, next, InboxCounts.From(counted));
    }

    public static async Task<InboxCandidate?> FindAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        UserId userId,
        bool isWorkspaceMember,
        string itemKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!InboxPolicies.TryParseItemKey(itemKey, out _, out _))
        {
            return null;
        }

        var collected = await CollectAsync(db, workspaceId, userId, isWorkspaceMember, now, cancellationToken);
        return collected.FirstOrDefault(item => item.ItemKey == itemKey);
    }

    private static InboxCandidate WithSnooze(
        InboxCandidate item,
        IReadOnlyDictionary<string, DateTimeOffset> snoozeUntil,
        DateTimeOffset now)
    {
        if (!snoozeUntil.TryGetValue(item.ItemKey, out var until) || !InboxPolicies.IsSnoozeActive(until, now))
        {
            return item;
        }

        return item with { SnoozedUntil = until };
    }

    private static async Task<List<ChannelAccess>> LoadChannelsAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        UserId userId,
        bool isWorkspaceMember,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from channel in db.Channels.AsNoTracking()
            where channel.WorkspaceId == workspaceId
            join member in db.ChannelMembers.AsNoTracking().Where(row => row.UserId == userId && row.LeftAt == null)
                on channel.Id equals member.ChannelId into members
            from member in members.DefaultIfEmpty()
            where ((channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement) && isWorkspaceMember)
                || member != null
            select new
            {
                channel.Id,
                channel.Type,
                channel.Name,
                channel.Title,
                channel.CreatedAt,
                JoinedSeq = member != null ? member.JoinedSeq : 0L,
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var cursors = await db.ReadCursors.AsNoTracking()
            .Where(row => row.UserId == userId && ids.Contains(row.ChannelId))
            .ToDictionaryAsync(row => row.ChannelId, row => row.LastReadSequence, cancellationToken);

        return rows.Select(row => new ChannelAccess
        {
            Id = row.Id,
            Type = row.Type,
            Name = row.Name,
            Title = row.Title,
            CreatedAt = row.CreatedAt,
            JoinedSeq = row.JoinedSeq,
            LastRead = cursors.TryGetValue(row.Id, out var cursor) ? cursor : 0L,
        }).ToList();
    }

    private static async Task<List<InboxCandidate>> AnnouncementsAsync(
        VibeChatDbContext db,
        IReadOnlyDictionary<ChannelId, ChannelAccess> channels,
        ChannelId[] channelIds,
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from announcement in db.Announcements.AsNoTracking()
            join message in db.Messages.AsNoTracking() on announcement.MessageId equals message.Id
            where channelIds.Contains(announcement.ChannelId)
                && announcement.RequiresAcknowledgement
                && announcement.ClosedAt == null
                && (announcement.AcknowledgeBy == null || announcement.AcknowledgeBy > now)
                && message.DeletedAt == null
                && message.ConversationId == announcement.ChannelId
                && !db.AnnouncementAcknowledgements.Any(ack =>
                    ack.MessageId == announcement.MessageId && ack.UserId == userId)
            orderby announcement.CreatedAt descending
            select new
            {
                announcement.MessageId,
                announcement.ChannelId,
                announcement.CreatedAt,
                message.Sequence,
                message.Body,
            }).Take(InboxPolicies.MaxItemsPerKind).ToListAsync(cancellationToken);

        var items = new List<InboxCandidate>();
        foreach (var row in rows)
        {
            if (!channels.TryGetValue(row.ChannelId, out var channel) || row.Sequence <= channel.JoinedSeq)
            {
                continue;
            }

            items.Add(Candidate(
                InboxItemKind.Announcement,
                row.MessageId.Value,
                channel,
                row.MessageId.Value,
                threadId: null,
                row.Sequence,
                InboxPolicies.Preview(row.Body),
                messageRemoved: false,
                row.CreatedAt));
        }

        return items;
    }

    private static async Task<List<InboxCandidate>> MentionsAsync(
        VibeChatDbContext db,
        IReadOnlyDictionary<ChannelId, ChannelAccess> channels,
        ChannelId[] channelIds,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from mention in db.MessageMentions.AsNoTracking()
            join message in db.Messages.AsNoTracking() on mention.MessageId equals message.Id
            where mention.MentionedUserId == userId
                && channelIds.Contains(mention.ChannelId)
                && message.DeletedAt == null
                && message.AuthorId != userId
                && message.ConversationId == mention.ChannelId
                && message.Sequence > (db.ReadCursors
                    .Where(cursor => cursor.UserId == userId && cursor.ChannelId == mention.ChannelId)
                    .Select(cursor => (long?)cursor.LastReadSequence)
                    .FirstOrDefault() ?? 0)
            orderby message.CreatedAt descending
            select new
            {
                mention.ChannelId,
                message.Id,
                message.Sequence,
                message.Body,
                message.CreatedAt,
            }).Take(InboxPolicies.MaxItemsPerKind).ToListAsync(cancellationToken);

        var items = new List<InboxCandidate>();
        foreach (var row in rows)
        {
            if (!channels.TryGetValue(row.ChannelId, out var channel) || row.Sequence <= channel.JoinedSeq)
            {
                continue;
            }

            items.Add(Candidate(
                InboxItemKind.Mention,
                row.Id.Value,
                channel,
                row.Id.Value,
                threadId: null,
                row.Sequence,
                InboxPolicies.Preview(row.Body),
                messageRemoved: false,
                row.CreatedAt));
        }

        return items;
    }

    private static async Task<List<InboxCandidate>> DirectsAsync(
        VibeChatDbContext db,
        IReadOnlyList<ChannelAccess> channels,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var directs = channels
            .Where(channel => channel.Type is ChannelType.Direct or ChannelType.GroupDm)
            .ToList();
        if (directs.Count == 0)
        {
            return [];
        }

        var ids = directs.Select(channel => channel.Id).ToArray();
        var rows = await (
            from message in db.Messages.AsNoTracking()
            where ids.Contains(message.ConversationId)
                && message.DeletedAt == null
                && message.AuthorId != userId
                && message.Sequence > (db.ReadCursors
                    .Where(cursor => cursor.UserId == userId && cursor.ChannelId == message.ConversationId)
                    .Select(cursor => (long?)cursor.LastReadSequence)
                    .FirstOrDefault() ?? 0)
            orderby message.CreatedAt descending
            select new
            {
                message.ConversationId,
                message.Id,
                message.Sequence,
                message.Body,
                message.CreatedAt,
            }).Take(InboxPolicies.MaxItemsPerKind * 5).ToListAsync(cancellationToken);

        var items = new List<InboxCandidate>();
        foreach (var channel in directs)
        {
            var visible = rows
                .Where(row => row.ConversationId == channel.Id && row.Sequence > channel.JoinedSeq)
                .ToList();
            if (visible.Count == 0)
            {
                continue;
            }

            var latest = visible.OrderByDescending(row => row.Sequence).First();
            items.Add(Candidate(
                InboxItemKind.Direct,
                channel.Id.Value,
                channel,
                latest.Id.Value,
                threadId: null,
                latest.Sequence,
                InboxPolicies.Preview(latest.Body),
                messageRemoved: false,
                channel.CreatedAt));
        }

        return items;
    }

    private static async Task<List<InboxCandidate>> ThreadsAsync(
        VibeChatDbContext db,
        IReadOnlyDictionary<ChannelId, ChannelAccess> channels,
        WorkspaceId workspaceId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var subscriptions = await (
            from subscription in db.ThreadSubscriptions.AsNoTracking()
            join channel in db.Channels.AsNoTracking() on subscription.ChannelId equals channel.Id
            where subscription.UserId == userId && channel.WorkspaceId == workspaceId
            select new
            {
                subscription.ThreadId,
                subscription.ChannelId,
                subscription.LastReadSeq,
                subscription.CreatedAt,
            }).ToListAsync(cancellationToken);

        var visible = subscriptions
            .Where(row => channels.ContainsKey(row.ChannelId))
            .Take(InboxPolicies.MaxItemsPerKind)
            .ToList();
        if (visible.Count == 0)
        {
            return [];
        }

        var threadIds = visible.Select(row => row.ThreadId).ToArray();
        var conversationIds = threadIds.Select(id => new ChannelId(id)).ToArray();
        var sequences = await db.ConversationSequences.AsNoTracking()
            .Where(row => conversationIds.Contains(row.ConversationId))
            .ToDictionaryAsync(row => row.ConversationId.Value, row => row.LastSequence, cancellationToken);

        var items = new List<InboxCandidate>();
        foreach (var row in visible)
        {
            var current = sequences.TryGetValue(row.ThreadId, out var sequence) ? sequence : 0L;
            if (current <= row.LastReadSeq || !channels.TryGetValue(row.ChannelId, out var channel))
            {
                continue;
            }

            items.Add(Candidate(
                InboxItemKind.Thread,
                row.ThreadId,
                channel,
                messageId: null,
                row.ThreadId,
                current,
                bodyPreview: string.Empty,
                messageRemoved: false,
                row.CreatedAt));
        }

        return items;
    }

    private static async Task<List<InboxCandidate>> TopicsAsync(
        VibeChatDbContext db,
        IReadOnlyDictionary<ChannelId, ChannelAccess> channels,
        WorkspaceId workspaceId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var follows = await (
            from follow in db.TopicSubscriptions.AsNoTracking()
            join topic in db.ChannelTopics.AsNoTracking() on follow.TopicId equals topic.Id
            join channel in db.Channels.AsNoTracking() on topic.ChannelId equals channel.Id
            where follow.UserId == userId && channel.WorkspaceId == workspaceId
            select new
            {
                topic.Id,
                topic.ChannelId,
                topic.Title,
                topic.LastActivityAt,
            }).Take(InboxPolicies.MaxItemsPerKind).ToListAsync(cancellationToken);

        var visible = follows.Where(row => channels.ContainsKey(row.ChannelId)).ToList();
        if (visible.Count == 0)
        {
            return [];
        }

        var conversationIds = visible.Select(row => new ChannelId(row.Id)).ToArray();
        var sequences = await db.ConversationSequences.AsNoTracking()
            .Where(row => conversationIds.Contains(row.ConversationId))
            .ToDictionaryAsync(row => row.ConversationId.Value, row => row.LastSequence, cancellationToken);
        var cursors = await db.ReadCursors.AsNoTracking()
            .Where(row => row.UserId == userId && conversationIds.Contains(row.ChannelId))
            .ToDictionaryAsync(row => row.ChannelId.Value, row => row.LastReadSequence, cancellationToken);

        var items = new List<InboxCandidate>();
        foreach (var row in visible)
        {
            var current = sequences.TryGetValue(row.Id, out var sequence) ? sequence : 0L;
            var read = cursors.TryGetValue(row.Id, out var cursor) ? cursor : 0L;
            if (current <= read || !channels.TryGetValue(row.ChannelId, out var channel))
            {
                continue;
            }

            items.Add(new InboxCandidate(
                InboxItemKind.Topic,
                row.Id,
                channel.Id.Value,
                channel.DisplayName,
                MessageId: null,
                ThreadId: null,
                current,
                InboxPolicies.Preview(row.Title),
                MessageRemoved: false,
                row.LastActivityAt,
                SnoozedUntil: null,
                TopicId: row.Id));
        }

        return items;
    }

    private static async Task<List<InboxCandidate>> SavedAsync(
        VibeChatDbContext db,
        IReadOnlyDictionary<ChannelId, ChannelAccess> channels,
        WorkspaceId workspaceId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from saved in db.SavedMessages.AsNoTracking()
            join channel in db.Channels.AsNoTracking() on saved.ChannelId equals channel.Id
            join message in db.Messages.AsNoTracking() on saved.MessageId equals message.Id
            where saved.UserId == userId
                && saved.CompletedAt == null
                && channel.WorkspaceId == workspaceId
            orderby saved.CreatedAt descending
            select new
            {
                saved.MessageId,
                saved.ChannelId,
                saved.CreatedAt,
                message.Sequence,
                message.Body,
                message.DeletedAt,
                message.ConversationId,
            }).Take(InboxPolicies.MaxItemsPerKind).ToListAsync(cancellationToken);

        var items = new List<InboxCandidate>();
        foreach (var row in rows)
        {
            if (!channels.TryGetValue(row.ChannelId, out var channel))
            {
                continue;
            }

            if (row.ConversationId == row.ChannelId && row.Sequence <= channel.JoinedSeq)
            {
                continue;
            }

            var removed = row.DeletedAt is not null;
            items.Add(Candidate(
                InboxItemKind.Saved,
                row.MessageId.Value,
                channel,
                row.MessageId.Value,
                threadId: null,
                row.Sequence,
                removed ? string.Empty : InboxPolicies.Preview(row.Body),
                removed,
                row.CreatedAt));
        }

        return items;
    }

    private static async Task<Dictionary<ChannelId, string>> DirectNamesAsync(
        VibeChatDbContext db,
        IReadOnlyList<ChannelAccess> channels,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var directIds = channels
            .Where(channel => channel.Type == ChannelType.Direct)
            .Select(channel => channel.Id)
            .ToArray();
        if (directIds.Length == 0)
        {
            return [];
        }

        var peers = await (
            from member in db.ChannelMembers.AsNoTracking()
            join profile in db.UserProfiles.AsNoTracking() on member.UserId equals profile.Id
            where directIds.Contains(member.ChannelId)
                && member.UserId != userId
                && member.LeftAt == null
            select new { member.ChannelId, profile.DisplayName }).ToListAsync(cancellationToken);

        return peers
            .GroupBy(row => row.ChannelId)
            .ToDictionary(group => group.Key, group => group.First().DisplayName);
    }

    private static string ChannelLabel(ChannelAccess channel)
    {
        if (!string.IsNullOrWhiteSpace(channel.Title))
        {
            return channel.Title;
        }

        return string.IsNullOrWhiteSpace(channel.Name) ? string.Empty : channel.Name;
    }

    private static InboxCandidate Candidate(
        InboxItemKind kind,
        Guid sourceId,
        ChannelAccess channel,
        Guid? messageId,
        Guid? threadId,
        long? sequence,
        string bodyPreview,
        bool messageRemoved,
        DateTimeOffset sortAt) =>
        new(
            kind,
            sourceId,
            channel.Id.Value,
            channel.DisplayName,
            messageId,
            threadId,
            sequence,
            bodyPreview,
            messageRemoved,
            sortAt,
            SnoozedUntil: null);

    private static async Task<List<InboxCandidate>> CommitmentsAsync(
        VibeChatDbContext db,
        IReadOnlyDictionary<ChannelId, ChannelAccess> channels,
        WorkspaceId workspaceId,
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await db.Commitments.AsNoTracking()
            .Where(row => row.WorkspaceId == workspaceId
                && row.OwnerUserId == userId
                && row.State == CommitmentState.Open)
            .OrderByDescending(row => row.UpdatedAt)
            .Take(InboxPolicies.MaxItemsPerKind)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var preference = await db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var globalLevel = preference?.Level ?? NotificationLevel.MentionsAndDms;
        var hidePreview = preference?.HidePreview == true;
        var dnd = preference is not null && PushDispatchPolicies.IsWithinDnd(
            preference.DndEnabled,
            preference.DndStart,
            preference.DndEnd,
            preference.DndDays,
            preference.TimeZone,
            now);
        var channelIds = rows.Select(row => row.ChannelId).Distinct().ToArray();
        var overrides = await db.ChannelNotificationPreferences.AsNoTracking()
            .Where(row => row.UserId == userId && channelIds.Contains(row.ChannelId))
            .ToListAsync(cancellationToken);

        var items = new List<InboxCandidate>();
        foreach (var row in rows)
        {
            if (!channels.TryGetValue(row.ChannelId, out var channel))
            {
                continue;
            }

            if (!CommitmentPolicies.CanSee(row, userId, canReadChannel: true))
            {
                continue;
            }

            var channelOverride = overrides.FirstOrDefault(item => item.ChannelId == row.ChannelId);
            (NotificationLevel Level, DateTimeOffset? MutedUntil)? activeOverride = channelOverride?.Level is NotificationLevel level
                ? (level, channelOverride.MutedUntil)
                : null;
            var effective = PushDispatchPolicies.ResolveEffectiveLevel(globalLevel, activeOverride, now);
            if (dnd || !PushDispatchPolicies.ShouldNotifyForLevel(effective, isDirect: false, isMentioned: true, isAuthor: false))
            {
                continue;
            }

            items.Add(Candidate(
                InboxItemKind.Commitment,
                row.Id,
                channel,
                row.SourceMessageId.Value,
                row.SourceThreadId,
                row.SourceVersion,
                hidePreview || row.SourceRedacted ? string.Empty : InboxPolicies.Preview(row.Title),
                row.SourceRedacted,
                row.UpdatedAt));
        }

        return items;
    }

    private static async Task<List<InboxCandidate>> TasksAsync(
        VibeChatDbContext db,
        IReadOnlyDictionary<ChannelId, ChannelAccess> channels,
        WorkspaceId workspaceId,
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var assignedIds = await db.MessageTaskAssignees.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => row.TaskId)
            .Take(InboxPolicies.MaxItemsPerKind)
            .ToListAsync(cancellationToken);
        if (assignedIds.Count == 0)
        {
            return [];
        }

        var rows = await db.MessageTasks.AsNoTracking()
            .Where(row => row.WorkspaceId == workspaceId
                && assignedIds.Contains(row.Id)
                && row.State != TaskState.Completed)
            .OrderByDescending(row => row.UpdatedAt)
            .Take(InboxPolicies.MaxItemsPerKind)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var preference = await db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var globalLevel = preference?.Level ?? NotificationLevel.MentionsAndDms;
        var hidePreview = preference?.HidePreview == true;
        var dnd = preference is not null && PushDispatchPolicies.IsWithinDnd(
            preference.DndEnabled,
            preference.DndStart,
            preference.DndEnd,
            preference.DndDays,
            preference.TimeZone,
            now);
        var channelIds = rows.Select(row => row.ChannelId).Distinct().ToArray();
        var overrides = await db.ChannelNotificationPreferences.AsNoTracking()
            .Where(row => row.UserId == userId && channelIds.Contains(row.ChannelId))
            .ToListAsync(cancellationToken);

        var items = new List<InboxCandidate>();
        foreach (var row in rows)
        {
            if (!channels.TryGetValue(row.ChannelId, out var channel))
            {
                continue;
            }

            if (!TaskPolicies.CanSee(row, userId, canReadChannel: true, isAssignee: true))
            {
                continue;
            }

            var channelOverride = overrides.FirstOrDefault(item => item.ChannelId == row.ChannelId);
            (NotificationLevel Level, DateTimeOffset? MutedUntil)? activeOverride = channelOverride?.Level is NotificationLevel level
                ? (level, channelOverride.MutedUntil)
                : null;
            var effective = PushDispatchPolicies.ResolveEffectiveLevel(globalLevel, activeOverride, now);
            if (dnd || !PushDispatchPolicies.ShouldNotifyForLevel(effective, isDirect: false, isMentioned: true, isAuthor: false))
            {
                continue;
            }

            items.Add(Candidate(
                InboxItemKind.Task,
                row.Id,
                channel,
                row.SourceMessageId.Value,
                row.SourceThreadId,
                row.SourceVersion,
                hidePreview || row.SourceRedacted ? string.Empty : InboxPolicies.Preview(row.Title),
                row.SourceRedacted,
                row.UpdatedAt));
        }

        return items;
    }

    private sealed class ChannelAccess
    {
        public ChannelId Id { get; init; }
        public ChannelType Type { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Title { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public long JoinedSeq { get; init; }
        public long LastRead { get; init; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
