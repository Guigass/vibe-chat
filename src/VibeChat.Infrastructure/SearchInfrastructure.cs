using Microsoft.EntityFrameworkCore;
using VibeChat.Conversations;
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

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE messaging.messages
            SET search_vector = to_tsvector({SearchPolicies.TextConfig}, coalesce("Body", ''))
            WHERE "Id" = {doc.MessageId.Value} AND "TenantId" = {doc.TenantId.Value} AND "DeletedAt" IS NULL
            """,
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
        if (term.Length < SearchPolicies.MinTermLength)
        {
            return new SearchResultPage(term, [], limit);
        }

        var channelFilter = query.ChannelId;
        const string config = SearchPolicies.TextConfig;

        var candidateQuery =
            from message in dbContext.Messages.AsNoTracking()
            join channel in dbContext.Channels.AsNoTracking() on message.ConversationId equals channel.Id
            where message.TenantId == query.TenantId
                && channel.TenantId == query.TenantId
                && channel.WorkspaceId == query.WorkspaceId
                && message.DeletedAt == null
                && (channelFilter == null || message.ConversationId == channelFilter)
                && EF.Functions.ToTsVector(config, message.Body)
                    .Matches(EF.Functions.PlainToTsQuery(config, term))
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
            select new
            {
                message.Id,
                ChannelId = channel.Id,
                ChannelName = channel.Name,
                ChannelType = channel.Type,
                message.Sequence,
                message.AuthorId,
                message.Body,
                message.CreatedAt,
                Rank = EF.Functions.ToTsVector(config, message.Body)
                    .Rank(EF.Functions.PlainToTsQuery(config, term))
            };

        var rows = await candidateQuery
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

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

        return new SearchResultPage(term, items, limit);
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
