using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeChat.Audit;
using VibeChat.Conversations;
using VibeChat.Messaging;
using VibeChat.Search;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed class QdrantSemanticIndex(
    IOptions<SemanticSearchOptions> options,
    ILogger<QdrantSemanticIndex> logger)
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly SemaphoreSlim EnsureGate = new(1, 1);
    private static volatile bool _ensured;

    public bool IsEnabled =>
        Uri.TryCreate(options.Value.QdrantUrl, UriKind.Absolute, out _);

    public async Task UpsertAsync(IReadOnlyList<SemanticChunk> chunks, CancellationToken cancellationToken)
    {
        if (!IsEnabled || chunks.Count == 0)
        {
            return;
        }

        await EnsureCollectionAsync(cancellationToken);
        var points = chunks.Select(chunk => new
        {
            id = chunk.Id,
            vector = SemanticEmbedding.FromBase64(chunk.Embedding),
            payload = new
            {
                tenant_id = chunk.TenantId.Value,
                workspace_id = chunk.WorkspaceId.Value,
                chunk_id = chunk.Id
            }
        }).ToArray();
        await SendAsync(
            HttpMethod.Put,
            $"/collections/{SemanticPolicies.CollectionName}/points?wait=true",
            new { points },
            cancellationToken,
            readBody: false);
    }

    public async Task DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (!IsEnabled || ids.Count == 0)
        {
            return;
        }

        await SendAsync(
            HttpMethod.Post,
            $"/collections/{SemanticPolicies.CollectionName}/points/delete?wait=true",
            new { points = ids },
            cancellationToken,
            readBody: false);
    }

    public async Task DeleteWorkspaceAsync(Guid tenantId, Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        await SendAsync(
            HttpMethod.Post,
            $"/collections/{SemanticPolicies.CollectionName}/points/delete?wait=true",
            new
            {
                filter = new
                {
                    must = new object[]
                    {
                        new { key = "tenant_id", match = new { value = tenantId } },
                        new { key = "workspace_id", match = new { value = workspaceId } }
                    }
                }
            },
            cancellationToken,
            readBody: false);
    }

    public async Task<IReadOnlyList<Guid>> SearchAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        float[] vector,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return [];
        }

        await EnsureCollectionAsync(cancellationToken);
        var body = await SendAsync(
            HttpMethod.Post,
            $"/collections/{SemanticPolicies.CollectionName}/points/search",
            new
            {
                vector,
                limit = SemanticPolicies.MaxCitations * 5,
                with_payload = true,
                filter = new
                {
                    must = new object[]
                    {
                        new { key = "tenant_id", match = new { value = tenantId.Value } },
                        new { key = "workspace_id", match = new { value = workspaceId.Value } }
                    }
                }
            },
            cancellationToken,
            readBody: true);
        return ParsePointIds(body);
    }

    public static IReadOnlyList<Guid> ParsePointIds(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var ids = new List<Guid>();
            foreach (var hit in result.EnumerateArray())
            {
                if (hit.TryGetProperty("id", out var idNode) && Guid.TryParse(idNode.ToString(), out var id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        if (_ensured)
        {
            return;
        }

        await EnsureGate.WaitAsync(cancellationToken);
        try
        {
            if (_ensured)
            {
                return;
            }

            await SendAsync(
                HttpMethod.Put,
                $"/collections/{SemanticPolicies.CollectionName}",
                new
                {
                    vectors = new
                    {
                        size = SemanticPolicies.Dimensions,
                        distance = "Cosine"
                    }
                },
                cancellationToken,
                readBody: false,
                acceptConflict: true);
            _ensured = true;
        }
        finally
        {
            EnsureGate.Release();
        }
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken,
        bool readBody,
        bool acceptConflict = false)
    {
        if (http.BaseAddress is null)
        {
            http.BaseAddress = new Uri(options.Value.QdrantUrl.TrimEnd('/') + "/");
        }

        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Qdrant request failed for {Path}", path);
            throw;
        }

        using (response)
        {
            if (acceptConflict && response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return string.Empty;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Qdrant {Method} {Path} returned {Status}", method, path, (int)response.StatusCode);
                throw new HttpRequestException($"Qdrant returned {(int)response.StatusCode}");
            }

            return readBody ? await response.Content.ReadAsStringAsync(cancellationToken) : string.Empty;
        }
    }
}

public sealed class SemanticSearchService(
    VibeChatDbContext db,
    QdrantSemanticIndex qdrant,
    IAuditWriter audit,
    IOptions<SemanticSearchOptions> options,
    IClock clock,
    ILogger<SemanticSearchService> logger) : ISemanticIndex, ISemanticQuery
{
    public async Task SyncMessageAsync(TenantId tenantId, MessageId messageId, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var message = await db.Messages.FirstOrDefaultAsync(
            row => row.Id == messageId && row.TenantId == tenantId,
            cancellationToken);
        if (message is null || message.DeletedAt is not null)
        {
            await TombstoneAsync(tenantId, SemanticSourceKind.Message, messageId.Value, cancellationToken);
            return;
        }

        var channel = await ResolveChannelAsync(message, cancellationToken);
        if (channel is null)
        {
            await TombstoneAsync(tenantId, SemanticSourceKind.Message, messageId.Value, cancellationToken);
            return;
        }

        if (!await WorkspaceOptedInAsync(tenantId, channel.WorkspaceId, cancellationToken))
        {
            return;
        }

        await ReplaceChunksAsync(
            tenantId,
            channel.WorkspaceId,
            SemanticSourceKind.Message,
            message.Id.Value,
            message.Body,
            channel.Id.Value,
            null,
            cancellationToken);
    }

    public async Task SyncPageAsync(TenantId tenantId, Guid pageId, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var page = await db.KnowledgePages.FirstOrDefaultAsync(
            row => row.Id == pageId && row.TenantId == tenantId,
            cancellationToken);
        if (page is null || page.DeletedAt is not null)
        {
            await TombstoneAsync(tenantId, SemanticSourceKind.Page, pageId, cancellationToken);
            return;
        }

        var collection = await db.KnowledgeCollections.FirstOrDefaultAsync(
            row => row.Id == page.CollectionId && row.TenantId == tenantId && row.DeletedAt == null,
            cancellationToken);
        if (collection is null || !await WorkspaceOptedInAsync(tenantId, page.WorkspaceId, cancellationToken))
        {
            if (collection is null)
            {
                await TombstoneAsync(tenantId, SemanticSourceKind.Page, pageId, cancellationToken);
            }

            return;
        }

        Guid? channelId = collection.ScopeKind == KnowledgeScopeKind.Channel ? collection.ScopeId : null;
        await ReplaceChunksAsync(
            tenantId,
            page.WorkspaceId,
            SemanticSourceKind.Page,
            page.Id,
            page.Title + "\n" + page.Body,
            channelId,
            collection.Id,
            cancellationToken);
    }

    public async Task<SemanticRebuildResult> RebuildAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !await WorkspaceOptedInAsync(tenantId, workspaceId, cancellationToken))
        {
            return new SemanticRebuildResult(0, 0, 0);
        }

        var channelIds = await db.Channels.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.WorkspaceId == workspaceId)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        var messages = await db.Messages.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.DeletedAt == null && channelIds.Contains(row.ConversationId))
            .OrderByDescending(row => row.CreatedAt)
            .Take(SemanticPolicies.MaxCandidates)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        foreach (var messageId in messages)
        {
            await SyncMessageAsync(tenantId, messageId, cancellationToken);
        }

        var pages = await db.KnowledgePages.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.WorkspaceId == workspaceId && row.DeletedAt == null)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        foreach (var pageId in pages)
        {
            await SyncPageAsync(tenantId, pageId, cancellationToken);
        }

        var chunks = await db.SemanticChunks.AsNoTracking()
            .CountAsync(
                row => row.TenantId == tenantId && row.WorkspaceId == workspaceId && row.DeletedAt == null,
                cancellationToken);
        return new SemanticRebuildResult(messages.Count, pages.Count, chunks);
    }

    public async Task DeleteWorkspaceProjectionAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        var rows = await db.SemanticChunks
            .Where(row => row.TenantId == tenantId && row.WorkspaceId == workspaceId)
            .ToListAsync(cancellationToken);
        if (rows.Count > 0)
        {
            db.SemanticChunks.RemoveRange(rows);
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await qdrant.DeleteWorkspaceAsync(tenantId.Value, workspaceId.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Qdrant workspace delete failed for {WorkspaceId}", workspaceId.Value);
        }
    }

    public async Task<SemanticQueryOutcome> QueryAsync(SemanticQueryRequest request, CancellationToken cancellationToken)
    {
        var started = clock.UtcNow;
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var settings = await db.SemanticSettings.FirstOrDefaultAsync(
            row => row.TenantId == request.TenantId && row.WorkspaceId == request.WorkspaceId,
            cancellationToken);
        if (settings is null || !settings.Enabled)
        {
            return Disabled();
        }

        var now = clock.UtcNow;
        if (settings.BudgetPeriodStart <= now.AddDays(-SemanticPolicies.BudgetPeriodDays))
        {
            settings.TokensUsed = 0;
            settings.BudgetPeriodStart = now;
        }

        var queryTokens = SemanticPolicies.EstimateTokens(request.Query);
        if (await IsRateLimitedAsync(request, now, cancellationToken))
        {
            await RecordAsync(request, SemanticPolicies.CodeRate, 0, 0, started, cancellationToken);
            return new SemanticQueryOutcome(SemanticPolicies.CodeRate, SemanticPolicies.EvidenceNone, null, 0, [], 0);
        }

        var budget = settings.MonthlyTokenBudget <= 0
            ? options.Value.MonthlyTokenBudget
            : settings.MonthlyTokenBudget;
        if (settings.TokensUsed + queryTokens > budget)
        {
            await RecordAsync(request, SemanticPolicies.CodeBudget, 0, 0, started, cancellationToken);
            return new SemanticQueryOutcome(SemanticPolicies.CodeBudget, SemanticPolicies.EvidenceNone, null, 0, [], 0);
        }

        var citations = await RetrieveAsync(request, cancellationToken);
        var charge = queryTokens + citations.Sum(item => SemanticPolicies.EstimateTokens(item.Excerpt));
        if (settings.TokensUsed + charge > budget)
        {
            await RecordAsync(request, SemanticPolicies.CodeBudget, 0, 0, started, cancellationToken);
            return new SemanticQueryOutcome(SemanticPolicies.CodeBudget, SemanticPolicies.EvidenceNone, null, 0, [], 0);
        }

        settings.TokensUsed += charge;
        var answer = SemanticAnswerComposer.Compose(citations);
        await RecordAsync(
            request,
            answer.Evidence,
            charge,
            citations.Count,
            started,
            cancellationToken);
        return new SemanticQueryOutcome(
            SemanticPolicies.CodeOk,
            answer.Evidence,
            answer.Text,
            Math.Round(answer.Confidence, 3),
            citations,
            charge);
    }

    private async Task<IReadOnlyList<SemanticCitation>> RetrieveAsync(
        SemanticQueryRequest request,
        CancellationToken cancellationToken)
    {
        var queryVector = SemanticEmbedding.Embed(request.Query);
        var rows = await db.SemanticChunks.AsNoTracking()
            .Where(row => row.TenantId == request.TenantId
                && row.WorkspaceId == request.WorkspaceId
                && row.DeletedAt == null)
            .OrderByDescending(row => row.UpdatedAt)
            .Take(SemanticPolicies.MaxCandidates)
            .ToListAsync(cancellationToken);

        List<(SemanticChunk Chunk, double Score)> ranked;
        if (qdrant.IsEnabled)
        {
            try
            {
                var ids = await qdrant.SearchAsync(request.TenantId, request.WorkspaceId, queryVector, cancellationToken);
                var byId = rows.ToDictionary(row => row.Id);
                var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
                if (missing.Length > 0)
                {
                    var extra = await db.SemanticChunks.AsNoTracking()
                        .Where(row => row.TenantId == request.TenantId
                            && row.WorkspaceId == request.WorkspaceId
                            && row.DeletedAt == null
                            && missing.Contains(row.Id))
                        .ToListAsync(cancellationToken);
                    foreach (var row in extra)
                    {
                        byId[row.Id] = row;
                    }
                }

                ranked = ids
                    .Where(byId.ContainsKey)
                    .Select(id => (byId[id], SemanticEmbedding.Cosine(queryVector, SemanticEmbedding.FromBase64(byId[id].Embedding))))
                    .ToList();
                if (ranked.Count == 0)
                {
                    ranked = Rank(rows, queryVector);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Semantic query fell back to the postgres projection");
                ranked = Rank(rows, queryVector);
            }
        }
        else
        {
            ranked = Rank(rows, queryVector);
        }

        var citations = new List<SemanticCitation>(SemanticPolicies.MaxCitations);
        foreach (var (chunk, score) in ranked
            .Where(item => item.Score >= SemanticPolicies.MinScore)
            .OrderByDescending(item => item.Score)
            .Take(SemanticPolicies.MaxCitations * 5))
        {
            if (citations.Count >= SemanticPolicies.MaxCitations)
            {
                break;
            }

            var citation = await CiteIfAllowedAsync(request, chunk, score, cancellationToken);
            if (citation is not null)
            {
                citations.Add(citation);
            }
        }

        return citations;
    }

    private async Task<SemanticCitation?> CiteIfAllowedAsync(
        SemanticQueryRequest request,
        SemanticChunk chunk,
        double score,
        CancellationToken cancellationToken)
    {
        if (chunk.SourceKind == SemanticSourceKind.Message)
        {
            var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(
                row => row.Id == new MessageId(chunk.SourceId) && row.TenantId == request.TenantId && row.DeletedAt == null,
                cancellationToken);
            if (message is null || !HashStillCurrent(message.Body, chunk.ContentHash))
            {
                return null;
            }

            var channel = await ResolveChannelAsync(message, cancellationToken);
            if (channel is null
                || channel.WorkspaceId != request.WorkspaceId
                || !await CanReadChannelAsync(channel, request.UserId, message.Sequence, cancellationToken))
            {
                return null;
            }

            return new SemanticCitation(
                chunk.Id,
                SemanticPolicies.SourceToken(chunk.SourceKind),
                chunk.SourceId,
                channel.Id.Value,
                message.Sequence,
                SemanticEmbedding.Excerpt(CurrentSlice(message.Body, chunk.ContentHash) ?? chunk.Text),
                score);
        }

        var page = await db.KnowledgePages.AsNoTracking().FirstOrDefaultAsync(
            row => row.Id == chunk.SourceId && row.TenantId == request.TenantId && row.DeletedAt == null,
            cancellationToken);
        if (page is null)
        {
            return null;
        }

        var pageText = page.Title + "\n" + page.Body;
        if (!HashStillCurrent(pageText, chunk.ContentHash))
        {
            return null;
        }

        var collection = await db.KnowledgeCollections.AsNoTracking().FirstOrDefaultAsync(
            row => row.Id == page.CollectionId && row.TenantId == request.TenantId && row.DeletedAt == null,
            cancellationToken);
        if (collection is null || collection.WorkspaceId != request.WorkspaceId)
        {
            return null;
        }

        if (collection.ScopeKind == KnowledgeScopeKind.Channel)
        {
            var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(
                row => row.Id == new ChannelId(collection.ScopeId) && row.TenantId == request.TenantId,
                cancellationToken);
            if (channel is null || !await CanReadChannelAsync(channel, request.UserId, long.MaxValue, cancellationToken))
            {
                return null;
            }
        }
        else if (!await IsWorkspaceMemberAsync(request.TenantId, request.WorkspaceId, request.UserId, cancellationToken))
        {
            return null;
        }

        return new SemanticCitation(
            chunk.Id,
            SemanticPolicies.SourceToken(chunk.SourceKind),
            page.Id,
            collection.ScopeKind == KnowledgeScopeKind.Channel ? collection.ScopeId : null,
            null,
            SemanticEmbedding.Excerpt(CurrentSlice(pageText, chunk.ContentHash) ?? chunk.Text),
            score);
    }

    private async Task ReplaceChunksAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        SemanticSourceKind kind,
        Guid sourceId,
        string text,
        Guid? channelId,
        Guid? collectionId,
        CancellationToken cancellationToken)
    {
        var pieces = SemanticChunker.Split(text);
        var existing = await db.SemanticChunks
            .Where(row => row.TenantId == tenantId && row.SourceKind == kind && row.SourceId == sourceId)
            .ToListAsync(cancellationToken);
        if (pieces.Count == 0)
        {
            await TombstoneRowsAsync(existing, cancellationToken);
            return;
        }

        var now = clock.UtcNow;
        var active = new List<SemanticChunk>(pieces.Count);
        var removed = new List<Guid>();
        for (var i = 0; i < pieces.Count; i++)
        {
            var row = existing.FirstOrDefault(item => item.ChunkIndex == i);
            if (row is null)
            {
                row = new SemanticChunk
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    SourceKind = kind,
                    SourceId = sourceId,
                    ChunkIndex = i
                };
                db.SemanticChunks.Add(row);
            }

            row.WorkspaceId = workspaceId;
            row.ChannelId = channelId;
            row.CollectionId = collectionId;
            row.Text = pieces[i];
            row.ContentHash = SemanticEmbedding.Hash(pieces[i]);
            row.Embedding = SemanticEmbedding.ToBase64(SemanticEmbedding.Embed(pieces[i]));
            row.DeletedAt = null;
            row.UpdatedAt = now;
            active.Add(row);
        }

        foreach (var stale in existing.Where(item => item.ChunkIndex >= pieces.Count))
        {
            Clear(stale, now);
            removed.Add(stale.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await qdrant.UpsertAsync(active, cancellationToken);
            await qdrant.DeleteAsync(removed, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Qdrant upsert failed; postgres projection remains");
        }
    }

    private async Task TombstoneAsync(
        TenantId tenantId,
        SemanticSourceKind kind,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var existing = await db.SemanticChunks
            .Where(row => row.TenantId == tenantId && row.SourceKind == kind && row.SourceId == sourceId && row.DeletedAt == null)
            .ToListAsync(cancellationToken);
        await TombstoneRowsAsync(existing, cancellationToken);
    }

    private async Task TombstoneRowsAsync(List<SemanticChunk> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var row in rows)
        {
            Clear(row, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await qdrant.DeleteAsync(rows.Select(row => row.Id).ToArray(), cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Qdrant delete failed after tombstone");
        }
    }

    private static void Clear(SemanticChunk row, DateTimeOffset now)
    {
        row.Text = string.Empty;
        row.Embedding = string.Empty;
        row.ContentHash = string.Empty;
        row.DeletedAt = now;
        row.UpdatedAt = now;
    }

    private async Task<bool> WorkspaceOptedInAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken) =>
        await db.SemanticSettings.AsNoTracking().AnyAsync(
            row => row.TenantId == tenantId && row.WorkspaceId == workspaceId && row.Enabled,
            cancellationToken);

    private async Task<bool> IsRateLimitedAsync(
        SemanticQueryRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var since = now.AddMinutes(-1);
        var recent = await db.SemanticUsage.CountAsync(
            row => row.TenantId == request.TenantId
                && row.WorkspaceId == request.WorkspaceId
                && row.UserId == request.UserId
                && row.CreatedAt >= since,
            cancellationToken);
        return recent >= SemanticPolicies.QueriesPerMinute;
    }

    private async Task RecordAsync(
        SemanticQueryRequest request,
        string outcome,
        int tokens,
        int citations,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        db.SemanticUsage.Add(new SemanticUsage
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            WorkspaceId = request.WorkspaceId,
            UserId = request.UserId,
            Tokens = tokens,
            CitationCount = citations,
            LatencyMs = (int)Math.Max(0, (now - started).TotalMilliseconds),
            Outcome = outcome.Length > 32 ? outcome[..32] : outcome,
            CreatedAt = now
        });
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ActorUserId = request.UserId,
            Action = AuditActions.SemanticQuery,
            EntityType = "SemanticQuery",
            EntityId = request.WorkspaceId.Value.ToString(),
            MetadataJson = $"{{\"outcome\":\"{outcome}\",\"citations\":{citations},\"tokens\":{tokens}}}",
            OccurredAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Channel?> ResolveChannelAsync(Message message, CancellationToken cancellationToken)
    {
        ChannelId channelId;
        if (message.ThreadId is Guid threadId)
        {
            var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(
                row => row.Id == threadId && row.TenantId == message.TenantId,
                cancellationToken);
            if (thread is null)
            {
                return null;
            }

            channelId = thread.ChannelId;
        }
        else
        {
            var topicChannelId = await db.ChannelTopics.AsNoTracking()
                .Where(row => row.TenantId == message.TenantId && row.ConversationId == message.ConversationId)
                .Select(row => (ChannelId?)row.ChannelId)
                .FirstOrDefaultAsync(cancellationToken);
            channelId = topicChannelId ?? message.ConversationId;
        }

        return await db.Channels.AsNoTracking().FirstOrDefaultAsync(
            row => row.Id == channelId && row.TenantId == message.TenantId,
            cancellationToken);
    }

    private async Task<bool> CanReadChannelAsync(
        Channel channel,
        UserId userId,
        long sequence,
        CancellationToken cancellationToken)
    {
        if (channel.Type.RequiresChannelMembership())
        {
            var member = await db.ChannelMembers.AsNoTracking().FirstOrDefaultAsync(
                row => row.TenantId == channel.TenantId
                    && row.ChannelId == channel.Id
                    && row.UserId == userId
                    && row.LeftAt == null,
                cancellationToken);
            if (member is null)
            {
                return false;
            }

            return channel.Type != ChannelType.GroupDm || sequence > member.JoinedSeq;
        }

        return await IsWorkspaceMemberAsync(channel.TenantId, channel.WorkspaceId, userId, cancellationToken);
    }

    private Task<bool> IsWorkspaceMemberAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        UserId userId,
        CancellationToken cancellationToken) =>
        db.WorkspaceMembers.AsNoTracking().AnyAsync(
            row => row.TenantId == tenantId && row.WorkspaceId == workspaceId && row.UserId == userId,
            cancellationToken);

    private static bool HashStillCurrent(string text, string hash) =>
        SemanticChunker.Split(text).Any(slice => SemanticEmbedding.Hash(slice) == hash);

    private static string? CurrentSlice(string text, string hash) =>
        SemanticChunker.Split(text).FirstOrDefault(slice => SemanticEmbedding.Hash(slice) == hash);

    private static List<(SemanticChunk Chunk, double Score)> Rank(IReadOnlyList<SemanticChunk> rows, float[] queryVector) =>
        rows.Select(row => (row, SemanticEmbedding.Cosine(queryVector, SemanticEmbedding.FromBase64(row.Embedding)))).ToList();

    private static SemanticQueryOutcome Disabled() =>
        new(SemanticPolicies.CodeDisabled, SemanticPolicies.EvidenceNone, null, 0, [], 0);
}

public static class SemanticRetention
{
    public static async Task RemoveSourcesAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        SemanticSourceKind kind,
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken)
    {
        if (sourceIds.Count == 0)
        {
            return;
        }

        var rows = await db.SemanticChunks.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId && row.SourceKind == kind && sourceIds.Contains(row.SourceId))
            .ToListAsync(cancellationToken);
        if (rows.Count > 0)
        {
            db.SemanticChunks.RemoveRange(rows);
        }
    }
}
