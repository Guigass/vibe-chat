using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeChat.AI;
using VibeChat.Search;

namespace VibeChat.Infrastructure.Ai;

public sealed class BotKnowledgeVectorIndex(
    IOptions<SemanticSearchOptions> options,
    ILogger<BotKnowledgeVectorIndex> logger)
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public bool IsEnabled => Uri.TryCreate(options.Value.QdrantUrl, UriKind.Absolute, out _);

    public async Task UpsertAsync(
        IReadOnlyList<BotKnowledgeChunk> chunks,
        string classification,
        IReadOnlyList<Guid> botIds,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || chunks.Count == 0)
        {
            return;
        }

        try
        {
            await EnsureAsync(cancellationToken);
            var points = chunks.Select(chunk => new
            {
                id = chunk.Id,
                vector = SemanticEmbedding.FromBase64(chunk.Embedding),
                payload = new
                {
                    tenant_id = chunk.TenantId.Value,
                    workspace_id = chunk.WorkspaceId.Value,
                    source_id = chunk.SourceId,
                    source_version = chunk.VersionNumber,
                    chunk_id = chunk.Id,
                    classification = classification,
                    content_hash = chunk.ContentHash,
                    embedding_profile = chunk.EmbeddingProfile,
                    projector_version = BotKnowledgePolicies.ProjectorVersion,
                    bot_ids = botIds
                }
            }).ToArray();
            await SendAsync(HttpMethod.Put, $"/collections/{BotKnowledgePolicies.CollectionName}/points?wait=true", new { points }, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Qdrant upsert deferred for bot knowledge");
            throw;
        }
    }

    public async Task DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (!IsEnabled || ids.Count == 0)
        {
            return;
        }

        try
        {
            await SendAsync(
                HttpMethod.Post,
                $"/collections/{BotKnowledgePolicies.CollectionName}/points/delete?wait=true",
                new { points = ids },
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Qdrant delete deferred for bot knowledge");
        }
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        await SendAsync(
            HttpMethod.Put,
            $"/collections/{BotKnowledgePolicies.CollectionName}",
            new { vectors = new { size = SemanticPolicies.Dimensions, distance = "Cosine" } },
            cancellationToken);
    }

    private async Task SendAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        var baseAddress = options.Value.QdrantUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(method, baseAddress + path) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            throw new HttpRequestException($"Qdrant returned {(int)response.StatusCode}");
        }
    }
}
