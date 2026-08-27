using Microsoft.EntityFrameworkCore;
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

public sealed class PostgresSearchQuery(VibeChatDbContext dbContext) : ISearchQuery
{
    public async Task<SearchResultPage> SearchMessagesAsync(SearchMessagesQuery query, CancellationToken cancellationToken)
    {
        var term = SearchPolicies.NormalizeTerm(query.Term);
        var limit = SearchPolicies.NormalizeLimit(query.Limit);
        var hasTerm = term.Length >= SearchPolicies.MinTermLength;
        if (!hasTerm && !SearchPolicies.HasStructuredFilter(query))
        {
            return new SearchResultPage(term, [], limit);
        }

        var channelFilter = query.ChannelId;
        var authorFilter = query.AuthorId;
        var createdFrom = query.From;
        var createdTo = query.To;
        const string config = SearchPolicies.TextConfig;

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
                            && cm.UserId == query.UserId)
                    )
                )
            select new { message, channel };

        if (hasTerm)
        {
            joined = joined.Where(x =>
                EF.Functions.ToTsVector(config, x.message.Body)
                    .Matches(EF.Functions.PlainToTsQuery(config, term)));
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
                Rank = EF.Functions.ToTsVector(config, x.message.Body)
                    .Rank(EF.Functions.PlainToTsQuery(config, term))
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
            SearchPolicies.BuildPreview(row.Body),
            row.CreatedAt,
            row.Rank)).ToArray();

        return new SearchResultPage(term, items, limit, total, nextCursor);
    }

    private static string FormatChannelName(string name, string type)
    {
        if (string.Equals(type, nameof(ChannelType.Direct), StringComparison.OrdinalIgnoreCase))
        {
            return name.StartsWith("dm:", StringComparison.OrdinalIgnoreCase) ? "DM" : name;
        }

        return name;
    }
}
