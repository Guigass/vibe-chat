using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure.Compliance;

public sealed class DelegatedAdminService(
    VibeChatDbContext db,
    IOptions<DelegatedAdminOptions> options,
    IAuditWriter audit,
    IClock clock,
    IPermissionChecker permissions,
    ILogger<DelegatedAdminService> logger)
{
    public bool Enabled => options.Value.Enabled;

    public DelegatedAdminCommandResult Catalog() =>
        Enabled
            ? DelegatedAdminCommandResult.Ok(new
            {
                scopes = DelegatedScopes.All,
                resources = DelegatedResources.All.Select(resource => new
                {
                    resource,
                    unit = DelegatedResources.Unit(resource)
                }),
                certification = false
            })
            : DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);

    public async Task<DelegatedAdminCommandResult> MeAsync(Workspace workspace, UserId userId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);
        }

        await SweepAsync(workspace.TenantId, workspace.Id, cancellationToken);
        var (root, held) = await LoadAsync(workspace.Id, userId, cancellationToken);
        return DelegatedAdminCommandResult.Ok(new
        {
            scopes = held.Select(ViewScope),
            areas = DelegatedAdminRules.Areas(root, held),
            canGrant = DelegatedAdminRules.CanGrantSomething(root, held)
        });
    }

    public async Task<DelegatedAdminCommandResult> ListAssignmentsAsync(Workspace workspace, UserId userId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);
        }

        await SweepAsync(workspace.TenantId, workspace.Id, cancellationToken);
        var (root, held) = await LoadAsync(workspace.Id, userId, cancellationToken);
        if (!DelegatedAdminRules.CanView(root, held))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ScopeExceeded);
        }

        var now = clock.UtcNow;
        var rows = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
            .OrderBy(x => x.GrantedAt)
            .ToListAsync(cancellationToken);
        return DelegatedAdminCommandResult.Ok(new { assignments = rows.Select(View) });
    }

    public async Task<DelegatedAdminCommandResult> GrantAsync(
        Workspace workspace,
        UserId actorId,
        Guid granteeId,
        string? scope,
        Guid? spaceId,
        string? reason,
        DateTimeOffset? expiresAt,
        bool breakGlass,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > DelegatedAdminLimits.MaxIdempotencyKeyLength)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.IdempotencyRequired);
        }

        if (!DelegatedAdminRules.TryReason(reason, out var cleanReason))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ReasonRequired);
        }

        if (!DelegatedAdminRules.TryScope(scope, spaceId, out var canonical, out var spaceKey))
        {
            return DelegatedAdminCommandResult.Fail(
                DelegatedScopes.IsKnown(scope) ? DelegatedAdminErrors.SpaceInvalid : DelegatedAdminErrors.ScopeUnknown);
        }

        var now = clock.UtcNow;
        if (!DelegatedAdminRules.TryExpiry(now, expiresAt, breakGlass))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ExpiryInvalid);
        }

        var grantee = new UserId(granteeId);
        var hash = DelegatedAdminRules.Hash(granteeId, canonical, spaceKey, cleanReason, expiresAt, breakGlass);
        var replay = await db.ScopedRoleAssignments
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.GrantedBy == actorId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return replay.RequestHash == hash
                ? DelegatedAdminCommandResult.Ok(JsonSerializer.Deserialize<JsonElement>(replay.ResultJson))
                : DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.IdempotencyConflict);
        }

        if (grantee == actorId)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.SelfGrant);
        }

        var member = await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.UserId == grantee, cancellationToken);
        if (!member)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.MemberRequired);
        }

        if (canonical == DelegatedScopes.Space
            && !await db.Spaces.AsNoTracking().AnyAsync(x => x.Id == spaceKey && x.WorkspaceId == workspace.Id, cancellationToken))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.SpaceInvalid);
        }

        await SweepAsync(workspace.TenantId, workspace.Id, cancellationToken);
        var (root, held) = await LoadAsync(workspace.Id, actorId, cancellationToken);
        if (!DelegatedAdminRules.CanGrant(root, held, canonical, spaceKey, self: false))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ScopeExceeded);
        }

        var active = await db.ScopedRoleAssignments.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == workspace.Id
                && x.GranteeId == grantee
                && x.Scope == canonical
                && x.SpaceKey == spaceKey
                && x.RevokedAt == null,
            cancellationToken);
        if (active)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.AlreadyAssigned);
        }

        var row = new ScopedRoleAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            GranteeId = grantee,
            Scope = canonical,
            SpaceKey = spaceKey,
            Reason = cleanReason,
            ExpiresAt = expiresAt,
            GrantedBy = actorId,
            GrantedAt = now,
            BreakGlass = breakGlass,
            IdempotencyKey = idempotencyKey,
            RequestHash = hash
        };
        var view = View(row);
        row.ResultJson = JsonSerializer.Serialize(view);
        db.ScopedRoleAssignments.Add(row);
        if (breakGlass)
        {
            db.QuotaAlerts.Add(new QuotaAlert
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Resource = canonical,
                Kind = DelegatedAdminAlerts.BreakGlass,
                AssignmentId = row.Id,
                CreatedAt = now
            });
            VibeChatMetrics.BreakGlassOpened.Add(1);
        }

        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actorId,
            Action = breakGlass ? AuditActions.DelegationBreakGlass : AuditActions.DelegationGrant,
            EntityType = nameof(ScopedRoleAssignment),
            EntityId = row.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                scope = canonical,
                spaceId = spaceKey == Guid.Empty ? (Guid?)null : spaceKey,
                granteeId,
                breakGlass,
                expiresAt
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        return DelegatedAdminCommandResult.Ok(view, 201);
    }

    public async Task<DelegatedAdminCommandResult> RevokeAsync(
        Workspace workspace,
        UserId actorId,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);
        }

        await SweepAsync(workspace.TenantId, workspace.Id, cancellationToken);
        var row = await db.ScopedRoleAssignments
            .FirstOrDefaultAsync(x => x.Id == assignmentId && x.WorkspaceId == workspace.Id && x.RevokedAt == null, cancellationToken);
        if (row is null)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.NotFound);
        }

        var (root, held) = await LoadAsync(workspace.Id, actorId, cancellationToken);
        if (!DelegatedAdminRules.CanGrant(root, held, row.Scope, row.SpaceKey, self: false))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ScopeExceeded);
        }

        row.RevokedAt = clock.UtcNow;
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actorId,
            Action = AuditActions.DelegationRevoke,
            EntityType = nameof(ScopedRoleAssignment),
            EntityId = row.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { scope = row.Scope, granteeId = row.GranteeId.Value })
        });
        await db.SaveChangesAsync(cancellationToken);
        return DelegatedAdminCommandResult.Ok(new { id = row.Id, revoked = true });
    }

    public async Task<DelegatedAdminCommandResult> SpaceAsync(
        Workspace workspace,
        UserId userId,
        Guid spaceId,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);
        }

        await SweepAsync(workspace.TenantId, workspace.Id, cancellationToken);
        var exists = await db.Spaces.AsNoTracking().AnyAsync(x => x.Id == spaceId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (!exists)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.SpaceInvalid);
        }

        var (root, held) = await LoadAsync(workspace.Id, userId, cancellationToken);
        return DelegatedAdminRules.CanManageSpace(root, held, spaceId)
            ? DelegatedAdminCommandResult.Ok(new { spaceId, allowed = true })
            : DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ScopeExceeded);
    }

    public async Task<DelegatedAdminCommandResult> PutQuotaAsync(
        Workspace workspace,
        UserId actorId,
        string? resource,
        long softLimit,
        long hardLimit,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);
        }

        if (!DelegatedResources.IsKnown(resource))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.QuotaResourceUnknown);
        }

        if (!DelegatedAdminRules.TryLimits(softLimit, hardLimit))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.QuotaInvalidLimit);
        }

        var (root, held) = await LoadAsync(workspace.Id, actorId, cancellationToken);
        if (!DelegatedAdminRules.CanEditQuotas(root, held))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ScopeExceeded);
        }

        var row = await db.ResourceQuotas.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.Resource == resource, cancellationToken);
        if (row is null)
        {
            row = new ResourceQuota
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Resource = resource!
            };
            db.ResourceQuotas.Add(row);
        }

        row.SoftLimit = softLimit;
        row.HardLimit = hardLimit;
        row.UpdatedBy = actorId;
        row.UpdatedAt = clock.UtcNow;
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actorId,
            Action = AuditActions.QuotaUpdate,
            EntityType = nameof(ResourceQuota),
            EntityId = row.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { resource, softLimit, hardLimit })
        });
        await db.SaveChangesAsync(cancellationToken);
        return DelegatedAdminCommandResult.Ok(QuotaView(row));
    }

    public async Task<DelegatedAdminCommandResult> UsageAsync(Workspace workspace, UserId userId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);
        }

        var (root, held) = await LoadAsync(workspace.Id, userId, cancellationToken);
        if (!DelegatedAdminRules.CanView(root, held) && !await HasAdminRoleAsync(workspace.TenantId, userId, cancellationToken))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ScopeExceeded);
        }

        var quotas = await db.ResourceQuotas.AsNoTracking().Where(x => x.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);
        var counters = await db.QuotaCounters.AsNoTracking().Where(x => x.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);
        var resources = DelegatedResources.All.Select(resource =>
        {
            var quota = quotas.FirstOrDefault(x => x.Resource == resource);
            var counter = counters.FirstOrDefault(x => x.Resource == resource);
            var used = counter?.Used ?? 0;
            var previous = counter?.PreviousUsed ?? 0;
            var configured = quota is not null;
            var action = DelegatedAdminRules.Action(configured, used, quota?.SoftLimit ?? 0, quota?.HardLimit ?? 0);
            return new
            {
                resource,
                unit = DelegatedResources.Unit(resource),
                used,
                previousUsed = previous,
                softLimit = quota?.SoftLimit,
                hardLimit = quota?.HardLimit,
                trend = DelegatedAdminRules.Trend(used, previous),
                action,
                reason = action switch
                {
                    "blocked" => DelegatedAdminErrors.QuotaHardLimit,
                    "warning" => "QuotaSoftLimit",
                    _ => (string?)null
                }
            };
        });
        return DelegatedAdminCommandResult.Ok(new { resources });
    }

    public async Task<DelegatedAdminCommandResult> AlertsAsync(Workspace workspace, UserId userId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);
        }

        await SweepAsync(workspace.TenantId, workspace.Id, cancellationToken);
        var (root, held) = await LoadAsync(workspace.Id, userId, cancellationToken);
        if (!DelegatedAdminRules.CanView(root, held))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ScopeExceeded);
        }

        var rows = await db.QuotaAlerts.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .Select(x => new { id = x.Id, resource = x.Resource, kind = x.Kind, createdAt = x.CreatedAt })
            .ToListAsync(cancellationToken);
        return DelegatedAdminCommandResult.Ok(new { alerts = rows });
    }

    public async Task<QuotaDecision> TryConsumeAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        string resource,
        long delta,
        CancellationToken cancellationToken)
    {
        if (!Enabled || delta <= 0 || !DelegatedResources.IsKnown(resource))
        {
            return QuotaDecision.Allow();
        }

        try
        {
            var limited = await db.ResourceQuotas.AsNoTracking()
                .AnyAsync(x => x.WorkspaceId == workspaceId && x.Resource == resource, cancellationToken);
            if (!limited)
            {
                return QuotaDecision.Allow();
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO compliance.quota_counters ("Id", "TenantId", "WorkspaceId", "Resource", "Used", "PreviousUsed")
                VALUES ({Guid.NewGuid()}, {tenantId.Value}, {workspaceId.Value}, {resource}, 0, 0)
                ON CONFLICT ("TenantId", "WorkspaceId", "Resource") DO NOTHING
                """,
                cancellationToken);

            var updated = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE compliance.quota_counters AS c
                SET "PreviousUsed" = c."Used", "Used" = c."Used" + {delta}
                FROM compliance.quotas AS q
                WHERE c."TenantId" = {tenantId.Value}
                  AND c."WorkspaceId" = {workspaceId.Value}
                  AND c."Resource" = {resource}
                  AND q."TenantId" = c."TenantId"
                  AND q."WorkspaceId" = c."WorkspaceId"
                  AND q."Resource" = c."Resource"
                  AND c."Used" + {delta} <= q."HardLimit"
                """,
                cancellationToken);
            if (updated == 0)
            {
                VibeChatMetrics.QuotaDenied.Add(1);
                await TryAlertAsync(tenantId, workspaceId, resource, DelegatedAdminAlerts.Hard, null, cancellationToken);
                return QuotaDecision.Deny(DelegatedAdminErrors.QuotaHardLimit);
            }

            VibeChatMetrics.QuotaConsumed.Add(1);
            var state = await db.QuotaCounters.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && x.Resource == resource)
                .Select(x => new { x.Used })
                .FirstAsync(cancellationToken);
            var soft = await db.ResourceQuotas.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && x.Resource == resource)
                .Select(x => x.SoftLimit)
                .FirstAsync(cancellationToken);
            if (state.Used >= soft)
            {
                await TryAlertAsync(tenantId, workspaceId, resource, DelegatedAdminAlerts.Soft, null, cancellationToken);
            }

            return QuotaDecision.Allow(consumed: true, used: state.Used);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Quota check failed open for resource {Resource}", resource);
            VibeChatMetrics.QuotaCheckFailed.Add(1);
            return QuotaDecision.Allow();
        }
    }

    public async Task<DelegatedAdminCommandResult> ReserveAsync(
        Workspace workspace,
        UserId actorId,
        string? resource,
        long delta,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.Disabled);
        }

        if (!DelegatedResources.IsKnown(resource))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.QuotaResourceUnknown);
        }

        var (root, held) = await LoadAsync(workspace.Id, actorId, cancellationToken);
        if (!DelegatedAdminRules.CanEditQuotas(root, held))
        {
            return DelegatedAdminCommandResult.Fail(DelegatedAdminErrors.ScopeExceeded);
        }

        var decision = await TryConsumeAsync(workspace.TenantId, workspace.Id, resource!, delta, cancellationToken);
        return decision.Allowed
            ? DelegatedAdminCommandResult.Ok(new { allowed = true, consumed = decision.Consumed, used = decision.Used })
            : DelegatedAdminCommandResult.Fail(decision.Error ?? DelegatedAdminErrors.QuotaHardLimit);
    }

    public async Task<bool> AllowsPermissionAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        UserId userId,
        string permission,
        Guid? spaceId,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return false;
        }

        if (permission == Permissions.Billing.Read
            && await permissions.HasPermissionAsync(tenantId, userId, Permissions.Workspace.Admin, cancellationToken))
        {
            return true;
        }

        await SweepAsync(tenantId, workspaceId, cancellationToken);
        var (_, held) = await LoadAsync(workspaceId, userId, cancellationToken);
        return DelegatedAdminRules.Allows(held, permission, spaceId);
    }

    private async Task<(bool Root, List<HeldScope> Held)> LoadAsync(WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken)
    {
        var role = await db.WorkspaceMembers.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .Select(x => (Role?)x.Role)
            .FirstOrDefaultAsync(cancellationToken);
        var root = role is Role.WorkspaceOwner or Role.PlatformOwner;
        var held = new List<HeldScope>();
        if (role == Role.Admin)
        {
            held.Add(new HeldScope(DelegatedScopes.Workspace, Guid.Empty));
        }

        var now = clock.UtcNow;
        var rows = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.GranteeId == userId && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
            .Select(x => new { x.Scope, x.SpaceKey })
            .ToListAsync(cancellationToken);
        held.AddRange(rows.Select(x => new HeldScope(x.Scope, x.SpaceKey)));
        return (root, held);
    }

    private Task<bool> HasAdminRoleAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken) =>
        permissions.HasPermissionAsync(tenantId, userId, Permissions.Workspace.Admin, cancellationToken);

    private async Task SweepAsync(TenantId tenantId, WorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var expired = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.RevokedAt == null && x.BreakGlass && x.ExpiresAt != null && x.ExpiresAt <= now)
            .Select(x => new { x.Id, x.Scope })
            .ToListAsync(cancellationToken);
        foreach (var row in expired)
        {
            await TryAlertAsync(tenantId, workspaceId, row.Scope, DelegatedAdminAlerts.BreakGlassExpired, row.Id, cancellationToken);
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE compliance.scoped_role_assignments
            SET "RevokedAt" = {now}
            WHERE "TenantId" = {tenantId.Value}
              AND "WorkspaceId" = {workspaceId.Value}
              AND "RevokedAt" IS NULL
              AND "ExpiresAt" IS NOT NULL
              AND "ExpiresAt" <= {now}
            """,
            cancellationToken);
    }

    private async Task TryAlertAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        string resource,
        string kind,
        Guid? assignmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = clock.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO compliance.quota_alerts ("Id", "TenantId", "WorkspaceId", "Resource", "Kind", "AssignmentId", "CreatedAt")
                SELECT {Guid.NewGuid()}, {tenantId.Value}, {workspaceId.Value}, {resource}, {kind}, {assignmentId}, {now}
                WHERE NOT EXISTS (
                    SELECT 1 FROM compliance.quota_alerts
                    WHERE "TenantId" = {tenantId.Value}
                      AND "WorkspaceId" = {workspaceId.Value}
                      AND "Resource" = {resource}
                      AND "Kind" = {kind}
                      AND "AssignmentId" IS NOT DISTINCT FROM {assignmentId}
                )
                """,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Quota alert was not recorded for resource {Resource}", resource);
        }
    }

    private static object ViewScope(HeldScope scope) => new
    {
        scope = scope.Scope,
        spaceId = scope.SpaceKey == Guid.Empty ? (Guid?)null : scope.SpaceKey
    };

    private static object View(ScopedRoleAssignment row) => new
    {
        id = row.Id,
        granteeId = row.GranteeId.Value,
        scope = row.Scope,
        spaceId = row.SpaceKey == Guid.Empty ? (Guid?)null : row.SpaceKey,
        reason = row.Reason,
        expiresAt = row.ExpiresAt,
        breakGlass = row.BreakGlass,
        grantedAt = row.GrantedAt,
        grantedBy = row.GrantedBy.Value
    };

    private static object QuotaView(ResourceQuota row) => new
    {
        resource = row.Resource,
        unit = DelegatedResources.Unit(row.Resource),
        softLimit = row.SoftLimit,
        hardLimit = row.HardLimit
    };
}
