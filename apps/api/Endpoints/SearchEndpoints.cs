using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.Messaging;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class SearchEndpoints
{
    internal static void MapSearch(this RouteGroupBuilder v1)
    {
        v1.MapGet("/search/messages", async (
            Guid workspaceId,
            string? q,
            Guid? channelId,
            Guid? authorId,
            string? from,
            string? to,
            bool? hasAttachment,
            bool? hasLink,
            string? attachmentKind,
            string? sort,
            string? cursor,
            int? limit,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            ISearchQuery search,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            ChannelId? scopedChannel = null;
            if (channelId is not null)
            {
                var channel = await ResolveChannelAsync(new ChannelId(channelId.Value), profile.Id, db, tenant, ct);
                if (channel is null || channel.WorkspaceId != workspace.Id)
                {
                    return Results.Forbid();
                }

                scopedChannel = channel.Id;
            }

            UserId? scopedAuthor = null;
            if (authorId is not null)
            {
                var authorVisible = await db.WorkspaceMembers.AsNoTracking().AnyAsync(
                    m => m.WorkspaceId == workspace.Id && m.UserId == new UserId(authorId.Value),
                    ct);
                if (!authorVisible)
                {
                    return Results.Forbid();
                }

                scopedAuthor = new UserId(authorId.Value);
            }

            if (!SearchPolicies.TryParseInstant(from, endOfDay: false, out var fromInstant)
                || !SearchPolicies.TryParseInstant(to, endOfDay: true, out var toInstant))
            {
                return Results.BadRequest(new { error = "InvalidDateRange" });
            }

            if (!SearchPolicies.TryParseAttachmentKind(attachmentKind, out var kind))
            {
                return Results.BadRequest(new { error = "InvalidAttachmentKind" });
            }

            if (!string.IsNullOrWhiteSpace(cursor) && !SearchCursorCodec.TryDecode(cursor, out _))
            {
                return Results.BadRequest(new { error = "InvalidCursor" });
            }

            try
            {
                var page = await search.SearchMessagesAsync(
                    new SearchMessagesQuery(
                        workspace.TenantId,
                        profile.Id,
                        workspace.Id,
                        q ?? string.Empty,
                        scopedChannel,
                        SearchPolicies.NormalizeLimit(limit),
                        scopedAuthor,
                        fromInstant,
                        toInstant,
                        hasAttachment,
                        hasLink,
                        kind,
                        SearchPolicies.ParseSort(sort),
                        cursor),
                    ct);

                return Results.Ok(new SearchMessagesResponse(
                    page.Query,
                    page.Limit,
                    page.Items.Select(x => new SearchMessageHitResponse(
                        x.MessageId,
                        x.ChannelId,
                        x.ChannelName,
                        x.ChannelType,
                        x.Sequence,
                        x.AuthorUserId,
                        x.AuthorDisplayName,
                        x.BodyPreview,
                        x.CreatedAt,
                        x.Rank)).ToArray(),
                    page.Total,
                    page.Cursor,
                    (page.Channels ?? []).Select(x => new SearchChannelHitResponse(
                        "channel",
                        x.ChannelId,
                        x.ChannelName,
                        x.ChannelType,
                        x.Rank)).ToArray(),
                    (page.People ?? []).Select(x => new SearchPersonHitResponse(
                        "person",
                        x.UserId,
                        x.DisplayName,
                        x.Rank)).ToArray(),
                    (page.Attachments ?? []).Select(x => new SearchAttachmentHitResponse(
                        "attachment",
                        x.AttachmentId,
                        x.FileName,
                        x.MessageId,
                        x.ChannelId,
                        x.ChannelName,
                        x.ChannelType,
                        x.Sequence,
                        x.Rank)).ToArray()));
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.GetBaseException().Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        }).RequirePermission(Permissions.Search.Messages, Permissions.Message.Read);

        v1.MapGet("/workspaces/{workspaceId:guid}/semantic/settings", GetSettings)
            .RequirePermission(Permissions.Ai.Search);
        v1.MapPut("/workspaces/{workspaceId:guid}/semantic/settings", PutSettings)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/semantic/query", Query)
            .RequirePermission(Permissions.Ai.Search, Permissions.Search.Messages);
        v1.MapPost("/workspaces/{workspaceId:guid}/semantic/report", Report)
            .RequirePermission(Permissions.Ai.Search, Permissions.Search.Messages);
        v1.MapPost("/workspaces/{workspaceId:guid}/semantic/rebuild", Rebuild)
            .RequirePermission(Permissions.Workspace.Admin);
    }

    private static async Task<IResult> GetSettings(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<SemanticSearchOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var settings = await db.SemanticSettings.AsNoTracking().FirstOrDefaultAsync(
            row => row.WorkspaceId == gate.Workspace!.Id,
            ct);
        var indexed = await db.SemanticChunks.AsNoTracking().CountAsync(
            row => row.WorkspaceId == gate.Workspace!.Id && row.DeletedAt == null,
            ct);
        return Results.Ok(new
        {
            processEnabled = true,
            enabled = settings?.Enabled ?? false,
            monthlyTokenBudget = settings?.MonthlyTokenBudget ?? NormalizedBudget(options.Value),
            tokensUsed = settings?.TokensUsed ?? 0,
            indexedChunks = indexed,
            embeddingProfile = SemanticPolicies.EmbeddingProfile
        });
    }

    private static async Task<IResult> PutSettings(
        Guid workspaceId,
        SemanticSettingsBody body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        ISemanticIndex index,
        IAuditWriter audit,
        IOptions<SemanticSearchOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var now = clock.UtcNow;
        var settings = await db.SemanticSettings.FirstOrDefaultAsync(row => row.WorkspaceId == gate.Workspace!.Id, ct);
        if (settings is null)
        {
            settings = new SemanticSettings
            {
                WorkspaceId = gate.Workspace!.Id,
                TenantId = gate.Workspace.TenantId,
                MonthlyTokenBudget = NormalizedBudget(options.Value),
                BudgetPeriodStart = now
            };
            db.SemanticSettings.Add(settings);
        }

        settings.Enabled = body.Enabled;
        if (!body.Enabled)
        {
            await index.DeleteWorkspaceProjectionAsync(gate.Workspace!.TenantId, gate.Workspace.Id, ct);
        }

        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Workspace!.TenantId,
            ActorUserId = gate.Profile!.Id,
            Action = AuditActions.SemanticSettingsChange,
            EntityType = "SemanticSettings",
            EntityId = gate.Workspace.Id.Value.ToString(),
            MetadataJson = $"{{\"enabled\":{(body.Enabled ? "true" : "false")}}}",
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { enabled = settings.Enabled });
    }

    private static async Task<IResult> Query(
        Guid workspaceId,
        SemanticQueryBody body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        ISemanticQuery semantic,
        DlpService dlp,
        IOptions<SemanticSearchOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var query = body.Q?.Trim() ?? string.Empty;
        if (query.Length < SemanticPolicies.MinQueryLength || query.Length > SemanticPolicies.MaxQueryLength)
        {
            return Results.BadRequest(new { error = SemanticPolicies.CodeInvalid });
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var denied = await DlpGuard.BlockAsync(
            dlp, gate.Workspace!.TenantId, gate.Workspace.Id, null, null, null, DlpActions.Ai, null, null, ct);
        if (denied is not null)
        {
            return denied;
        }

        var outcome = await semantic.QueryAsync(
            new SemanticQueryRequest(gate.Workspace!.TenantId, gate.Workspace.Id, gate.Profile!.Id, query),
            ct);
        return MapOutcome(outcome);
    }

    private static async Task<IResult> Report(
        Guid workspaceId,
        SemanticReportBody body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        DlpService dlp,
        IOptions<SemanticSearchOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var enabled = await db.SemanticSettings.AsNoTracking().AnyAsync(
            row => row.WorkspaceId == gate.Workspace!.Id && row.Enabled,
            ct);
        if (!enabled)
        {
            return Disabled();
        }

        var denied = await DlpGuard.BlockAsync(
            dlp, gate.Workspace!.TenantId, gate.Workspace.Id, null, null, null, DlpActions.Ai, null, null, ct);
        if (denied is not null)
        {
            return denied;
        }

        var ids = (body.CitationIds ?? []).Where(id => id != Guid.Empty).Distinct().Take(SemanticPolicies.MaxCitations).ToArray();
        var now = clock.UtcNow;
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Workspace!.TenantId,
            ActorUserId = gate.Profile!.Id,
            Action = AuditActions.SemanticReport,
            EntityType = "SemanticQuery",
            EntityId = gate.Workspace.Id.Value.ToString(),
            MetadataJson = $"{{\"citations\":{ids.Length}}}",
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { reported = true, citations = ids.Length });
    }

    private static async Task<IResult> Rebuild(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        ISemanticIndex index,
        IAuditWriter audit,
        IOptions<SemanticSearchOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var enabled = await db.SemanticSettings.AsNoTracking().AnyAsync(
            row => row.WorkspaceId == gate.Workspace!.Id && row.Enabled,
            ct);
        if (!enabled)
        {
            return Disabled();
        }

        var result = await index.RebuildAsync(gate.Workspace!.TenantId, gate.Workspace.Id, ct);
        var now = clock.UtcNow;
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = gate.Workspace.TenantId,
            ActorUserId = gate.Profile!.Id,
            Action = AuditActions.SemanticRebuild,
            EntityType = "SemanticSettings",
            EntityId = gate.Workspace.Id.Value.ToString(),
            MetadataJson = $"{{\"messages\":{result.Messages},\"pages\":{result.Pages},\"chunks\":{result.Chunks}}}",
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { result.Messages, result.Pages, result.Chunks });
    }

    private static async Task<(Workspace? Workspace, UserProfile? Profile, IResult? Error)> OpenWorkspaceAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return (null, null, Results.Forbid());
        }

        return (workspace, profile, null);
    }

    private static IResult MapOutcome(SemanticQueryOutcome outcome) => outcome.Code switch
    {
        SemanticPolicies.CodeDisabled => Disabled(),
        SemanticPolicies.CodeBudget or SemanticPolicies.CodeRate => Results.Json(
            new { error = outcome.Code },
            statusCode: StatusCodes.Status429TooManyRequests),
        SemanticPolicies.CodeUnavailable => Results.Json(
            new { error = outcome.Code },
            statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Ok(new
        {
            evidence = outcome.Evidence,
            answer = outcome.Answer,
            confidence = outcome.Confidence,
            tokensCharged = outcome.TokensCharged,
            citations = outcome.Citations.Select(item => new
            {
                chunkId = item.ChunkId,
                sourceKind = item.SourceKind,
                sourceId = item.SourceId,
                channelId = item.ChannelId,
                sequence = item.Sequence,
                excerpt = item.Excerpt,
                score = Math.Round(item.Score, 3)
            }).ToArray()
        })
    };

    private static int NormalizedBudget(SemanticSearchOptions options) =>
        Math.Clamp(
            options.MonthlyTokenBudget <= 0 ? SemanticPolicies.DefaultMonthlyTokenBudget : options.MonthlyTokenBudget,
            1,
            SemanticPolicies.MaxMonthlyTokenBudget);

    private static IResult Disabled() =>
        Results.NotFound(new { error = SemanticPolicies.CodeDisabled });

    internal sealed record SemanticQueryBody(string? Q);
    internal sealed record SemanticSettingsBody(bool Enabled);
    internal sealed record SemanticReportBody(Guid[]? CitationIds);
}
