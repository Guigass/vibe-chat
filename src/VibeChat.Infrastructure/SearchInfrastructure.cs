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

        var tsQuery = EF.Functions.PlainToTsQuery(SearchPolicies.TextConfig, term);
        var channelFilter = query.ChannelId;

        var rows = await (
            from message in dbContext.Messages.AsNoTracking()
            join channel in dbContext.Channels.AsNoTracking() on message.ConversationId equals channel.Id
            join author in dbContext.UserProfiles.AsNoTracking() on message.AuthorId equals author.Id into authors
            from author in authors.DefaultIfEmpty()
            where message.TenantId == query.TenantId
                && channel.TenantId == query.TenantId
                && channel.WorkspaceId == query.WorkspaceId
                && message.DeletedAt == null
                && (channelFilter == null || message.ConversationId == channelFilter)
                && EF.Functions.ToTsVector(SearchPolicies.TextConfig, message.Body).Matches(tsQuery)
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
            orderby EF.Functions.ToTsVector(SearchPolicies.TextConfig, message.Body).Rank(tsQuery) descending, message.CreatedAt descending
            select new
            {
                Message = message,
                Channel = channel,
                AuthorName = author != null ? author.DisplayName : message.AuthorId.Value.ToString(),
                Rank = EF.Functions.ToTsVector(SearchPolicies.TextConfig, message.Body).Rank(tsQuery)
            })
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new SearchMessageHit(
            row.Message.Id.Value,
            row.Channel.Id.Value,
            FormatChannelName(row.Channel.Name, row.Channel.Type.ToString()),
            row.Channel.Type.ToString(),
            row.Message.Sequence,
            row.Message.AuthorId.Value,
            row.AuthorName,
            SearchPolicies.BuildPreview(row.Message.Body),
            row.Message.CreatedAt,
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
