using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Api;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class DiagnosticsEndpoints
{
    internal static void MapDiagnostics(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/diagnostics", PreflightAsync).RequirePermission(Permissions.Support.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/diagnostics/probes/{kind}", ProbeAsync).RequirePermission(Permissions.Support.Repair);
        v1.MapPost("/workspaces/{workspaceId:guid}/diagnostics/bundles", CreateBundleAsync).RequirePermission(Permissions.Support.Bundle);
        v1.MapGet("/workspaces/{workspaceId:guid}/diagnostics/bundles/{bundleId:guid}", DownloadAsync).RequirePermission(Permissions.Support.Bundle);
        v1.MapPost("/workspaces/{workspaceId:guid}/diagnostics/repairs", CreateRepairAsync).RequirePermission(Permissions.Support.Repair);
        v1.MapPost("/workspaces/{workspaceId:guid}/diagnostics/repairs/{repairId:guid}/apply", ApplyAsync).RequirePermission(Permissions.Support.Repair);
        v1.MapPost("/workspaces/{workspaceId:guid}/diagnostics/repairs/{repairId:guid}/cancel", CancelAsync).RequirePermission(Permissions.Support.Repair);
    }

    private static async Task<IResult> PreflightAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        SupportDiagnosticsService diagnostics,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, Permissions.Support.Read, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var report = await diagnostics.PreflightAsync(access.Workspace!, access.Role, http.TraceIdentifier, ct);
        return Results.Ok(new
        {
            verdict = report.Verdict,
            audience = report.Audience,
            observedAt = report.ObservedAt,
            correlationId = report.CorrelationId,
            checks = report.Checks.Select(View)
        });
    }

    private static async Task<IResult> ProbeAsync(
        Guid workspaceId,
        string kind,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        SupportDiagnosticsService diagnostics,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, Permissions.Support.Repair, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!ProbeKinds.IsAllowed(kind))
        {
            return Problem(400, SupportErrors.ProbeUnknown);
        }

        var probe = await diagnostics.ProbeAsync(access.Workspace!, access.Profile!.Id, kind, http.TraceIdentifier, ct);
        return Results.Ok(new { code = probe.Code, status = probe.Status, severity = probe.Severity, summary = probe.Summary, cleaned = probe.Cleaned });
    }

    private static async Task<IResult> CreateBundleAsync(
        Guid workspaceId,
        BundleBody? body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        SupportDiagnosticsService diagnostics,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, Permissions.Support.Bundle, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!TryKey(http, out var key))
        {
            return Problem(400, SupportErrors.IdempotencyRequired);
        }

        var window = Math.Clamp(body?.WindowMinutes ?? SupportBundleSchema.DefaultWindowMinutes, 5, 1440);
        var result = await diagnostics.CreateBundleAsync(access.Workspace!, access.Profile!.Id, access.Role, key, window, http.TraceIdentifier, ct);
        return BundleResult(result);
    }

    private static async Task<IResult> DownloadAsync(
        Guid workspaceId,
        Guid bundleId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        SupportDiagnosticsService diagnostics,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, Permissions.Support.Bundle, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var result = await diagnostics.DownloadAsync(access.Workspace!, bundleId, ct);
        if (result.Error is not null || result.Bundle is null)
        {
            return Problem(result.StatusCode, result.Error ?? SupportErrors.NotFound);
        }

        return Results.Text(result.Bundle.ManifestJson, "application/json");
    }

    private static async Task<IResult> CreateRepairAsync(
        Guid workspaceId,
        RepairBody? body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        SupportDiagnosticsService diagnostics,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, Permissions.Support.Repair, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!TryKey(http, out var key))
        {
            return Problem(400, SupportErrors.IdempotencyRequired);
        }

        var result = await diagnostics.CreateRepairAsync(
            access.Workspace!,
            access.Profile!.Id,
            body?.Action ?? "",
            body?.DryRun == true,
            body?.Confirm == true,
            body?.Target,
            key,
            ct);
        return RepairResult(result);
    }

    private static async Task<IResult> ApplyAsync(
        Guid workspaceId,
        Guid repairId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        SupportDiagnosticsService diagnostics,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, Permissions.Support.Repair, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        return RepairResult(await diagnostics.ApplyRepairAsync(access.Workspace!, access.Profile!.Id, repairId, ct));
    }

    private static async Task<IResult> CancelAsync(
        Guid workspaceId,
        Guid repairId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        SupportDiagnosticsService diagnostics,
        CancellationToken ct)
    {
        var access = await AccessAsync(workspaceId, http, db, tenant, permissions, clock, Permissions.Support.Repair, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        return RepairResult(await diagnostics.CancelRepairAsync(access.Workspace!, access.Profile!.Id, repairId, ct));
    }

    private static async Task<(IResult? Error, VibeChat.Identity.UserProfile? Profile, VibeChat.Tenancy.Workspace? Workspace, Role Role)> AccessAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        string permission,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, permission, ct))
        {
            return (Results.Forbid(), null, null, Role.Member);
        }

        var member = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == profile.Id, ct);
        return (null, profile, workspace, member?.Role ?? Role.Member);
    }

    private static bool TryKey(HttpContext http, out string key)
    {
        key = "";
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]) || keys[0]!.Length > 200)
        {
            return false;
        }

        key = keys[0]!;
        return true;
    }

    private static IResult BundleResult(SupportBundleResult result)
    {
        if (result.Error is not null || result.Bundle is null)
        {
            return Problem(result.StatusCode, result.Error ?? SupportErrors.NotFound);
        }

        return Results.Json(new
        {
            id = result.Bundle.Id,
            status = result.Bundle.Status,
            expiresAt = result.Bundle.ExpiresAt,
            checksum = result.Bundle.Checksum,
            downloadsRemaining = Math.Max(0, SupportBundleSchema.MaxDownloads - result.Bundle.DownloadCount),
            idempotent = result.Idempotent
        }, statusCode: result.StatusCode);
    }

    private static IResult RepairResult(SupportRepairResult result)
    {
        if (result.Error is not null || result.Job is null)
        {
            return Problem(result.StatusCode, result.Error ?? SupportErrors.ActionNotAllowed);
        }

        return Results.Json(new
        {
            id = result.Job.Id,
            action = result.Job.ActionCode,
            status = result.Job.Status,
            dryRun = result.Job.DryRun,
            estimated = result.Job.Estimated,
            written = result.Job.Written,
            idempotent = result.Idempotent
        }, statusCode: result.StatusCode);
    }

    private static object View(DiagnosticCheck check) => new
    {
        code = check.Code,
        version = check.Version,
        component = check.Component,
        status = check.Status,
        severity = check.Severity,
        summary = check.Summary,
        evidence = new
        {
            latencyMs = check.Evidence.LatencyMs,
            pending = check.Evidence.Pending,
            stale = check.Evidence.Stale,
            configured = check.Evidence.Configured,
            migration = check.Evidence.Migration,
            endpoint = check.Evidence.Endpoint
        },
        runbook = check.Runbook,
        observedAt = check.ObservedAt,
        correlationId = check.CorrelationId
    };

    private static IResult Problem(int status, string error) =>
        Results.Json(new { error }, statusCode: status);

    private sealed class BundleBody
    {
        public int WindowMinutes { get; set; } = SupportBundleSchema.DefaultWindowMinutes;
    }

    private sealed class RepairBody
    {
        public string Action { get; set; } = "";
        public bool DryRun { get; set; }
        public bool Confirm { get; set; }
        public string? Target { get; set; }
    }
}
