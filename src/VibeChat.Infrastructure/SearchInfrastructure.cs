using Microsoft.EntityFrameworkCore;
using VibeChat.Conversations;
using VibeChat.Search;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

internal sealed class SearchHitRow
{
    public Guid MessageId { get; init; }
    public Guid ChannelId { get; init; }
    public string ChannelName { get; init; } = string.Empty;
    public string ChannelType { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public Guid AuthorUserId { get; init; }
    public string AuthorDisplayName { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public double Rank { get; init; }
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

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE messaging.messages
            SET search_vector = to_tsvector({0}, coalesce("Body", ''))
            WHERE "Id" = {1} AND "TenantId" = {2} AND "DeletedAt" IS NULL
            """,
            SearchPolicies.TextConfig,
            doc.MessageId.Value,
            doc.TenantId.Value);
    }

    public Task RemoveMessageAsync(TenantId tenantId, MessageId messageId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE messaging.messages
            SET search_vector = NULL
            WHERE "Id" = {0} AND "TenantId" = {1}
            """,
            messageId.Value,
            tenantId.Value);
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

        const string baseSql = """
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
                ts_rank(m.search_vector, plainto_tsquery({0}, {1}))::double precision AS "Rank"
            FROM messaging.messages m
            INNER JOIN conversations.channels c
                ON c."Id" = m."ConversationId" AND c."TenantId" = m."TenantId"
            LEFT JOIN identity.user_profiles u
                ON u."Id" = m."AuthorId"
            WHERE m."TenantId" = {2}
              AND c."WorkspaceId" = {3}
              AND m."DeletedAt" IS NULL
              AND m.search_vector @@ plainto_tsquery({0}, {1})
              AND (
                    (
                        c."Type" IN ('Public', 'Announcement')
                        AND EXISTS (
                            SELECT 1
                            FROM tenancy.workspace_members wm
                            WHERE wm."TenantId" = m."TenantId"
                              AND wm."WorkspaceId" = c."WorkspaceId"
                              AND wm."UserId" = {4}
                        )
                    )
                    OR (
                        c."Type" NOT IN ('Public', 'Announcement')
                        AND EXISTS (
                            SELECT 1
                            FROM conversations.channel_members cm
                            WHERE cm."TenantId" = m."TenantId"
                              AND cm."ChannelId" = c."Id"
                              AND cm."UserId" = {4}
                        )
                    )
                  )
            """;

        string sql;
        object[] parameters;
        if (query.ChannelId is null)
        {
            sql = baseSql + """
                
                ORDER BY "Rank" DESC, m."CreatedAt" DESC
                LIMIT {5}
                """;
            parameters =
            [
                SearchPolicies.TextConfig,
                term,
                query.TenantId.Value,
                query.WorkspaceId.Value,
                query.UserId.Value,
                limit
            ];
        }
        else
        {
            sql = baseSql + """
                
                  AND m."ConversationId" = {5}
                ORDER BY "Rank" DESC, m."CreatedAt" DESC
                LIMIT {6}
                """;
            parameters =
            [
                SearchPolicies.TextConfig,
                term,
                query.TenantId.Value,
                query.WorkspaceId.Value,
                query.UserId.Value,
                query.ChannelId.Value.Value,
                limit
            ];
        }

        var rows = await dbContext.Database
            .SqlQueryRaw<SearchHitRow>(sql, parameters)
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
