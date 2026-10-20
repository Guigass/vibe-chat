using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure.Compliance;

public sealed class LegalHoldService(VibeChatDbContext db, IClock clock, ILogger<LegalHoldService> logger)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ComplianceResult> AccessAsync(
        Workspace workspace,
        UserId actor,
        bool canGrant,
        CancellationToken cancellationToken)
    {
        var grants = await db.ComplianceGrants.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && x.UserId == actor)
            .Select(x => x.Capability)
            .ToListAsync(cancellationToken);
        return ComplianceResult.Ok(new
        {
            enabled = true,
            hold = grants.Contains(ComplianceCapabilities.Hold),
            discovery = grants.Contains(ComplianceCapabilities.Discovery),
            canGrant
        });
    }

    public async Task<ComplianceResult> ListGrantsAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var rows = await db.ComplianceGrants.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return ComplianceResult.Ok(rows.Select(View));
    }

    public async Task<ComplianceResult> GrantAsync(
        Workspace workspace,
        UserId actor,
        Guid userId,
        string capability,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!ComplianceCapabilities.Known(capability))
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.CapabilityInvalid);
        }

        if (userId == actor.Value)
        {
            return ComplianceResult.Fail(409, LegalHoldErrors.SelfGrant);
        }

        var target = new UserId(userId);
        var member = await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.UserId == target, cancellationToken);
        if (!member)
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.MemberRequired);
        }

        var replay = await db.ComplianceGrants
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.GrantedBy == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return ComplianceResult.Ok(View(replay));
        }

        var existing = await db.ComplianceGrants
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == target && x.Capability == capability, cancellationToken);
        if (existing is not null)
        {
            return ComplianceResult.Ok(View(existing));
        }

        var now = clock.UtcNow;
        var grant = new ComplianceGrant
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            UserId = target,
            Capability = capability,
            GrantedBy = actor,
            CreatedAt = now,
            IdempotencyKey = idempotencyKey
        };
        db.ComplianceGrants.Add(grant);
        await AppendCustodyAsync(workspace, actor, CustodyActions.Grant, capability, null, null, null, idempotencyKey, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Compliance grant {GrantId} created in workspace {WorkspaceId}", grant.Id, workspace.Id.Value);
        return ComplianceResult.Ok(View(grant), 201);
    }

    public async Task<ComplianceResult> RevokeAsync(
        Workspace workspace,
        UserId actor,
        Guid grantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var replay = await db.CustodyEvents.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.ActorUserId == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        var grant = await db.ComplianceGrants.FirstOrDefaultAsync(x => x.Id == grantId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (grant is null)
        {
            return replay
                ? ComplianceResult.Ok(new { id = grantId, revoked = true })
                : ComplianceResult.Fail(404, LegalHoldErrors.NotFound);
        }

        if (replay)
        {
            return ComplianceResult.Ok(View(grant));
        }

        var now = clock.UtcNow;
        db.ComplianceGrants.Remove(grant);
        await AppendCustodyAsync(workspace, actor, CustodyActions.Revoke, grant.Capability, null, null, null, idempotencyKey, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ComplianceResult.Ok(new { id = grantId, revoked = true });
    }

    public async Task<ComplianceResult> ListHoldsAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var holds = await db.LegalHolds.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        var history = await db.CustodyEvents.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && x.HoldId != null)
            .OrderByDescending(x => x.OccurredAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        return ComplianceResult.Ok(new
        {
            holds = holds.Select(View),
            history = history.Select(View)
        });
    }

    public async Task<ComplianceResult> RequestHoldAsync(
        Workspace workspace,
        UserId actor,
        string scopeKind,
        Guid? scopeId,
        DateTimeOffset? intervalStart,
        DateTimeOffset? intervalEnd,
        string? reason,
        DateTimeOffset? expiresAt,
        bool confirmed,
        bool acknowledged,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!LegalHoldRules.Confirmed(confirmed, acknowledged))
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ConfirmationRequired);
        }

        if (!LegalHoldRules.ReasonOk(reason))
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ReasonRequired);
        }

        var replay = await db.LegalHolds
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.RequestedBy == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return ComplianceResult.Ok(View(replay));
        }

        var scopeError = await ValidateScopeAsync(workspace, scopeKind, scopeId, intervalStart, intervalEnd, cancellationToken);
        if (scopeError is not null)
        {
            return ComplianceResult.Fail(400, scopeError);
        }

        var now = clock.UtcNow;
        if (expiresAt is not null && expiresAt <= now)
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ExpiryInvalid);
        }

        var hold = new LegalHold
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            ScopeKind = scopeKind,
            ScopeId = scopeKind is LegalHoldScope.Workspace or LegalHoldScope.Interval ? null : scopeId,
            IntervalStart = scopeKind == LegalHoldScope.Interval ? intervalStart : null,
            IntervalEnd = scopeKind == LegalHoldScope.Interval ? intervalEnd : null,
            Reason = reason!.Trim(),
            Status = LegalHoldStatus.Requested,
            RequestedBy = actor,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            IdempotencyKey = idempotencyKey
        };
        db.LegalHolds.Add(hold);
        await AppendCustodyAsync(workspace, actor, CustodyActions.HoldRequest, hold.Reason, hold.Id, null, null, idempotencyKey, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Legal hold {HoldId} requested in workspace {WorkspaceId}", hold.Id, workspace.Id.Value);
        return ComplianceResult.Ok(View(hold), 201);
    }

    public Task<ComplianceResult> ApproveHoldAsync(
        Workspace workspace,
        UserId actor,
        Guid holdId,
        bool confirmed,
        bool acknowledged,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        TransitionAsync(workspace, actor, holdId, confirmed, acknowledged, idempotencyKey, LegalHoldStatus.Requested, LegalHoldStatus.Active, CustodyActions.HoldApprove, cancellationToken);

    public Task<ComplianceResult> RequestReleaseAsync(
        Workspace workspace,
        UserId actor,
        Guid holdId,
        string? reason,
        bool confirmed,
        bool acknowledged,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        TransitionAsync(workspace, actor, holdId, confirmed, acknowledged, idempotencyKey, LegalHoldStatus.Active, LegalHoldStatus.ReleaseRequested, CustodyActions.ReleaseRequest, cancellationToken, reason);

    public Task<ComplianceResult> ApproveReleaseAsync(
        Workspace workspace,
        UserId actor,
        Guid holdId,
        bool confirmed,
        bool acknowledged,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        TransitionAsync(workspace, actor, holdId, confirmed, acknowledged, idempotencyKey, LegalHoldStatus.ReleaseRequested, LegalHoldStatus.Released, CustodyActions.ReleaseApprove, cancellationToken);

    public async Task<ComplianceResult> ListCasesAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var cases = await db.DiscoveryCases.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return ComplianceResult.Ok(cases.Select(View));
    }

    public async Task<ComplianceResult> CreateCaseAsync(
        Workspace workspace,
        UserId actor,
        string? title,
        string? reason,
        Guid[]? custodians,
        Guid[]? channelIds,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? text,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var name = title?.Trim() ?? string.Empty;
        if (name.Length is 0 or > LegalHoldLimits.MaxTitleLength || !LegalHoldRules.ReasonOk(reason))
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ReasonRequired);
        }

        if (from is not null && to is not null && from > to)
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ScopeInvalid);
        }

        if ((text?.Length ?? 0) > LegalHoldLimits.MaxTextLength)
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ScopeInvalid);
        }

        var replay = await db.DiscoveryCases
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.CreatedBy == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return ComplianceResult.Ok(View(replay));
        }

        var query = new DiscoveryQuery(
            custodians ?? [],
            channelIds ?? [],
            from,
            to,
            string.IsNullOrWhiteSpace(text) ? null : text.Trim());
        if (query.Custodians.Length > 50 || query.ChannelIds.Length > 50)
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ScopeInvalid);
        }

        var now = clock.UtcNow;
        var discoveryCase = new DiscoveryCase
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            Title = name,
            Reason = reason!.Trim(),
            Status = "open",
            QueryJson = JsonSerializer.Serialize(query, Json),
            CreatedBy = actor,
            CreatedAt = now,
            IdempotencyKey = idempotencyKey
        };
        db.DiscoveryCases.Add(discoveryCase);
        await AppendCustodyAsync(workspace, actor, CustodyActions.CaseCreate, discoveryCase.Reason, null, discoveryCase.Id, null, idempotencyKey, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Discovery case {CaseId} opened in workspace {WorkspaceId}", discoveryCase.Id, workspace.Id.Value);
        return ComplianceResult.Ok(View(discoveryCase), 201);
    }

    public async Task<ComplianceResult> ExportAsync(
        Workspace workspace,
        UserId actor,
        Guid caseId,
        bool confirmed,
        bool acknowledged,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!LegalHoldRules.Confirmed(confirmed, acknowledged))
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ConfirmationRequired);
        }

        var replay = await db.DiscoveryExports
            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId && x.CreatedBy == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return ComplianceResult.Ok(ExportView(replay));
        }

        var discoveryCase = await db.DiscoveryCases
            .FirstOrDefaultAsync(x => x.Id == caseId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (discoveryCase is null)
        {
            return ComplianceResult.Fail(404, LegalHoldErrors.NotFound);
        }

        var query = JsonSerializer.Deserialize<DiscoveryQuery>(discoveryCase.QueryJson, Json)
            ?? new DiscoveryQuery([], [], null, null, null);
        var rows = await LoadMatchesAsync(workspace, query, cancellationToken);
        if (rows.Count > LegalHoldLimits.MaxItems)
        {
            return ComplianceResult.Fail(413, LegalHoldErrors.ResultTooLarge);
        }

        var now = clock.UtcNow;
        var manifest = new DiscoveryManifest(
            LegalHoldLimits.ExportFormat,
            discoveryCase.Id,
            now,
            query,
            rows.Select(row => new DiscoveryManifestItem(
                row.Id.Value,
                row.AuthorId.Value,
                row.ConversationId.Value,
                row.CreatedAt,
                row.DeletedAt,
                CustodyChain.Sha256Hex(row.Body),
                row.Body)).ToArray());
        var manifestJson = JsonSerializer.Serialize(manifest, Json);
        var export = new DiscoveryExportRecord
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            CaseId = discoveryCase.Id,
            Status = "ready",
            Progress = 100,
            ManifestJson = manifestJson,
            Checksum = CustodyChain.Sha256Hex(manifestJson),
            ItemCount = rows.Count,
            CreatedBy = actor,
            CreatedAt = now,
            IdempotencyKey = idempotencyKey
        };
        db.DiscoveryExports.Add(export);
        await AppendCustodyAsync(workspace, actor, CustodyActions.Export, discoveryCase.Reason, null, discoveryCase.Id, export.Id, idempotencyKey, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Discovery export {ExportId} ready for case {CaseId} with {Count} items",
            export.Id,
            discoveryCase.Id,
            export.ItemCount);
        return ComplianceResult.Ok(ExportView(export), 201);
    }

    public async Task<ComplianceResult> GetExportAsync(
        Workspace workspace,
        Guid caseId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        var export = await db.DiscoveryExports.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == exportId && x.CaseId == caseId && x.TenantId == workspace.TenantId, cancellationToken);
        if (export is null)
        {
            return ComplianceResult.Fail(404, LegalHoldErrors.NotFound);
        }

        var owned = await db.DiscoveryCases.AsNoTracking()
            .AnyAsync(x => x.Id == caseId && x.WorkspaceId == workspace.Id, cancellationToken);
        return owned ? ComplianceResult.Ok(ExportView(export)) : ComplianceResult.Fail(404, LegalHoldErrors.NotFound);
    }

    private async Task<ComplianceResult> TransitionAsync(
        Workspace workspace,
        UserId actor,
        Guid holdId,
        bool confirmed,
        bool acknowledged,
        string idempotencyKey,
        string expected,
        string next,
        string action,
        CancellationToken cancellationToken,
        string? reason = null)
    {
        if (!LegalHoldRules.Confirmed(confirmed, acknowledged))
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ConfirmationRequired);
        }

        if (action == CustodyActions.ReleaseRequest && !LegalHoldRules.ReasonOk(reason))
        {
            return ComplianceResult.Fail(400, LegalHoldErrors.ReasonRequired);
        }

        var replay = await db.CustodyEvents.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.ActorUserId == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        var hold = await db.LegalHolds.FirstOrDefaultAsync(x => x.Id == holdId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (hold is null)
        {
            return ComplianceResult.Fail(404, LegalHoldErrors.NotFound);
        }

        if (replay)
        {
            return ComplianceResult.Ok(View(hold));
        }

        if (hold.Status != expected)
        {
            return ComplianceResult.Fail(409, LegalHoldErrors.State);
        }

        var counterpart = action switch
        {
            CustodyActions.HoldApprove => hold.RequestedBy.Value,
            CustodyActions.ReleaseApprove => hold.ReleaseRequestedBy?.Value ?? Guid.Empty,
            _ => Guid.Empty
        };
        if (counterpart != Guid.Empty && !LegalHoldRules.CanApprove(counterpart, actor.Value))
        {
            return ComplianceResult.Fail(409, LegalHoldErrors.Segregation);
        }

        var now = clock.UtcNow;
        hold.Status = next;
        if (next == LegalHoldStatus.Active)
        {
            hold.ApprovedBy = actor;
            hold.ActivatedAt = now;
        }
        else if (next == LegalHoldStatus.ReleaseRequested)
        {
            hold.ReleaseRequestedBy = actor;
        }
        else if (next == LegalHoldStatus.Released)
        {
            hold.ReleaseApprovedBy = actor;
            hold.ReleasedAt = now;
        }

        var custodyReason = action == CustodyActions.ReleaseRequest ? reason!.Trim() : hold.Reason;
        await AppendCustodyAsync(workspace, actor, action, custodyReason, hold.Id, null, null, idempotencyKey, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Legal hold {HoldId} moved to {Status}", hold.Id, hold.Status);
        return ComplianceResult.Ok(View(hold));
    }

    private async Task<string?> ValidateScopeAsync(
        Workspace workspace,
        string scopeKind,
        Guid? scopeId,
        DateTimeOffset? intervalStart,
        DateTimeOffset? intervalEnd,
        CancellationToken cancellationToken)
    {
        if (!LegalHoldScope.Known(scopeKind))
        {
            return LegalHoldErrors.ScopeInvalid;
        }

        if (scopeKind == LegalHoldScope.Workspace)
        {
            return scopeId is null ? null : LegalHoldErrors.ScopeInvalid;
        }

        if (scopeKind == LegalHoldScope.Interval)
        {
            return scopeId is null && intervalStart is not null && intervalEnd is not null && intervalStart < intervalEnd
                ? null
                : LegalHoldErrors.ScopeInvalid;
        }

        if (scopeId is null)
        {
            return LegalHoldErrors.ScopeInvalid;
        }

        if (scopeKind == LegalHoldScope.User)
        {
            var member = await db.WorkspaceMembers.AsNoTracking()
                .AnyAsync(x => x.WorkspaceId == workspace.Id && x.UserId == new UserId(scopeId.Value), cancellationToken);
            return member ? null : LegalHoldErrors.MemberRequired;
        }

        if (scopeKind == LegalHoldScope.Channel)
        {
            var channel = await db.Channels.AsNoTracking()
                .AnyAsync(x => x.WorkspaceId == workspace.Id && x.Id == new ChannelId(scopeId.Value), cancellationToken);
            return channel ? null : LegalHoldErrors.ScopeInvalid;
        }

        var asChannel = await db.Channels.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.Id == new ChannelId(scopeId.Value), cancellationToken);
        if (asChannel)
        {
            return null;
        }

        var thread = await db.MessageThreads.AsNoTracking()
            .Where(x => x.Id == scopeId.Value)
            .Select(x => x.ChannelId)
            .FirstOrDefaultAsync(cancellationToken);
        if (thread.Value == Guid.Empty)
        {
            return LegalHoldErrors.ScopeInvalid;
        }

        var inWorkspace = await db.Channels.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.Id == thread, cancellationToken);
        return inWorkspace ? null : LegalHoldErrors.ScopeInvalid;
    }

    private async Task<List<VibeChat.Messaging.Message>> LoadMatchesAsync(
        Workspace workspace,
        DiscoveryQuery query,
        CancellationToken cancellationToken)
    {
        var channelIds = await db.Channels.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (query.ChannelIds.Length > 0)
        {
            var selected = query.ChannelIds.ToHashSet();
            channelIds = channelIds.Where(id => selected.Contains(id.Value)).ToList();
        }

        var threadIds = channelIds.Count == 0
            ? []
            : await db.MessageThreads.AsNoTracking()
                .Where(x => channelIds.Contains(x.ChannelId))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        var conversationIds = channelIds.Select(x => x.Value).Concat(threadIds).Distinct().ToArray();
        if (conversationIds.Length == 0)
        {
            return [];
        }

        var conversations = conversationIds.Select(id => new ChannelId(id)).ToArray();
        var messages = db.Messages.AsNoTracking().Where(x => conversations.Contains(x.ConversationId));
        if (query.From is not null)
        {
            messages = messages.Where(x => x.CreatedAt >= query.From);
        }

        if (query.To is not null)
        {
            messages = messages.Where(x => x.CreatedAt <= query.To);
        }

        if (query.Custodians.Length > 0)
        {
            var authors = query.Custodians.ToHashSet();
            var loaded = await messages.OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
            return loaded
                .Where(x => authors.Contains(x.AuthorId.Value))
                .Where(x => string.IsNullOrWhiteSpace(query.Text) || x.Body.Contains(query.Text, StringComparison.Ordinal))
                .Take(LegalHoldLimits.MaxItems + 1)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text;
            messages = messages.Where(x => x.Body.Contains(text));
        }

        return await messages.OrderBy(x => x.CreatedAt).Take(LegalHoldLimits.MaxItems + 1).ToListAsync(cancellationToken);
    }

    private async Task AppendCustodyAsync(
        Workspace workspace,
        UserId actor,
        string action,
        string reason,
        Guid? holdId,
        Guid? caseId,
        Guid? exportId,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previous = await db.CustodyEvents.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Select(x => x.PayloadHash)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var payload = CustodyChain.Payload(action, actor.Value, holdId, caseId, exportId, reason, now);
        var hash = CustodyChain.Hash(previous, payload);
        db.CustodyEvents.Add(new CustodyEvent
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            HoldId = holdId,
            CaseId = caseId,
            ExportId = exportId,
            Action = action,
            ActorUserId = actor,
            Reason = reason,
            OccurredAt = now,
            PayloadHash = hash,
            PreviousHash = previous,
            IdempotencyKey = idempotencyKey
        });
        AddAudit(workspace, actor, action, "Compliance", holdId ?? caseId ?? exportId, now);
    }

    private void AddAudit(Workspace workspace, UserId actor, string action, string entityType, Guid? entityId, DateTimeOffset now)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            MetadataJson = "{}",
            OccurredAt = now
        });
    }

    private static object View(ComplianceGrant grant) => new
    {
        id = grant.Id,
        userId = grant.UserId.Value,
        capability = grant.Capability,
        grantedBy = grant.GrantedBy.Value,
        createdAt = grant.CreatedAt
    };

    private static object View(LegalHold hold) => new
    {
        id = hold.Id,
        scopeKind = hold.ScopeKind,
        scopeId = hold.ScopeId,
        intervalStart = hold.IntervalStart,
        intervalEnd = hold.IntervalEnd,
        reason = hold.Reason,
        status = hold.Status,
        requestedBy = hold.RequestedBy.Value,
        approvedBy = hold.ApprovedBy?.Value,
        releaseRequestedBy = hold.ReleaseRequestedBy?.Value,
        releaseApprovedBy = hold.ReleaseApprovedBy?.Value,
        expiresAt = hold.ExpiresAt,
        createdAt = hold.CreatedAt,
        activatedAt = hold.ActivatedAt,
        releasedAt = hold.ReleasedAt
    };

    private static object View(DiscoveryCase discoveryCase) => new
    {
        id = discoveryCase.Id,
        title = discoveryCase.Title,
        reason = discoveryCase.Reason,
        status = discoveryCase.Status,
        query = JsonSerializer.Deserialize<DiscoveryQuery>(discoveryCase.QueryJson, Json),
        createdBy = discoveryCase.CreatedBy.Value,
        createdAt = discoveryCase.CreatedAt
    };

    private static object View(CustodyEvent row) => new
    {
        id = row.Id,
        action = row.Action,
        actorUserId = row.ActorUserId.Value,
        holdId = row.HoldId,
        caseId = row.CaseId,
        exportId = row.ExportId,
        occurredAt = row.OccurredAt,
        payloadHash = row.PayloadHash,
        previousHash = row.PreviousHash
    };

    private static object ExportView(DiscoveryExportRecord export) => new
    {
        id = export.Id,
        caseId = export.CaseId,
        status = export.Status,
        progress = export.Progress,
        checksum = export.Checksum,
        itemCount = export.ItemCount,
        createdAt = export.CreatedAt,
        manifestJson = export.ManifestJson
    };
}
