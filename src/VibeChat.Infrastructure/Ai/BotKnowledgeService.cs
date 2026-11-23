using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure.Ai;

public sealed record BotKnowledgeCommandResult(int Status, string? Error, object? Body);

public sealed class BotKnowledgeService(
    VibeChatDbContext db,
    ITenantContext tenant,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IIdempotencyStore idempotency,
    IClock clock,
    IOptions<BotKnowledgeOptions> options,
    IBotKnowledgeDirectory directory,
    BotKnowledgeVectorIndex vectors,
    ILogger<BotKnowledgeService> logger)
{
    public async Task<BotKnowledgeCommandResult> ListAsync(Workspace workspace, CancellationToken ct)
    {
        await PrepareAsync(workspace, ct);
        var rows = await db.BotKnowledgeSources.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        return Ok(rows.Select(Summary).ToArray());
    }

    public async Task<BotKnowledgeCommandResult> GetAsync(Workspace workspace, Guid sourceId, CancellationToken ct)
    {
        await PrepareAsync(workspace, ct);
        var source = await FindAsync(workspace.Id, sourceId, ct);
        return source is null ? Fail(404, BotKnowledgeErrors.NotFound) : Ok(await DetailAsync(source, ct));
    }

    public Task<BotKnowledgeCommandResult> CreateAsync(
        Workspace workspace, UserId actor, BotKnowledgeAccept input, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            if (!BotKnowledgePolicies.TryAccept(input.Name, input.Classification, input.Kind, out var kind, out var error))
            {
                return Fail(400, error!);
            }

            var count = await db.BotKnowledgeSources.CountAsync(x => x.WorkspaceId == workspace.Id && x.DeletedAt == null, ct);
            if (count >= options.Value.MaxSourcesPerWorkspace)
            {
                return Fail(409, BotKnowledgeErrors.QuotaExceeded);
            }

            var loaded = await LoadInputAsync(workspace, kind, input, ct);
            if (loaded.Error is not null)
            {
                return Fail(loaded.Status, loaded.Error);
            }

            var now = clock.UtcNow;
            var source = new BotKnowledgeSource
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Kind = kind,
                Status = BotKnowledgeStatus.Queued,
                Name = input.Name!.Trim(),
                Classification = input.Classification!.Trim(),
                Version = 1,
                Locator = loaded.Locator,
                FileId = loaded.FileId,
                PageId = loaded.PageId,
                OwnerUserId = actor,
                EmbeddingProfile = BotKnowledgePolicies.EmbeddingProfile,
                ProjectorVersion = BotKnowledgePolicies.ProjectorVersion,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.BotKnowledgeSources.Add(source);
            await IngestAsync(workspace, source, loaded, source.Version, attempt: 1, now, ct);
            Emit(source, actor, PublicEventCatalog.KnowledgeAccepted, now);
            Touch(source, actor, "ai.knowledge.accepted", now);
            await db.SaveChangesAsync(ct);
            return Ok(201, await DetailAsync(source, ct));
        }, ct);

    public Task<BotKnowledgeCommandResult> PauseAsync(Workspace workspace, UserId actor, Guid sourceId, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, () => SetPausedAsync(workspace, actor, sourceId, paused: true, ct), ct);

    public Task<BotKnowledgeCommandResult> ResumeAsync(Workspace workspace, UserId actor, Guid sourceId, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var paused = await SetPausedAsync(workspace, actor, sourceId, paused: false, ct);
            if (paused.Error is not null)
            {
                return paused;
            }

            var source = await FindTrackedAsync(workspace.Id, sourceId, ct);
            if (source is null)
            {
                return Fail(404, BotKnowledgeErrors.NotFound);
            }

            var loaded = await ReloadAsync(workspace, source, ct);
            if (loaded.Error is not null)
            {
                source.Status = BotKnowledgeStatus.Failed;
                source.LastError = loaded.Error;
                source.UpdatedAt = clock.UtcNow;
                await db.SaveChangesAsync(ct);
                return Ok(await DetailAsync(source, ct));
            }

            await IngestAsync(workspace, source, loaded, source.Version, await NextAttemptAsync(source.Id, ct), clock.UtcNow, ct);
            Emit(source, actor, PublicEventCatalog.KnowledgeChanged, clock.UtcNow);
            Touch(source, actor, "ai.knowledge.resumed", clock.UtcNow);
            await db.SaveChangesAsync(ct);
            return Ok(await DetailAsync(source, ct));
        }, ct);

    public Task<BotKnowledgeCommandResult> ReindexAsync(Workspace workspace, UserId actor, Guid sourceId, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var source = await FindTrackedAsync(workspace.Id, sourceId, ct);
            if (source is null || source.DeletedAt is not null)
            {
                return Fail(404, BotKnowledgeErrors.NotFound);
            }

            var previous = source.Status;
            var loaded = await ReloadAsync(workspace, source, ct);
            if (loaded.Error is not null)
            {
                source.LastError = loaded.Error;
                source.Status = previous == BotKnowledgeStatus.Ready ? BotKnowledgeStatus.Ready : BotKnowledgeStatus.Failed;
                source.UpdatedAt = clock.UtcNow;
                await db.SaveChangesAsync(ct);
                return Ok(await DetailAsync(source, ct));
            }

            var next = source.Version + 1;
            await IngestAsync(workspace, source, loaded, next, await NextAttemptAsync(source.Id, ct), clock.UtcNow, ct);
            if (source.Status == BotKnowledgeStatus.Ready)
            {
                await TombstoneOtherVersionsAsync(source, ct);
            }
            else
            {
                source.Status = previous == BotKnowledgeStatus.Ready ? BotKnowledgeStatus.Ready : source.Status;
                source.Version = next - 1;
            }

            Emit(source, actor, PublicEventCatalog.KnowledgeChanged, clock.UtcNow);
            Touch(source, actor, "ai.knowledge.reindexed", clock.UtcNow);
            await db.SaveChangesAsync(ct);
            return Ok(await DetailAsync(source, ct));
        }, ct);

    public Task<BotKnowledgeCommandResult> DeleteAsync(Workspace workspace, UserId actor, Guid sourceId, bool purge, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var source = await FindTrackedAsync(workspace.Id, sourceId, ct);
            if (source is null)
            {
                return Fail(404, BotKnowledgeErrors.NotFound);
            }

            var now = clock.UtcNow;
            var chunks = await db.BotKnowledgeChunks.Where(x => x.SourceId == source.Id).ToListAsync(ct);
            var ids = chunks.Select(x => x.Id).ToArray();
            source.PurgeEvidence = BotKnowledgePolicies.Checksum(ids.Select(id => id.ToString("N")).ToArray());
            source.Status = BotKnowledgeStatus.Deleting;
            source.DeletedAt ??= now;
            source.ChunkCount = 0;
            source.LastError = null;
            source.UpdatedAt = now;
            foreach (var chunk in chunks)
            {
                chunk.Text = string.Empty;
                chunk.Embedding = string.Empty;
                chunk.DeletedAt = now;
            }

            if (purge)
            {
                var versions = await db.BotKnowledgeSourceVersions.Where(x => x.SourceId == source.Id).ToListAsync(ct);
                foreach (var version in versions)
                {
                    version.Text = string.Empty;
                }

                db.BotKnowledgeChunks.RemoveRange(chunks);
            }

            await vectors.DeleteAsync(ids, ct);
            Emit(source, actor, PublicEventCatalog.KnowledgeRemoved, now);
            Touch(source, actor, purge ? "ai.knowledge.purged" : "ai.knowledge.deleted", now);
            await db.SaveChangesAsync(ct);
            return Ok(new
            {
                id = source.Id,
                status = source.Status.ToString(),
                removedChunks = ids.Length,
                evidenceChecksum = source.PurgeEvidence,
                deletedAt = source.DeletedAt
            });
        }, ct);

    public Task<BotKnowledgeCommandResult> GrantAsync(
        Workspace workspace, UserId actor, Guid sourceId, Guid botId, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var source = await FindAsync(workspace.Id, sourceId, ct);
            var bot = await db.AiBotDefinitions.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.Id == botId, ct);
            if (source is null || source.DeletedAt is not null)
            {
                return Fail(404, BotKnowledgeErrors.NotFound);
            }

            if (bot is null)
            {
                return Fail(404, BotKnowledgeErrors.BotNotFound);
            }

            var now = clock.UtcNow;
            var existing = await db.BotKnowledgeGrants.FirstOrDefaultAsync(
                x => x.SourceId == source.Id && x.BotId == botId && x.RevokedAt == null, ct);
            if (existing is null)
            {
                existing = new BotKnowledgeGrant
                {
                    Id = Guid.NewGuid(),
                    TenantId = workspace.TenantId,
                    WorkspaceId = workspace.Id,
                    BotId = botId,
                    SourceId = source.Id,
                    GrantedBy = actor,
                    CreatedAt = now
                };
                db.BotKnowledgeGrants.Add(existing);
                var ids = bot.KnowledgeGrantIds ?? [];
                if (!ids.Contains(existing.Id))
                {
                    bot.KnowledgeGrantIds = ids.Append(existing.Id).ToArray();
                    bot.Revision++;
                    bot.UpdatedAt = now;
                }
            }

            Touch(source, actor, "ai.knowledge.granted", now);
            await db.SaveChangesAsync(ct);
            return Ok(new { id = existing.Id, botId, sourceId = source.Id, revokedAt = existing.RevokedAt });
        }, ct);

    public Task<BotKnowledgeCommandResult> RevokeAsync(
        Workspace workspace, UserId actor, Guid sourceId, Guid grantId, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var grant = await db.BotKnowledgeGrants.FirstOrDefaultAsync(
                x => x.Id == grantId && x.SourceId == sourceId && x.WorkspaceId == workspace.Id, ct);
            if (grant is null)
            {
                return Fail(404, BotKnowledgeErrors.GrantNotFound);
            }

            grant.RevokedAt ??= clock.UtcNow;
            var source = await FindTrackedAsync(workspace.Id, sourceId, ct);
            if (source is not null)
            {
                Touch(source, actor, "ai.knowledge.revoked", grant.RevokedAt.Value);
            }

            await db.SaveChangesAsync(ct);
            return Ok(new { id = grant.Id, botId = grant.BotId, sourceId = grant.SourceId, revokedAt = grant.RevokedAt });
        }, ct);

    public async Task<BotKnowledgeCommandResult> RetrieveAsync(Workspace workspace, Guid botId, string? query, CancellationToken ct)
    {
        await PrepareAsync(workspace, ct);
        var text = (query ?? string.Empty).Trim();
        if (text.Length is < BotKnowledgePolicies.MinQueryLength or > BotKnowledgePolicies.MaxQueryLength)
        {
            return Fail(400, BotKnowledgeErrors.InvalidQuery);
        }

        var bot = await db.AiBotDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.Id == botId, ct);
        if (bot is null)
        {
            return Fail(404, BotKnowledgeErrors.BotNotFound);
        }

        var grants = await db.BotKnowledgeGrants.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && x.BotId == botId && x.RevokedAt == null)
            .Select(x => x.SourceId)
            .ToListAsync(ct);
        var sources = await db.BotKnowledgeSources.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && grants.Contains(x.Id))
            .ToListAsync(ct);
        var readable = sources.Where(BotKnowledgePolicies.Readable).ToArray();
        var sourceIds = readable.Select(x => x.Id).ToArray();
        var versions = readable.Select(x => x.Version).ToArray();
        var chunks = await db.BotKnowledgeChunks.AsNoTracking()
            .Where(x => sourceIds.Contains(x.SourceId) && x.DeletedAt == null && versions.Contains(x.VersionNumber))
            .ToListAsync(ct);
        var bySource = readable.ToDictionary(x => x.Id);
        var vector = SemanticEmbedding.Embed(text);
        var ranked = chunks
            .Where(chunk => bySource.TryGetValue(chunk.SourceId, out var source) && source.Version == chunk.VersionNumber)
            .Select(chunk => (
                Item: new
                {
                    sourceId = chunk.SourceId,
                    version = chunk.VersionNumber,
                    chunkId = chunk.Id,
                    excerpt = BotKnowledgePolicies.Excerpt(chunk.Text),
                    score = SemanticEmbedding.Cosine(vector, SemanticEmbedding.FromBase64(chunk.Embedding))
                },
                Score: SemanticEmbedding.Cosine(vector, SemanticEmbedding.FromBase64(chunk.Embedding)),
                Allowed: true))
            .ToArray();
        var citations = BotKnowledgePolicies.Citations(ranked);
        return Ok(new
        {
            evidence = citations.Count == 0 ? BotKnowledgeErrors.EvidenceInsufficient : BotKnowledgeErrors.EvidenceCited,
            citations
        });
    }

    private async Task<BotKnowledgeCommandResult> SetPausedAsync(Workspace workspace, UserId actor, Guid sourceId, bool paused, CancellationToken ct)
    {
        var source = await FindTrackedAsync(workspace.Id, sourceId, ct);
        if (source is null || source.DeletedAt is not null)
        {
            return Fail(404, BotKnowledgeErrors.NotFound);
        }

        var now = clock.UtcNow;
        source.Status = paused ? BotKnowledgeStatus.Paused : BotKnowledgeStatus.Queued;
        source.UpdatedAt = now;
        Emit(source, actor, PublicEventCatalog.KnowledgeChanged, now);
        Touch(source, actor, paused ? "ai.knowledge.paused" : "ai.knowledge.resumed", now);
        await db.SaveChangesAsync(ct);
        return Ok(await DetailAsync(source, ct));
    }

    private async Task IngestAsync(
        Workspace workspace,
        BotKnowledgeSource source,
        LoadedSource loaded,
        int version,
        int attempt,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var job = new BotKnowledgeIngestionJob
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            SourceId = source.Id,
            VersionNumber = version,
            Status = BotKnowledgeStatus.Scanning,
            Attempt = attempt,
            CreatedAt = now
        };
        db.BotKnowledgeIngestionJobs.Add(job);
        source.Status = BotKnowledgeStatus.Scanning;
        source.UpdatedAt = now;

        var failure = await PrepareTextAsync(loaded, ct);
        if (failure is not null)
        {
            await FailJobAsync(source, job, failure, now, ct);
            return;
        }

        if (loaded.Text.Length > BotKnowledgePolicies.MaxTextLength)
        {
            await FailJobAsync(source, job, BotKnowledgeErrors.SourceTooLarge, now, ct);
            return;
        }

        source.Status = BotKnowledgeStatus.Parsing;
        job.Status = BotKnowledgeStatus.Parsing;
        var pieces = SemanticChunker.Split(loaded.Text);
        if (pieces.Count == 0)
        {
            await FailJobAsync(source, job, BotKnowledgeErrors.InvalidSource, now, ct);
            return;
        }

        source.Status = BotKnowledgeStatus.Embedding;
        job.Status = BotKnowledgeStatus.Embedding;
        var previousChunks = await db.BotKnowledgeChunks.Where(x => x.SourceId == source.Id && x.VersionNumber == version).ToListAsync(ct);
        db.BotKnowledgeChunks.RemoveRange(previousChunks);
        var previousVersion = await db.BotKnowledgeSourceVersions.Where(x => x.SourceId == source.Id && x.VersionNumber == version).ToListAsync(ct);
        db.BotKnowledgeSourceVersions.RemoveRange(previousVersion);
        var hashes = new List<string>(pieces.Count);
        var rows = new List<BotKnowledgeChunk>(pieces.Count);
        for (var i = 0; i < pieces.Count; i++)
        {
            var hash = SemanticEmbedding.Hash(pieces[i]);
            hashes.Add(hash);
            rows.Add(new BotKnowledgeChunk
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                SourceId = source.Id,
                VersionNumber = version,
                ChunkIndex = i,
                Text = pieces[i],
                ContentHash = hash,
                Embedding = SemanticEmbedding.ToBase64(SemanticEmbedding.Embed(pieces[i])),
                EmbeddingProfile = BotKnowledgePolicies.EmbeddingProfile
            });
        }

        db.BotKnowledgeChunks.AddRange(rows);
        var checksum = BotKnowledgePolicies.Checksum(hashes);
        var contentHash = BotKnowledgePolicies.Sha256(loaded.Text);
        db.BotKnowledgeSourceVersions.Add(new BotKnowledgeSourceVersion
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            SourceId = source.Id,
            VersionNumber = version,
            ContentSha256 = contentHash,
            Text = loaded.Text,
            ChunkCount = rows.Count,
            Checksum = checksum,
            EmbeddingProfile = BotKnowledgePolicies.EmbeddingProfile,
            CreatedAt = now
        });
        source.Version = version;
        source.ChunkCount = rows.Count;
        source.Checksum = checksum;
        source.ContentSha256 = contentHash;
        source.EmbeddingProfile = BotKnowledgePolicies.EmbeddingProfile;
        source.ProjectorVersion = BotKnowledgePolicies.ProjectorVersion;
        source.LastError = null;
        source.Status = BotKnowledgeStatus.Ready;
        source.UpdatedAt = now;
        job.Status = BotKnowledgeStatus.Ready;
        job.FinishedAt = now;

        var botIds = await db.BotKnowledgeGrants.AsNoTracking()
            .Where(x => x.SourceId == source.Id && x.RevokedAt == null)
            .Select(x => x.BotId)
            .ToListAsync(ct);
        try
        {
            await vectors.UpsertAsync(rows, source.Classification, botIds, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            job.Status = BotKnowledgeStatus.Failed;
            job.Error = BotKnowledgeErrors.QdrantDeferred;
            logger.LogWarning("Bot knowledge source {SourceId} stayed ready without Qdrant", source.Id);
        }

        Emit(source, source.OwnerUserId, PublicEventCatalog.KnowledgeIndexed, now);
    }

    private async Task FailJobAsync(BotKnowledgeSource source, BotKnowledgeIngestionJob job, string error, DateTimeOffset now, CancellationToken ct)
    {
        source.Status = BotKnowledgeStatus.Failed;
        source.LastError = error;
        source.ChunkCount = 0;
        source.Checksum = string.Empty;
        source.UpdatedAt = now;
        job.Status = BotKnowledgeStatus.Failed;
        job.Error = error;
        job.FinishedAt = now;
        var stale = await db.BotKnowledgeChunks.Where(x => x.SourceId == source.Id && x.VersionNumber == job.VersionNumber).ToListAsync(ct);
        db.BotKnowledgeChunks.RemoveRange(stale);
        await Task.CompletedTask;
    }

    private async Task<LoadedSource> LoadInputAsync(Workspace workspace, BotKnowledgeKind kind, BotKnowledgeAccept input, CancellationToken ct)
    {
        if (BotKnowledgePolicies.RejectContentType(input.ContentType) is string rejected)
        {
            return LoadedSource.Fail(400, rejected);
        }

        if (kind == BotKnowledgeKind.Page)
        {
            if (input.PageId is not Guid pageId)
            {
                return LoadedSource.Fail(400, BotKnowledgeErrors.InvalidSource);
            }

            var page = await db.KnowledgePages.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == pageId && x.WorkspaceId == workspace.Id && x.DeletedAt == null, ct);
            if (page is null)
            {
                return LoadedSource.Fail(404, BotKnowledgeErrors.PageNotFound);
            }

            return new LoadedSource(page.Body, page.Id.ToString("D"), null, page.Id, null);
        }

        if (kind == BotKnowledgeKind.File)
        {
            if (input.FileId is Guid fileId)
            {
                var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(
                    x => x.Id == fileId && x.TenantId == workspace.TenantId, ct);
                if (attachment is null)
                {
                    return LoadedSource.Fail(404, BotKnowledgeErrors.FileNotFound);
                }

                if (attachment.ScanStatus == AttachmentScanStatus.Quarantined)
                {
                    return LoadedSource.Fail(400, BotKnowledgeErrors.MalwareQuarantined);
                }
            }

            if (!TryBytes(input, out var bytes, out var error))
            {
                return LoadedSource.Fail(400, error!);
            }

            return new LoadedSource(Encoding.UTF8.GetString(bytes), input.FileId?.ToString("D") ?? "inline", input.FileId, null, bytes);
        }

        if (!Uri.TryCreate((input.Url ?? string.Empty).Trim(), UriKind.Absolute, out var url) || url.ToString().Length > BotKnowledgePolicies.MaxUrlLength)
        {
            return LoadedSource.Fail(400, BotKnowledgeErrors.InvalidSource);
        }

        return new LoadedSource(string.Empty, url.ToString(), null, null, null, url);
    }

    private async Task<LoadedSource> ReloadAsync(Workspace workspace, BotKnowledgeSource source, CancellationToken ct)
    {
        if (source.Kind == BotKnowledgeKind.Page && source.PageId is Guid pageId)
        {
            return await LoadInputAsync(workspace, BotKnowledgeKind.Page, new BotKnowledgeAccept { PageId = pageId, ContentType = "text/plain" }, ct);
        }

        if (source.Kind == BotKnowledgeKind.Url && Uri.TryCreate(source.Locator, UriKind.Absolute, out var url))
        {
            return new LoadedSource(string.Empty, url.ToString(), null, null, null, url);
        }

        var version = await db.BotKnowledgeSourceVersions.AsNoTracking()
            .Where(x => x.SourceId == source.Id && x.Text != "")
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (version is null)
        {
            return LoadedSource.Fail(400, BotKnowledgeErrors.InvalidSource);
        }

        return new LoadedSource(version.Text, source.Locator, source.FileId, source.PageId, Encoding.UTF8.GetBytes(version.Text));
    }

    private async Task<string?> PrepareTextAsync(LoadedSource loaded, CancellationToken ct)
    {
        if (loaded.Url is null)
        {
            if (loaded.Bytes is null)
            {
                return null;
            }

            return Scan(loaded.Bytes, "text/plain");
        }

        var current = loaded.Url;
        for (var hop = 0; hop <= BotKnowledgePolicies.MaxRedirects; hop++)
        {
            var blocked = await AuthorizeAsync(current, ct);
            if (blocked is not null)
            {
                return blocked;
            }

            BotKnowledgeHttpResult response;
            try
            {
                response = await directory.GetAsync(
                    current,
                    Math.Clamp(options.Value.MaxUrlBytes, 1, BotKnowledgePolicies.MaxUrlBytes),
                    TimeSpan.FromSeconds(Math.Clamp(options.Value.FetchTimeoutSeconds, 1, 10)),
                    ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger.LogWarning("Bot knowledge fetch failed for host {Host}", current.Host);
                return BotKnowledgeErrors.FetchFailed;
            }

            if (response.Status is 301 or 302 or 303 or 307 or 308)
            {
                if (hop == BotKnowledgePolicies.MaxRedirects || !Uri.TryCreate(current, response.Location, out var next))
                {
                    return BotKnowledgeErrors.EgressBlocked;
                }

                current = next;
                continue;
            }

            if (response.Status == 413)
            {
                return BotKnowledgeErrors.SourceTooLarge;
            }

            if (response.Status != 200)
            {
                return BotKnowledgeErrors.FetchFailed;
            }

            if (BotKnowledgePolicies.RejectContentType(response.ContentType) is string rejected)
            {
                return rejected;
            }

            var media = response.ContentType.Split(';')[0].Trim();
            if (media is not ("text/plain" or "text/markdown"))
            {
                return BotKnowledgeErrors.UnsupportedType;
            }

            var scan = Scan(response.Body, "text/plain");
            if (scan is not null)
            {
                return scan;
            }

            loaded.Text = Encoding.UTF8.GetString(response.Body);
            return null;
        }

        return BotKnowledgeErrors.EgressBlocked;
    }

    private async Task<string?> AuthorizeAsync(Uri url, CancellationToken ct)
    {
        var allowlist = BotKnowledgePolicies.Allowlist(options.Value.UrlAllowlist);
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(url.UserInfo)
            || ConnectorPolicies.IsBlockedHost(url.IdnHost)
            || !allowlist.Contains(url.IdnHost.TrimEnd('.').ToLowerInvariant(), StringComparer.Ordinal))
        {
            return BotKnowledgeErrors.EgressBlocked;
        }

        try
        {
            var first = await directory.LookupAsync(url.IdnHost, ct);
            var second = await directory.LookupAsync(url.IdnHost, ct);
            return BotKnowledgeUrlPolicy.BlockReason(url, allowlist, first, second);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException)
        {
            logger.LogWarning("Bot knowledge DNS failed for host {Host}", url.Host);
            return BotKnowledgeErrors.EgressBlocked;
        }
    }

    private static string? Scan(byte[] bytes, string contentType)
    {
        var result = MalwareScanPolicies.Inspect(bytes, contentType);
        return result.Status == AttachmentScanStatus.Clean ? null : BotKnowledgeErrors.MalwareQuarantined;
    }

    private static bool TryBytes(BotKnowledgeAccept input, out byte[] bytes, out string? error)
    {
        bytes = [];
        error = null;
        if (!string.IsNullOrWhiteSpace(input.ContentBase64))
        {
            try
            {
                bytes = Convert.FromBase64String(input.ContentBase64);
                return bytes.Length > 0 ? true : FailBytes(out bytes, out error);
            }
            catch (FormatException)
            {
                return FailBytes(out bytes, out error);
            }
        }

        if (string.IsNullOrWhiteSpace(input.Text))
        {
            return FailBytes(out bytes, out error);
        }

        bytes = Encoding.UTF8.GetBytes(input.Text);
        return true;
    }

    private static bool FailBytes(out byte[] bytes, out string? error)
    {
        bytes = [];
        error = BotKnowledgeErrors.InvalidSource;
        return false;
    }

    private async Task TombstoneOtherVersionsAsync(BotKnowledgeSource source, CancellationToken ct)
    {
        var previous = await db.BotKnowledgeChunks
            .Where(x => x.SourceId == source.Id && x.VersionNumber != source.Version && x.DeletedAt == null)
            .ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var chunk in previous)
        {
            chunk.Text = string.Empty;
            chunk.Embedding = string.Empty;
            chunk.DeletedAt = now;
        }

        await vectors.DeleteAsync(previous.Select(x => x.Id).ToArray(), ct);
    }

    private async Task<int> NextAttemptAsync(Guid sourceId, CancellationToken ct) =>
        await db.BotKnowledgeIngestionJobs.CountAsync(x => x.SourceId == sourceId, ct) + 1;

    private async Task<object> DetailAsync(BotKnowledgeSource source, CancellationToken ct)
    {
        var jobs = await db.BotKnowledgeIngestionJobs.AsNoTracking()
            .Where(x => x.SourceId == source.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .Select(x => new { x.Id, status = x.Status.ToString(), x.Attempt, x.Error, x.FinishedAt })
            .ToListAsync(ct);
        var grants = await db.BotKnowledgeGrants.AsNoTracking()
            .Where(x => x.SourceId == source.Id)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.BotId, x.RevokedAt })
            .ToListAsync(ct);
        var lag = source.Status == BotKnowledgeStatus.Ready
            ? 0
            : Math.Max(0, (int)(clock.UtcNow - source.UpdatedAt).TotalSeconds);
        return new
        {
            id = source.Id,
            name = source.Name,
            kind = source.Kind.ToString().ToLowerInvariant(),
            classification = source.Classification,
            status = source.Status.ToString(),
            version = source.Version,
            chunkCount = source.ChunkCount,
            checksum = source.Checksum,
            contentSha256 = source.ContentSha256,
            lastError = source.LastError,
            locator = source.Locator,
            pageId = source.PageId,
            fileId = source.FileId,
            updatedAt = source.UpdatedAt,
            deletedAt = source.DeletedAt,
            indexLagSeconds = lag,
            purgeEvidence = source.PurgeEvidence,
            jobs,
            grants
        };
    }

    private static object Summary(BotKnowledgeSource source) => new
    {
        id = source.Id,
        name = source.Name,
        kind = source.Kind.ToString().ToLowerInvariant(),
        classification = source.Classification,
        status = source.Status.ToString(),
        version = source.Version,
        chunkCount = source.ChunkCount,
        checksum = source.Checksum,
        lastError = source.LastError,
        updatedAt = source.UpdatedAt
    };

    private void Emit(BotKnowledgeSource source, UserId actor, string eventType, DateTimeOffset now)
    {
        var payload = BotKnowledgePolicies.EventDocument(
            source.Id,
            source.WorkspaceId.Value,
            source.TenantId.Value,
            Math.Max(1, source.Version),
            actor.Value,
            string.IsNullOrEmpty(source.ContentSha256) ? new string('0', 64) : source.ContentSha256,
            source.Kind.ToString().ToLowerInvariant(),
            source.Status.ToString());
        var decision = PublicEventCatalog.Validate(eventType, 1, payload);
        if (!decision.Accepted)
        {
            throw new InvalidOperationException(decision.Reason);
        }

        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = source.TenantId,
            Type = eventType,
            Payload = payload,
            OccurredAt = now
        });
    }

    private void Touch(BotKnowledgeSource source, UserId actor, string action, DateTimeOffset now)
    {
        audit.Add(new AuditEvent
        {
            TenantId = source.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "BotKnowledgeSource",
            EntityId = source.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                status = source.Status.ToString(),
                version = source.Version,
                checksum = source.Checksum,
                error = source.LastError
            }),
            OccurredAt = now
        });
    }

    private async Task<BotKnowledgeCommandResult> MutateAsync(
        Workspace workspace, string key, string hash, Func<Task<BotKnowledgeCommandResult>> action, CancellationToken ct)
    {
        await PrepareAsync(workspace, ct);
        var storeKey = $"aiknowledge:{workspace.Id.Value:N}:{key}";
        var existing = await idempotency.FindAsync(workspace.TenantId, storeKey, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            {
                return Fail(409, BotKnowledgeErrors.IdempotencyConflict);
            }

            var envelope = JsonSerializer.Deserialize<StoredBody>(existing.ResultJson);
            return envelope is null
                ? Fail(409, BotKnowledgeErrors.IdempotencyConflict)
                : new BotKnowledgeCommandResult(envelope.Status, null, envelope.Body);
        }

        var result = await action();
        if (result.Error is null && result.Body is not null)
        {
            await idempotency.StoreAsync(new IdempotencyRecord(
                workspace.TenantId,
                storeKey,
                hash,
                JsonSerializer.Serialize(new StoredBody(result.Status, JsonSerializer.SerializeToElement(result.Body))),
                clock.UtcNow), ct);
            await db.SaveChangesAsync(ct);
        }

        if (result.Error is null)
        {
            await RlsSession.CommitAsync(db, ct);
        }

        return result;
    }

    private async Task PrepareAsync(Workspace workspace, CancellationToken ct)
    {
        tenant.SetTenant(workspace.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
    }

    private async Task<BotKnowledgeSource?> FindAsync(WorkspaceId workspaceId, Guid sourceId, CancellationToken ct) =>
        await db.BotKnowledgeSources.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == sourceId, ct);

    private async Task<BotKnowledgeSource?> FindTrackedAsync(WorkspaceId workspaceId, Guid sourceId, CancellationToken ct) =>
        await db.BotKnowledgeSources.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == sourceId, ct);

    private static BotKnowledgeCommandResult Ok(object body) => new(200, null, body);
    private static BotKnowledgeCommandResult Ok(int status, object body) => new(status, null, body);
    private static BotKnowledgeCommandResult Fail(int status, string error) => new(status, error, null);

    private sealed class LoadedSource
    {
        public LoadedSource(string text, string locator, Guid? fileId, Guid? pageId, byte[]? bytes, Uri? url = null)
        {
            Text = text;
            Locator = locator;
            FileId = fileId;
            PageId = pageId;
            Bytes = bytes;
            Url = url;
        }

        public string Text { get; set; }
        public string Locator { get; }
        public Guid? FileId { get; }
        public Guid? PageId { get; }
        public byte[]? Bytes { get; }
        public Uri? Url { get; }
        public int Status { get; private init; }
        public string? Error { get; private init; }

        public static LoadedSource Fail(int status, string error) =>
            new(string.Empty, string.Empty, null, null, null) { Status = status, Error = error };
    }

    private sealed record StoredBody(int Status, JsonElement Body);
}

public sealed class BotKnowledgeAccept
{
    public string? Name { get; set; }
    public string? Classification { get; set; }
    public string? Kind { get; set; }
    public string? Text { get; set; }
    public string? ContentBase64 { get; set; }
    public string? ContentType { get; set; }
    public string? Url { get; set; }
    public Guid? PageId { get; set; }
    public Guid? FileId { get; set; }
}
