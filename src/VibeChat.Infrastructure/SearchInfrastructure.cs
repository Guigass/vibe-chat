using Microsoft.EntityFrameworkCore;
using VibeChat.Conversations;
using VibeChat.Search;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

internal sealed class SearchHitRow
{
    public Guid MessageId { get; set; }
    public Guid ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string ChannelType { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorDisplayName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public double Rank { get; set; }
}

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

        // TextConfig is a fixed constant (not user input); keep it inline so EF parameter
        // binding stays unique/simple for SqlQuery.
        const string config = SearchPolicies.TextConfig;
        var channelId = query.ChannelId?.Value;

        IQueryable<SearchHitRow> rowsQuery = dbContext.Database.SqlQuery<SearchHitRow>($"""
            SELECT
                m."Id" AS "MessageId",
                m."ConversationId" AS "ChannelId",
                c."Name" AS "ChannelName",
                c."Type" AS "ChannelType",
                m."Sequence" AS "Sequence",
                m."AuthorId" AS "AuthorUserId",
                coalesce(u."DisplayName", m."AuthorId"::text) AS "AuthorDisplayName",
                m."Body" AS "Body",
                m."CreatedAt" AS "CreatedAt",
                ts_rank(m.search_vector, plainto_tsquery({config}, {term}))::double precision AS "Rank"
            FROM messaging.messages AS m
            INNER JOIN conversations.channels AS c
                ON c."Id" = m."ConversationId" AND c."TenantId" = m."TenantId"
            LEFT JOIN identity.user_profiles AS u
                ON u."Id" = m."AuthorId"
            WHERE m."TenantId" = {query.TenantId.Value}
              AND c."WorkspaceId" = {query.WorkspaceId.Value}
              AND m."DeletedAt" IS NULL
              AND m.search_vector @@ plainto_tsquery({config}, {term})
              AND ({channelId}::uuid IS NULL OR m."ConversationId" = {channelId})
              AND (
                    (
                        c."Type" IN ('Public', 'Announcement')
                        AND EXISTS (
                            SELECT 1
                            FROM tenancy.workspace_members AS wm
                            WHERE wm."TenantId" = m."TenantId"
                              AND wm."WorkspaceId" = c."WorkspaceId"
                              AND wm."UserId" = {query.UserId.Value}
                        )
                    )
                    OR (
                        c."Type" NOT IN ('Public', 'Announcement')
                        AND EXISTS (
                            SELECT 1
                            FROM conversations.channel_members AS cm
                            WHERE cm."TenantId" = m."TenantId"
                              AND cm."ChannelId" = c."Id"
                              AND cm."UserId" = {query.UserId.Value}
                        )
                    )
                  )
            """);

        var rows = await rowsQuery
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new SearchMessageHit(
            row.MessageId,
            row.ChannelId,
            FormatChannelName(row.ChannelName, row.ChannelType),
            row.ChannelType,
            row.Sequence,
            row.AuthorUserId,
            row.AuthorDisplayName,
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
