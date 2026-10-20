using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure.Compliance;

public sealed class DlpService(
    VibeChatDbContext db,
    IClock clock,
    IOptions<DlpOptions> options,
    IAuditWriter audit)
{
    public const string ReviewHint = "DlpReview";

    private static readonly Meter Meter = new("VibeChat.Dlp", "1.0.0");
    private static readonly Counter<long> Decisions = Meter.CreateCounter<long>("vibechat.dlp.decisions");

    public bool Enabled => options.Value.Enabled;

    public async Task<DlpOutcome> EvaluateAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        Guid? spaceId,
        Guid? channelId,
        string? channelType,
        string action,
        string? body,
        Guid? subjectId,
        bool save,
        CancellationToken cancellationToken,
        bool? forceSimulation = null)
    {
        if (!Enabled || !DlpActions.Known(action))
        {
            return new DlpOutcome(action, DlpEffects.Allow, DlpReasons.Allow, true, true, null, -1, 0, false, false);
        }

        var auditOn = await ContentAuditEnabledAsync(tenantId, cancellationToken);
        var snapshot = await LoadAsync(workspaceId, cancellationToken);
        var outcome = DlpRules.Evaluate(
            action,
            forceSimulation ?? snapshot.Simulation,
            auditOn,
            channelType,
            workspaceId.Value,
            spaceId,
            channelId,
            body,
            subjectId,
            snapshot.Assignments,
            snapshot.Ranks,
            snapshot.Rules,
            snapshot.Detectors,
            snapshot.Exceptions,
            snapshot.FalsePositives,
            clock.UtcNow);
        if (!outcome.Recorded)
        {
            return outcome;
        }

        db.DlpDecisions.Add(new DlpDecisionRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Action = outcome.Action,
            Effect = outcome.Effect,
            ReasonCode = outcome.ReasonCode,
            Simulation = outcome.Simulation,
            Inspectable = outcome.Inspectable,
            LabelKey = outcome.LabelKey,
            MatchCount = outcome.MatchCount,
            ChannelId = channelId,
            SubjectId = subjectId,
            CreatedAt = clock.UtcNow
        });
        Decisions.Add(1,
            new KeyValuePair<string, object?>("action", outcome.Action),
            new KeyValuePair<string, object?>("effect", outcome.Effect),
            new KeyValuePair<string, object?>("simulation", outcome.Simulation));
        if (save)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return outcome;
    }

    public async Task<DlpCommandResult> LabelAsync(Workspace workspace, Guid? channelId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        Guid? spaceId = null;
        string? channelType = null;
        if (channelId is Guid id)
        {
            var channel = await db.Channels.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == new ChannelId(id) && x.WorkspaceId == workspace.Id, cancellationToken);
            if (channel is null)
            {
                return DlpCommandResult.Fail(404, DlpErrors.NotFound);
            }

            spaceId = channel.SpaceId;
            channelType = channel.Type.ToString();
        }

        var snapshot = await LoadAsync(workspace.Id, cancellationToken);
        var auditOn = await ContentAuditEnabledAsync(workspace.TenantId, cancellationToken);
        var key = DlpRules.ResolveLabel(snapshot.Assignments, workspace.Id.Value, spaceId, channelId);
        var inspectable = DlpRules.IsInspectable(channelType, auditOn);
        return DlpCommandResult.Ok(new
        {
            labelKey = key,
            inspectable,
            notInspectable = !inspectable && ContentAuditRules.IsConfidentialE2eeType(channelType),
            simulation = snapshot.Simulation
        });
    }

    public async Task<DlpCommandResult> PolicyAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        var policy = await db.DlpPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id, cancellationToken);
        return DlpCommandResult.Ok(new
        {
            simulation = policy?.Simulation ?? true,
            version = policy?.Version ?? 0
        });
    }

    public async Task<DlpCommandResult> SetPolicyAsync(
        Workspace workspace,
        UserId actor,
        bool simulation,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        var policy = await db.DlpPolicies.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id, cancellationToken);
        if (policy is null)
        {
            policy = new DlpPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Version = 0
            };
            db.DlpPolicies.Add(policy);
        }

        policy.Simulation = simulation;
        policy.Version += 1;
        policy.UpdatedAt = clock.UtcNow;
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actor,
            Action = AuditActions.DlpPolicyChange,
            EntityType = "DlpPolicy",
            EntityId = policy.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { simulation, version = policy.Version })
        });
        await db.SaveChangesAsync(cancellationToken);
        return DlpCommandResult.Ok(new { simulation = policy.Simulation, version = policy.Version });
    }

    public async Task<DlpCommandResult> ListLabelsAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        var custom = await db.DataLabels.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.Key)
            .Select(x => new { x.Key, x.Rank, builtin = false })
            .ToListAsync(cancellationToken);
        var labels = DlpLabels.Builtin
            .Select(x => new { key = x.Key, rank = x.Rank, builtin = true })
            .Concat(custom.Select(x => new { key = x.Key, rank = x.Rank, builtin = x.builtin }))
            .ToArray();
        return DlpCommandResult.Ok(labels);
    }

    public async Task<DlpCommandResult> CreateLabelAsync(
        Workspace workspace,
        UserId actor,
        string? key,
        int rank,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        if (!DlpRules.ValidCustomKey(key) || rank is < 0 or > DlpLimits.MaxRank)
        {
            return DlpCommandResult.Fail(400, DlpErrors.LabelInvalid);
        }

        var replay = await db.DataLabels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.CreatedBy == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return DlpCommandResult.Ok(new { replay.Key, replay.Rank, builtin = false });
        }

        if (await db.DataLabels.AnyAsync(x => x.WorkspaceId == workspace.Id && x.Key == key, cancellationToken))
        {
            return DlpCommandResult.Fail(409, DlpErrors.LabelInvalid);
        }

        var row = new DataLabel
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            Key = key!,
            Rank = rank,
            CreatedBy = actor,
            CreatedAt = clock.UtcNow,
            IdempotencyKey = idempotencyKey
        };
        db.DataLabels.Add(row);
        audit.Add(Change(workspace, actor, AuditActions.DlpLabelAssign, "DataLabel", row.Id, new { key, rank }));
        await db.SaveChangesAsync(cancellationToken);
        return DlpCommandResult.Ok(new { row.Key, row.Rank, builtin = false }, 201);
    }

    public async Task<DlpCommandResult> AssignAsync(
        Workspace workspace,
        UserId actor,
        string? scopeKind,
        Guid? scopeId,
        string? labelKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        if (!DlpScopes.Known(scopeKind) || labelKey is null)
        {
            return DlpCommandResult.Fail(400, DlpErrors.ScopeInvalid);
        }

        var resolvedScopeId = scopeKind == DlpScopes.Workspace ? workspace.Id.Value : scopeId ?? Guid.Empty;
        Guid? spaceId = null;
        if (scopeKind == DlpScopes.Workspace)
        {
            if (scopeId is Guid requested && requested != workspace.Id.Value)
            {
                return DlpCommandResult.Fail(400, DlpErrors.ScopeInvalid);
            }
        }
        else if (scopeKind == DlpScopes.Space)
        {
            var space = await db.Spaces.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == resolvedScopeId && x.WorkspaceId == workspace.Id, cancellationToken);
            if (space is null)
            {
                return DlpCommandResult.Fail(404, DlpErrors.NotFound);
            }
        }
        else
        {
            var channel = await db.Channels.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == new ChannelId(resolvedScopeId) && x.WorkspaceId == workspace.Id, cancellationToken);
            if (channel is null)
            {
                return DlpCommandResult.Fail(404, DlpErrors.NotFound);
            }

            spaceId = channel.SpaceId;
        }

        var snapshot = await LoadAsync(workspace.Id, cancellationToken);
        if (!DlpLabels.TryRank(labelKey, snapshot.Ranks, out var childRank))
        {
            return DlpCommandResult.Fail(400, DlpErrors.LabelInvalid);
        }

        if (DlpRules.IsDowngrade(DlpRules.ParentRank(snapshot.Assignments, snapshot.Ranks, scopeKind!, workspace.Id.Value, spaceId), childRank))
        {
            return DlpCommandResult.Fail(409, DlpErrors.DowngradeBlocked);
        }

        var row = await db.LabelAssignments
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.ScopeKind == scopeKind && x.ScopeId == resolvedScopeId, cancellationToken);
        if (row is null)
        {
            row = new LabelAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                ScopeKind = scopeKind!,
                ScopeId = resolvedScopeId
            };
            db.LabelAssignments.Add(row);
        }

        row.LabelKey = labelKey;
        row.Version += 1;
        row.UpdatedBy = actor;
        row.UpdatedAt = clock.UtcNow;
        audit.Add(Change(workspace, actor, AuditActions.DlpLabelAssign, "LabelAssignment", row.Id, new { scopeKind, scopeId = resolvedScopeId, labelKey, version = row.Version }));
        await db.SaveChangesAsync(cancellationToken);
        return DlpCommandResult.Ok(new { row.ScopeKind, row.ScopeId, row.LabelKey, row.Version });
    }

    public async Task<DlpCommandResult> UpsertRuleAsync(
        Workspace workspace,
        UserId actor,
        string? action,
        int minRank,
        string? effect,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        if (!DlpActions.Known(action))
        {
            return DlpCommandResult.Fail(400, DlpErrors.ActionInvalid);
        }

        if (!DlpEffects.Configurable(effect) || minRank is < 0 or > DlpLimits.MaxRank)
        {
            return DlpCommandResult.Fail(400, DlpErrors.EffectInvalid);
        }

        var row = await db.DlpRules.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.Action == action, cancellationToken);
        if (row is null)
        {
            row = new DlpRule
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Action = action!
            };
            db.DlpRules.Add(row);
        }

        row.MinRank = minRank;
        row.Effect = effect!;
        row.Version += 1;
        row.UpdatedAt = clock.UtcNow;
        audit.Add(Change(workspace, actor, AuditActions.DlpRuleChange, "DlpRule", row.Id, new { action, minRank, effect, version = row.Version }));
        await db.SaveChangesAsync(cancellationToken);
        return DlpCommandResult.Ok(new { row.Action, row.MinRank, row.Effect, row.Version });
    }

    public async Task<DlpCommandResult> CreateDetectorAsync(
        Workspace workspace,
        UserId actor,
        string? name,
        string? pattern,
        int raiseRank,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < 1 or > DlpLimits.MaxNameLength || raiseRank is < 0 or > DlpLimits.MaxRank)
        {
            return DlpCommandResult.Fail(400, DlpErrors.PatternInvalid);
        }

        if (!DlpRules.TryCompile(pattern, out _))
        {
            return DlpCommandResult.Fail(400, DlpErrors.PatternInvalid);
        }

        var replay = await db.DlpDetectors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.CreatedBy == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return DlpCommandResult.Ok(DetectorView(replay));
        }

        var row = new DlpDetector
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            Name = trimmed,
            Pattern = pattern!,
            RaiseRank = raiseRank,
            CreatedBy = actor,
            CreatedAt = clock.UtcNow,
            IdempotencyKey = idempotencyKey
        };
        db.DlpDetectors.Add(row);
        audit.Add(Change(workspace, actor, AuditActions.DlpDetectorChange, "DlpDetector", row.Id, new { name = trimmed, raiseRank }));
        await db.SaveChangesAsync(cancellationToken);
        return DlpCommandResult.Ok(DetectorView(row), 201);
    }

    public async Task<DlpCommandResult> ReviewFalsePositiveAsync(
        Workspace workspace,
        UserId actor,
        Guid detectorId,
        Guid subjectId,
        string? reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        var note = (reason ?? string.Empty).Trim();
        if (note.Length is < 8 or > DlpLimits.MaxReasonLength || subjectId == Guid.Empty)
        {
            return DlpCommandResult.Fail(400, DlpErrors.LabelInvalid);
        }

        if (!await db.DlpDetectors.AnyAsync(x => x.Id == detectorId && x.WorkspaceId == workspace.Id, cancellationToken))
        {
            return DlpCommandResult.Fail(404, DlpErrors.NotFound);
        }

        var replay = await db.DlpFalsePositives.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.ReviewedBy == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return DlpCommandResult.Ok(new { replay.DetectorId, replay.SubjectId });
        }

        var row = new DlpFalsePositive
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            DetectorId = detectorId,
            SubjectId = subjectId,
            Reason = note,
            ReviewedBy = actor,
            ReviewedAt = clock.UtcNow,
            IdempotencyKey = idempotencyKey
        };
        db.DlpFalsePositives.Add(row);
        audit.Add(Change(workspace, actor, AuditActions.DlpFalsePositive, "DlpFalsePositive", row.Id, new { detectorId, subjectId }));
        await db.SaveChangesAsync(cancellationToken);
        return DlpCommandResult.Ok(new { row.DetectorId, row.SubjectId }, 201);
    }

    public async Task<DlpCommandResult> CreateExceptionAsync(
        Workspace workspace,
        UserId actor,
        string? action,
        string? scopeKind,
        Guid? scopeId,
        DateTimeOffset? expiresAt,
        string? reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        if (!DlpActions.Known(action) || !DlpScopes.Known(scopeKind))
        {
            return DlpCommandResult.Fail(400, DlpErrors.ActionInvalid);
        }

        var note = (reason ?? string.Empty).Trim();
        if (note.Length is < 8 or > DlpLimits.MaxReasonLength)
        {
            return DlpCommandResult.Fail(400, DlpErrors.LabelInvalid);
        }

        var now = clock.UtcNow;
        if (expiresAt is null || !DlpRules.ValidExceptionWindow(now, expiresAt.Value))
        {
            return DlpCommandResult.Fail(400, DlpErrors.ExpiryInvalid);
        }

        var resolvedScopeId = scopeKind == DlpScopes.Workspace ? workspace.Id.Value : scopeId ?? Guid.Empty;
        if (resolvedScopeId == Guid.Empty)
        {
            return DlpCommandResult.Fail(400, DlpErrors.ScopeInvalid);
        }

        var replay = await db.DlpExceptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.CreatedBy == actor && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return DlpCommandResult.Ok(new { replay.Id, replay.Action, replay.ExpiresAt });
        }

        var row = new DlpException
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            Action = action!,
            ScopeKind = scopeKind!,
            ScopeId = resolvedScopeId,
            ExpiresAt = expiresAt.Value,
            Reason = note,
            CreatedBy = actor,
            CreatedAt = now,
            IdempotencyKey = idempotencyKey
        };
        db.DlpExceptions.Add(row);
        audit.Add(Change(workspace, actor, AuditActions.DlpExceptionCreate, "DlpException", row.Id, new { action, scopeKind, scopeId = resolvedScopeId, expiresAt }));
        await db.SaveChangesAsync(cancellationToken);
        return DlpCommandResult.Ok(new { row.Id, row.Action, row.ScopeKind, row.ScopeId, row.ExpiresAt }, 201);
    }

    public async Task<DlpCommandResult> MetricsAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        var rows = await db.DlpDecisions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => new { x.Action, x.Effect, x.Simulation })
            .ToListAsync(cancellationToken);
        return DlpCommandResult.Ok(new
        {
            total = rows.Count,
            simulated = rows.Count(x => x.Simulation),
            blocked = rows.Count(x => !x.Simulation && x.Effect is DlpEffects.Block or DlpEffects.Quarantine),
            byAction = rows.GroupBy(x => x.Action).Select(g => new { action = g.Key, count = g.Count() }).ToArray()
        });
    }

    public async Task<DlpCommandResult> SimulateAsync(
        Workspace workspace,
        Guid? channelId,
        string? action,
        string? sample,
        Guid? subjectId,
        bool? inspectableOverride,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DlpCommandResult.Fail(404, DlpErrors.Disabled);
        }

        if (!DlpActions.Known(action))
        {
            return DlpCommandResult.Fail(400, DlpErrors.ActionInvalid);
        }

        Guid? spaceId = null;
        string? channelType = inspectableOverride == false ? ContentAuditRules.WireType : null;
        if (channelId is Guid id)
        {
            var channel = await db.Channels.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == new ChannelId(id) && x.WorkspaceId == workspace.Id, cancellationToken);
            if (channel is null)
            {
                return DlpCommandResult.Fail(404, DlpErrors.NotFound);
            }

            spaceId = channel.SpaceId;
            channelType = inspectableOverride == false ? ContentAuditRules.WireType : channel.Type.ToString();
        }

        var outcome = await EvaluateAsync(
            workspace.TenantId,
            workspace.Id,
            spaceId,
            channelId,
            channelType,
            action!,
            sample,
            subjectId,
            save: true,
            cancellationToken,
            forceSimulation: true);
        return DlpCommandResult.Ok(new
        {
            outcome.Action,
            outcome.Effect,
            outcome.ReasonCode,
            outcome.Proceed,
            outcome.Inspectable,
            outcome.LabelKey,
            outcome.MatchCount,
            review = ReviewHint
        });
    }

    private async Task<bool> ContentAuditEnabledAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var stored = await db.ContentAuditSettings.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => (bool?)x.Enabled)
            .FirstOrDefaultAsync(cancellationToken);
        return ContentAuditRules.IsEnabled(stored);
    }

    private async Task<Snapshot> LoadAsync(WorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        var simulation = await db.DlpPolicies.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => (bool?)x.Simulation)
            .FirstOrDefaultAsync(cancellationToken) ?? true;
        var labels = await db.DataLabels.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => new { x.Key, x.Rank })
            .ToListAsync(cancellationToken);
        var assignmentRows = await db.LabelAssignments.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => new { x.ScopeKind, x.ScopeId, x.LabelKey })
            .ToListAsync(cancellationToken);
        var ruleRows = await db.DlpRules.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => new { x.Action, x.MinRank, x.Effect })
            .ToListAsync(cancellationToken);
        var detectorRows = await db.DlpDetectors.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => new { x.Id, x.Pattern, x.RaiseRank, x.Enabled })
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        var exceptionRows = await db.DlpExceptions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.RevokedAt == null && x.ExpiresAt > now)
            .Select(x => new { x.Action, x.ScopeKind, x.ScopeId, x.ExpiresAt })
            .ToListAsync(cancellationToken);
        var falsePositiveRows = await db.DlpFalsePositives.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => new { x.DetectorId, x.SubjectId })
            .ToListAsync(cancellationToken);
        return new Snapshot(
            simulation,
            labels.ToDictionary(x => x.Key, x => x.Rank, StringComparer.Ordinal),
            assignmentRows.Select(x => new DlpAssignmentInput(x.ScopeKind, x.ScopeId, x.LabelKey)).ToArray(),
            ruleRows.Select(x => new DlpRuleInput(x.Action, x.MinRank, x.Effect)).ToArray(),
            detectorRows.Select(x => new DlpDetectorInput(x.Id, x.Pattern, x.RaiseRank, x.Enabled)).ToArray(),
            exceptionRows.Select(x => new DlpExceptionInput(x.Action, x.ScopeKind, x.ScopeId, x.ExpiresAt, false)).ToArray(),
            falsePositiveRows.Select(x => new DlpFalsePositiveInput(x.DetectorId, x.SubjectId)).ToArray());
    }

    private static AuditEvent Change(Workspace workspace, UserId actor, string action, string entityType, Guid entityId, object metadata) =>
        new()
        {
            TenantId = workspace.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            MetadataJson = JsonSerializer.Serialize(metadata)
        };

    private static object DetectorView(DlpDetector row) =>
        new { row.Id, row.Name, row.Pattern, row.RaiseRank, row.Enabled };

    private sealed record Snapshot(
        bool Simulation,
        IReadOnlyDictionary<string, int> Ranks,
        IReadOnlyList<DlpAssignmentInput> Assignments,
        IReadOnlyList<DlpRuleInput> Rules,
        IReadOnlyList<DlpDetectorInput> Detectors,
        IReadOnlyList<DlpExceptionInput> Exceptions,
        IReadOnlyList<DlpFalsePositiveInput> FalsePositives);
}

public sealed record DlpCommandResult(int Status, string? Error, object? Body)
{
    public static DlpCommandResult Ok(object body, int status = 200) => new(status, null, body);

    public static DlpCommandResult Fail(int status, string error) => new(status, error, null);
}
