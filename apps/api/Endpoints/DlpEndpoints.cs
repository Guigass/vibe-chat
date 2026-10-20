using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class DlpEndpoints
{
    internal static void MapDlp(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/dlp/label", LabelAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/dlp/policy", PolicyAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapPut("/workspaces/{workspaceId:guid}/dlp/policy", SetPolicyAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapGet("/workspaces/{workspaceId:guid}/dlp/labels", ListLabelsAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapPost("/workspaces/{workspaceId:guid}/dlp/labels", CreateLabelAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapPut("/workspaces/{workspaceId:guid}/dlp/assignments", AssignAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapPut("/workspaces/{workspaceId:guid}/dlp/rules", UpsertRuleAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapPost("/workspaces/{workspaceId:guid}/dlp/detectors", CreateDetectorAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapPost("/workspaces/{workspaceId:guid}/dlp/false-positives", ReviewFalsePositiveAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapPost("/workspaces/{workspaceId:guid}/dlp/exceptions", CreateExceptionAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapGet("/workspaces/{workspaceId:guid}/dlp/metrics", MetricsAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
        v1.MapPost("/workspaces/{workspaceId:guid}/dlp/simulate", SimulateAsync)
            .RequirePermission(Permissions.Compliance.Dlp);
    }

    private static Task<IResult> LabelAsync(Guid workspaceId, Guid? channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.LabelAsync(workspace, channelId, token), ct);

    private static Task<IResult> PolicyAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.PolicyAsync(workspace, token), ct);

    private static Task<IResult> SetPolicyAsync(Guid workspaceId, PolicyBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) => svc.SetPolicyAsync(workspace, profile.Id, body.Simulation, token), ct);

    private static Task<IResult> ListLabelsAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.ListLabelsAsync(workspace, token), ct);

    private static async Task<IResult> CreateLabelAsync(Guid workspaceId, LabelBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, DlpErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.CreateLabelAsync(workspace, profile.Id, body.Key, body.Rank, key, token), ct);
    }

    private static Task<IResult> AssignAsync(Guid workspaceId, AssignmentBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.AssignAsync(workspace, profile.Id, body.ScopeKind, body.ScopeId, body.LabelKey, token), ct);

    private static Task<IResult> UpsertRuleAsync(Guid workspaceId, RuleBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.UpsertRuleAsync(workspace, profile.Id, body.Action, body.MinRank, body.Effect, token), ct);

    private static async Task<IResult> CreateDetectorAsync(Guid workspaceId, DetectorBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, DlpErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.CreateDetectorAsync(workspace, profile.Id, body.Name, body.Pattern, body.RaiseRank, key, token), ct);
    }

    private static async Task<IResult> ReviewFalsePositiveAsync(Guid workspaceId, FalsePositiveBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, DlpErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.ReviewFalsePositiveAsync(workspace, profile.Id, body.DetectorId, body.SubjectId, body.Reason, key, token), ct);
    }

    private static async Task<IResult> CreateExceptionAsync(Guid workspaceId, ExceptionBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, DlpErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.CreateExceptionAsync(workspace, profile.Id, body.Action, body.ScopeKind, body.ScopeId, body.ExpiresAt, body.Reason, key, token), ct);
    }

    private static Task<IResult> MetricsAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.MetricsAsync(workspace, token), ct);

    private static Task<IResult> SimulateAsync(Guid workspaceId, SimulateBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<DlpOptions> options, DlpService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) =>
            svc.SimulateAsync(workspace, body.ChannelId, body.Action, body.Sample, body.SubjectId, body.Inspectable, token), ct);

    private static async Task<IResult> RunAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<DlpOptions> options,
        DlpService service,
        Func<DlpService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, CancellationToken, Task<DlpCommandResult>> command,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Workspace.Read, ct))
        {
            return Results.Forbid();
        }

        if (!options.Value.Enabled)
        {
            return Problem(404, DlpErrors.Disabled);
        }

        var result = await command(service, workspace, profile, ct);
        return result.Error is null
            ? Results.Json(result.Body, statusCode: result.Status)
            : Problem(result.Status, result.Error);
    }

    private static bool TryKey(HttpContext http, out string key)
    {
        key = string.Empty;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]))
        {
            return false;
        }

        key = keys[0]!;
        return key.Length <= DlpLimits.MaxIdempotencyKeyLength;
    }

    private static IResult Problem(int status, string error) =>
        Results.Json(new { error }, statusCode: status);

    private sealed class PolicyBody
    {
        public bool Simulation { get; set; } = true;
    }

    private sealed class LabelBody
    {
        public string? Key { get; set; }
        public int Rank { get; set; }
    }

    private sealed class AssignmentBody
    {
        public string? ScopeKind { get; set; }
        public Guid? ScopeId { get; set; }
        public string? LabelKey { get; set; }
    }

    private sealed class RuleBody
    {
        public string? Action { get; set; }
        public int MinRank { get; set; }
        public string? Effect { get; set; }
    }

    private sealed class DetectorBody
    {
        public string? Name { get; set; }
        public string? Pattern { get; set; }
        public int RaiseRank { get; set; }
    }

    private sealed class FalsePositiveBody
    {
        public Guid DetectorId { get; set; }
        public Guid SubjectId { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class ExceptionBody
    {
        public string? Action { get; set; }
        public string? ScopeKind { get; set; }
        public Guid? ScopeId { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class SimulateBody
    {
        public Guid? ChannelId { get; set; }
        public string? Action { get; set; }
        public string? Sample { get; set; }
        public Guid? SubjectId { get; set; }
        public bool? Inspectable { get; set; }
    }
}

internal static class DlpGuard
{
    public static async Task<IResult?> BlockAsync(
        DlpService dlp,
        TenantId tenantId,
        WorkspaceId workspaceId,
        Guid? spaceId,
        Guid? channelId,
        string? channelType,
        string action,
        string? body,
        Guid? subjectId,
        CancellationToken cancellationToken)
    {
        var outcome = await dlp.EvaluateAsync(
            tenantId,
            workspaceId,
            spaceId,
            channelId,
            channelType,
            action,
            body,
            subjectId,
            save: true,
            cancellationToken);
        if (outcome.Proceed)
        {
            return null;
        }

        var error = outcome.Effect == DlpEffects.Quarantine ? DlpErrors.Quarantined : DlpErrors.Blocked;
        return Results.Json(
            new
            {
                error,
                reasonCode = outcome.ReasonCode,
                labelKey = outcome.LabelKey,
                action = outcome.Action,
                review = DlpService.ReviewHint
            },
            statusCode: StatusCodes.Status403Forbidden);
    }
}
