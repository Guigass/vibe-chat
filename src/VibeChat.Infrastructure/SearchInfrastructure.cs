using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Search;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed class PostgresSearchIndexer(VibeChatDbContext dbContext) : ISearchIndexer
{
    public async Task IndexMessageAsync(MessageIndexed doc, CancellationToken cancellationToken)
    {
        if (doc.IsDeleted)
        {
            await RemoveMessageAsync(doc.TenantId, doc.MessageId, cancellationToken);
            return;
        }

        // TextConfig must be a SQL literal (regconfig). Parameterizing it as text makes
        // Postgres look for to_tsvector(text, varchar), which does not exist.
        var config = SearchPolicies.TextConfig;
        var sql =
            $$"""
            UPDATE messaging.messages
            SET search_vector = to_tsvector('{{config}}'::regconfig, coalesce("Body", ''))
            WHERE "Id" = {0} AND "TenantId" = {1} AND "DeletedAt" IS NULL
            """;
        await dbContext.Database.ExecuteSqlRawAsync(
            sql,
            new object[] { doc.MessageId.Value, doc.TenantId.Value },
            cancellationToken);
    }

    public Task RemoveMessageAsync(TenantId tenantId, MessageId messageId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE messaging.messages
            SET search_vector = NULL
            WHERE "Id" = {messageId.Value} AND "TenantId" = {tenantId.Value}
            """,
            cancellationToken);
}

/// <summary>SQL helpers for B-188. Translated by EF; not for in-process calls.</summary>
public static class SearchSql
{
    public static NpgsqlTsQuery VersatileQuery(string config, string raw) =>
        throw new InvalidOperationException("SearchSql.VersatileQuery is translated to SQL.");

    public static float PhraseBoost(string body, string raw) =>
        throw new InvalidOperationException("SearchSql.PhraseBoost is translated to SQL.");

    public static string PlainText(string raw) =>
        throw new InvalidOperationException("SearchSql.PlainText is translated to SQL.");

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(typeof(SearchSql).GetMethod(nameof(VersatileQuery))!)
            .HasName("versatile_tsquery")
            .HasSchema("messaging");
        modelBuilder.HasDbFunction(typeof(SearchSql).GetMethod(nameof(PhraseBoost))!)
            .HasName("phrase_boost")
            .HasSchema("messaging");
        modelBuilder.HasDbFunction(typeof(SearchSql).GetMethod(nameof(PlainText))!)
            .HasName("plain_search_text")
            .HasSchema("messaging");
    }
}

public sealed class PostgresSearchQuery(VibeChatDbContext dbContext) : ISearchQuery
{
    public async Task<SearchResultPage> SearchMessagesAsync(SearchMessagesQuery query, CancellationToken cancellationToken)
    {
        var term = SearchPolicies.NormalizeTerm(query.Term);
        var limit = SearchPolicies.NormalizeLimit(query.Limit);
        var hasTerm = term.Length >= SearchPolicies.MinTermLength;
        if (!hasTerm && !SearchPolicies.HasStructuredFilter(query))
        {
            return Empty(term, limit);
        }

        var channelFilter = query.ChannelId;
        var authorFilter = query.AuthorId;
        var createdFrom = query.From;
        var createdTo = query.To;
        const string config = SearchPolicies.TextConfig;
        var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
        var monthAgo = DateTimeOffset.UtcNow.AddDays(-30);
        var headlineOptions =
            $"StartSel={SearchPolicies.HeadlineStart}, StopSel={SearchPolicies.HeadlineStop}, MaxFragments=1, MaxWords=28, MinWords=6, ShortWord=2";

        var joined =
            from message in dbContext.Messages.AsNoTracking()
            from thread in dbContext.MessageThreads.AsNoTracking()
                .Where(t => message.ThreadId != null && t.Id == message.ThreadId.Value)
                .DefaultIfEmpty()
            join channel in dbContext.Channels.AsNoTracking()
                on (thread != null ? thread.ChannelId : message.ConversationId) equals channel.Id
            where message.TenantId == query.TenantId
                && channel.TenantId == query.TenantId
                && channel.WorkspaceId == query.WorkspaceId
                && message.DeletedAt == null
                && (channelFilter == null || channel.Id == channelFilter)
                && (authorFilter == null || message.AuthorId == authorFilter)
                && (createdFrom == null || message.CreatedAt >= createdFrom)
                && (createdTo == null || message.CreatedAt <= createdTo)
                && (
                    (
                        (channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement)
                        && dbContext.WorkspaceMembers.Any(wm =>
                            wm.TenantId == query.TenantId
                            && wm.WorkspaceId == channel.WorkspaceId
                            && wm.UserId == query.UserId)
                    )
                    || (
                        channel.Type != ChannelType.Public
                        && channel.Type != ChannelType.Announcement
                        && dbContext.ChannelMembers.Any(cm =>
                            cm.TenantId == query.TenantId
                            && cm.ChannelId == channel.Id
                            && cm.UserId == query.UserId
                            && (channel.Type != ChannelType.GroupDm || message.Sequence > cm.JoinedSeq))
                    )
                )
            select new { message, channel };

        if (hasTerm)
        {
            joined = joined.Where(x =>
                EF.Property<NpgsqlTsVector>(x.message, "SearchVector")
                    .Matches(SearchSql.VersatileQuery(config, term)));
        }

        if (query.HasAttachment == true)
        {
            joined = joined.Where(x => dbContext.Attachments.Any(a =>
                a.TenantId == query.TenantId
                && a.MessageId == x.message.Id
                && a.Status == AttachmentStatus.Ready));
        }
        else if (query.HasAttachment == false)
        {
            joined = joined.Where(x => !dbContext.Attachments.Any(a =>
                a.TenantId == query.TenantId
                && a.MessageId == x.message.Id
                && a.Status == AttachmentStatus.Ready));
        }

        if (query.HasLink == true)
        {
            joined = joined.Where(x =>
                x.message.Body.Contains("http://")
                || x.message.Body.Contains("https://")
                || dbContext.MessageLinkPreviews.Any(p =>
                    p.TenantId == query.TenantId
                    && p.MessageId == x.message.Id
                    && p.RemovedAt == null));
        }
        else if (query.HasLink == false)
        {
            joined = joined.Where(x =>
                !x.message.Body.Contains("http://")
                && !x.message.Body.Contains("https://")
                && !dbContext.MessageLinkPreviews.Any(p =>
                    p.TenantId == query.TenantId
                    && p.MessageId == x.message.Id
                    && p.RemovedAt == null));
        }

        var attachmentKind = query.AttachmentKind?.Trim().ToLowerInvariant();
        if (attachmentKind == "audio")
        {
            joined = joined.Where(x => dbContext.Attachments.Any(a =>
                a.TenantId == query.TenantId
                && a.MessageId == x.message.Id
                && a.Status == AttachmentStatus.Ready
                && a.Kind == AttachmentKind.Audio));
        }
        else if (attachmentKind == "image")
        {
            joined = joined.Where(x => dbContext.Attachments.Any(a =>
                a.TenantId == query.TenantId
                && a.MessageId == x.message.Id
                && a.Status == AttachmentStatus.Ready
                && a.ContentType.StartsWith("image/")));
        }
        else if (attachmentKind == "document")
        {
            joined = joined.Where(x => dbContext.Attachments.Any(a =>
                a.TenantId == query.TenantId
                && a.MessageId == x.message.Id
                && a.Status == AttachmentStatus.Ready
                && a.Kind == AttachmentKind.File
                && !a.ContentType.StartsWith("image/")));
        }

        var total = await joined.CountAsync(cancellationToken);

        SearchPageCursor? pageCursor = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor)
            && SearchCursorCodec.TryDecode(query.Cursor, out var decodedCursor))
        {
            pageCursor = decodedCursor;
            var cursorCreated = decodedCursor.CreatedAt;
            if (query.Sort == SearchSort.Date)
            {
                joined = joined.Where(x => x.message.CreatedAt < cursorCreated);
            }
        }

        var candidateQuery = hasTerm
            ? joined.Select(x => new
            {
                x.message.Id,
                ChannelId = x.channel.Id,
                ChannelName = x.channel.Name,
                ChannelType = x.channel.Type,
                x.message.Sequence,
                x.message.AuthorId,
                x.message.Body,
                x.message.CreatedAt,
                Headline = SearchSql.VersatileQuery(config, term).GetResultHeadline(config, x.message.Body, headlineOptions),
                Rank = EF.Property<NpgsqlTsVector>(x.message, "SearchVector").Rank(SearchSql.VersatileQuery(config, term))
                    + SearchSql.PhraseBoost(x.message.Body, term)
                    + (x.message.CreatedAt >= weekAgo ? 0.15f : x.message.CreatedAt >= monthAgo ? 0.05f : 0f)
            })
            : joined.Select(x => new
            {
                x.message.Id,
                ChannelId = x.channel.Id,
                ChannelName = x.channel.Name,
                ChannelType = x.channel.Type,
                x.message.Sequence,
                x.message.AuthorId,
                x.message.Body,
                x.message.CreatedAt,
                Headline = x.message.Body,
                Rank = 0f
            });

        if (pageCursor is not null && query.Sort != SearchSort.Date)
        {
            var cursorRank = (float)pageCursor.Rank;
            var cursorCreated = pageCursor.CreatedAt;
            candidateQuery = candidateQuery.Where(x =>
                x.Rank < cursorRank
                || (x.Rank == cursorRank && x.CreatedAt < cursorCreated));
        }

        var rows = query.Sort == SearchSort.Date
            ? await candidateQuery
                .OrderByDescending(x => x.CreatedAt)
                .Take(limit + 1)
                .ToListAsync(cancellationToken)
            : await candidateQuery
                .OrderByDescending(x => x.Rank)
                .ThenByDescending(x => x.CreatedAt)
                .Take(limit + 1)
                .ToListAsync(cancellationToken);

        string? nextCursor = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1];
            nextCursor = SearchCursorCodec.Encode(new SearchPageCursor(query.Sort, last.Rank, last.CreatedAt, last.Id.Value));
            rows = rows.Take(limit).ToList();
        }

        var authorIds = rows.Select(x => x.AuthorId).Distinct().ToArray();
        var authors = await dbContext.UserProfiles.AsNoTracking()
            .Where(x => authorIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);

        var items = rows.Select(row => new SearchMessageHit(
            row.Id.Value,
            row.ChannelId.Value,
            FormatChannelName(row.ChannelName, row.ChannelType.ToString()),
            row.ChannelType.ToString(),
            row.Sequence,
            row.AuthorId.Value,
            authors.TryGetValue(row.AuthorId, out var name) ? name : row.AuthorId.Value.ToString("D"),
            hasTerm && !string.IsNullOrWhiteSpace(row.Headline)
                ? row.Headline
                : SearchPolicies.BuildPreview(row.Body),
            row.CreatedAt,
            row.Rank)).ToArray();

        if (!hasTerm || !string.IsNullOrWhiteSpace(query.Cursor) || !SearchQuerySyntax.HasPositiveTerm(term))
        {
            return new SearchResultPage(term, items, limit, total, nextCursor, [], [], []);
        }

        var channels = await SearchChannelsAsync(query, config, term, limit, cancellationToken);
        var people = await SearchPeopleAsync(query, config, term, limit, cancellationToken);
        var attachments = IncludeAttachmentHits(query)
            ? await SearchAttachmentsAsync(query, config, term, limit, cancellationToken)
            : [];

        return new SearchResultPage(term, items, limit, total, nextCursor, channels, people, attachments);
    }

    private async Task<SearchChannelHit[]> SearchChannelsAsync(
        SearchMessagesQuery query,
        string config,
        string term,
        int limit,
        CancellationToken cancellationToken)
    {
        var channelFilter = query.ChannelId;
        var rows = await VisibleChannels(query)
            .Where(channel =>
                channel.Type != ChannelType.Direct
                && (channelFilter == null || channel.Id == channelFilter)
                && (
                    (channel.Type != ChannelType.GroupDm
                        && EF.Functions.ToTsVector(config, SearchSql.PlainText(channel.Name)).Matches(SearchSql.VersatileQuery(config, term)))
                    || (channel.Type == ChannelType.GroupDm
                        && channel.Title != null
                        && EF.Functions.ToTsVector(config, SearchSql.PlainText(channel.Title)).Matches(SearchSql.VersatileQuery(config, term)))
                ))
            .Select(channel => new
            {
                channel.Id,
                channel.Name,
                channel.Title,
                channel.Type,
                Rank = channel.Type == ChannelType.GroupDm
                    ? EF.Functions.ToTsVector(config, SearchSql.PlainText(channel.Title ?? channel.Name)).Rank(SearchSql.VersatileQuery(config, term))
                    : EF.Functions.ToTsVector(config, SearchSql.PlainText(channel.Name)).Rank(SearchSql.VersatileQuery(config, term))
            })
            .OrderByDescending(x => x.Rank)
            .ThenBy(x => x.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new SearchChannelHit(
            row.Id.Value,
            FormatChannelName(row.Type == ChannelType.GroupDm && !string.IsNullOrWhiteSpace(row.Title) ? row.Title! : row.Name, row.Type.ToString()),
            row.Type.ToString(),
            row.Rank)).ToArray();
    }

    private async Task<SearchPersonHit[]> SearchPeopleAsync(
        SearchMessagesQuery query,
        string config,
        string term,
        int limit,
        CancellationToken cancellationToken)
    {
        var authorFilter = query.AuthorId;
        var rows = await (
            from member in dbContext.WorkspaceMembers.AsNoTracking()
            join profile in dbContext.UserProfiles.AsNoTracking() on member.UserId equals profile.Id
            where member.TenantId == query.TenantId
                && member.WorkspaceId == query.WorkspaceId
                && member.UserId != query.UserId
                && (authorFilter == null || member.UserId == authorFilter)
                && EF.Functions.ToTsVector(config, SearchSql.PlainText(profile.DisplayName)).Matches(SearchSql.VersatileQuery(config, term))
            select new
            {
                profile.Id,
                profile.DisplayName,
                Rank = EF.Functions.ToTsVector(config, SearchSql.PlainText(profile.DisplayName)).Rank(SearchSql.VersatileQuery(config, term))
            })
            .OrderByDescending(x => x.Rank)
            .ThenBy(x => x.DisplayName)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new SearchPersonHit(row.Id.Value, row.DisplayName, row.Rank)).ToArray();
    }

    private async Task<SearchAttachmentHit[]> SearchAttachmentsAsync(
        SearchMessagesQuery query,
        string config,
        string term,
        int limit,
        CancellationToken cancellationToken)
    {
        var channelFilter = query.ChannelId;
        var authorFilter = query.AuthorId;
        var createdFrom = query.From;
        var createdTo = query.To;
        var attachmentKind = query.AttachmentKind?.Trim().ToLowerInvariant();

        var rows = await (
            from attachment in dbContext.Attachments.AsNoTracking()
            join message in dbContext.Messages.AsNoTracking()
                on attachment.MessageId equals (MessageId?)message.Id
            from thread in dbContext.MessageThreads.AsNoTracking()
                .Where(t => message.ThreadId != null && t.Id == message.ThreadId.Value)
                .DefaultIfEmpty()
            join channel in dbContext.Channels.AsNoTracking()
                on (thread != null ? thread.ChannelId : message.ConversationId) equals channel.Id
            where attachment.TenantId == query.TenantId
                && attachment.Status == AttachmentStatus.Ready
                && attachment.MessageId != null
                && message.TenantId == query.TenantId
                && message.DeletedAt == null
                && channel.TenantId == query.TenantId
                && channel.WorkspaceId == query.WorkspaceId
                && (channelFilter == null || channel.Id == channelFilter)
                && (authorFilter == null || message.AuthorId == authorFilter)
                && (createdFrom == null || message.CreatedAt >= createdFrom)
                && (createdTo == null || message.CreatedAt <= createdTo)
                && (attachmentKind != "audio" || attachment.Kind == AttachmentKind.Audio)
                && (attachmentKind != "image" || attachment.ContentType.StartsWith("image/"))
                && (attachmentKind != "document" || (attachment.Kind == AttachmentKind.File && !attachment.ContentType.StartsWith("image/")))
                && EF.Functions.ToTsVector(config, SearchSql.PlainText(attachment.FileName)).Matches(SearchSql.VersatileQuery(config, term))
                && (
                    (
                        (channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement)
                        && dbContext.WorkspaceMembers.Any(wm =>
                            wm.TenantId == query.TenantId
                            && wm.WorkspaceId == channel.WorkspaceId
                            && wm.UserId == query.UserId)
                    )
                    || (
                        channel.Type != ChannelType.Public
                        && channel.Type != ChannelType.Announcement
                        && dbContext.ChannelMembers.Any(cm =>
                            cm.TenantId == query.TenantId
                            && cm.ChannelId == channel.Id
                            && cm.UserId == query.UserId
                            && (channel.Type != ChannelType.GroupDm || message.Sequence > cm.JoinedSeq))
                    )
                )
            select new
            {
                attachment.Id,
                attachment.FileName,
                MessageId = message.Id,
                ChannelId = channel.Id,
                ChannelName = channel.Name,
                ChannelType = channel.Type,
                message.Sequence,
                Rank = EF.Functions.ToTsVector(config, SearchSql.PlainText(attachment.FileName)).Rank(SearchSql.VersatileQuery(config, term))
            })
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => x.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new SearchAttachmentHit(
            row.Id,
            row.FileName,
            row.MessageId.Value,
            row.ChannelId.Value,
            FormatChannelName(row.ChannelName, row.ChannelType.ToString()),
            row.ChannelType.ToString(),
            row.Sequence,
            row.Rank)).ToArray();
    }

    private IQueryable<Channel> VisibleChannels(SearchMessagesQuery query) =>
        dbContext.Channels.AsNoTracking().Where(channel =>
            channel.TenantId == query.TenantId
            && channel.WorkspaceId == query.WorkspaceId
            && (
                (
                    (channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement)
                    && dbContext.WorkspaceMembers.Any(wm =>
                        wm.TenantId == query.TenantId
                        && wm.WorkspaceId == channel.WorkspaceId
                        && wm.UserId == query.UserId)
                )
                || (
                    channel.Type != ChannelType.Public
                    && channel.Type != ChannelType.Announcement
                    && dbContext.ChannelMembers.Any(cm =>
                        cm.TenantId == query.TenantId
                        && cm.ChannelId == channel.Id
                        && cm.UserId == query.UserId)
                )
            ));

    private static bool IncludeAttachmentHits(SearchMessagesQuery query)
    {
        if (query.HasAttachment == false)
        {
            return false;
        }

        if (query.HasLink == true && query.HasAttachment != true && string.IsNullOrWhiteSpace(query.AttachmentKind))
        {
            return false;
        }

        return true;
    }

    private static SearchResultPage Empty(string term, int limit) =>
        new(term, [], limit, 0, null, [], [], []);

    private static string FormatChannelName(string name, string type)
    {
        if (string.Equals(type, nameof(ChannelType.Direct), StringComparison.OrdinalIgnoreCase))
        {
            return name.StartsWith("dm:", StringComparison.OrdinalIgnoreCase) ? "DM" : name;
        }

        return name;
    }
}
