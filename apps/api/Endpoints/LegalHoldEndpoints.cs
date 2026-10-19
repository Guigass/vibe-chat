using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class LegalHoldEndpoints
{
    internal static void MapCompliance(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/compliance/access", AccessAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/compliance/grants", ListGrantsAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/compliance/grants", GrantAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapDelete("/workspaces/{workspaceId:guid}/compliance/grants/{grantId:guid}", RevokeAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapGet("/workspaces/{workspaceId:guid}/compliance/holds", ListHoldsAsync)
            .RequirePermission(Permissions.Compliance.Hold);
        v1.MapPost("/workspaces/{workspaceId:guid}/compliance/holds", RequestHoldAsync)
            .RequirePermission(Permissions.Compliance.Hold);
        v1.MapPost("/workspaces/{workspaceId:guid}/compliance/holds/{holdId:guid}/approve", ApproveHoldAsync)
            .RequirePermission(Permissions.Compliance.Hold);
        v1.MapPost("/workspaces/{workspaceId:guid}/compliance/holds/{holdId:guid}/release", RequestReleaseAsync)
            .RequirePermission(Permissions.Compliance.Hold);
        v1.MapPost("/workspaces/{workspaceId:guid}/compliance/holds/{holdId:guid}/release/approve", ApproveReleaseAsync)
            .RequirePermission(Permissions.Compliance.Hold);
        v1.MapGet("/workspaces/{workspaceId:guid}/compliance/cases", ListCasesAsync)
            .RequirePermission(Permissions.Compliance.Discovery);
        v1.MapPost("/workspaces/{workspaceId:guid}/compliance/cases", CreateCaseAsync)
            .RequirePermission(Permissions.Compliance.Discovery);
        v1.MapPost("/workspaces/{workspaceId:guid}/compliance/cases/{caseId:guid}/exports", ExportAsync)
            .RequirePermission(Permissions.Compliance.Discovery);
        v1.MapGet("/workspaces/{workspaceId:guid}/compliance/cases/{caseId:guid}/exports/{exportId:guid}", GetExportAsync)
            .RequirePermission(Permissions.Compliance.Discovery);
    }

    private static async Task<IResult> AccessAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<LegalHoldOptions> options,
        LegalHoldService service,
        CancellationToken ct)
    {
        var access = await OpenAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var canGrant = await permissions.HasPermissionAsync(access.Workspace!.TenantId, access.Profile!.Id, Permissions.Workspace.Admin, ct);
        return ToResult(await service.AccessAsync(access.Workspace, access.Profile.Id, canGrant, ct));
    }

    private static Task<IResult> ListGrantsAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.ListGrantsAsync(workspace, token), ct);

    private static async Task<IResult> GrantAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct)
    {
        var access = await OpenAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!TryKey(http, out var key))
        {
            return Problem(400, LegalHoldErrors.IdempotencyRequired);
        }

        var body = await http.Request.ReadFromJsonAsync<GrantBody>(ct);
        if (body is null)
        {
            return Problem(400, LegalHoldErrors.CapabilityInvalid);
        }

        return ToResult(await service.GrantAsync(access.Workspace!, access.Profile!.Id, body.UserId, body.Capability, key, ct));
    }

    private static async Task<IResult> RevokeAsync(Guid workspaceId, Guid grantId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct)
    {
        var access = await OpenAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!TryKey(http, out var key))
        {
            return Problem(400, LegalHoldErrors.IdempotencyRequired);
        }

        return ToResult(await service.RevokeAsync(access.Workspace!, access.Profile!.Id, grantId, key, ct));
    }

    private static Task<IResult> ListHoldsAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.ListHoldsAsync(workspace, token), ct);

    private static async Task<IResult> RequestHoldAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct)
    {
        var access = await OpenAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!TryKey(http, out var key))
        {
            return Problem(400, LegalHoldErrors.IdempotencyRequired);
        }

        var body = await http.Request.ReadFromJsonAsync<HoldBody>(ct) ?? new HoldBody();
        return ToResult(await service.RequestHoldAsync(
            access.Workspace!,
            access.Profile!.Id,
            body.ScopeKind,
            body.ScopeId,
            body.IntervalStart,
            body.IntervalEnd,
            body.Reason,
            body.ExpiresAt,
            body.Confirmed,
            body.Acknowledged,
            key,
            ct));
    }

    private static Task<IResult> ApproveHoldAsync(Guid workspaceId, Guid holdId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct) =>
        ConfirmedAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, body, key, token) =>
            svc.ApproveHoldAsync(workspace, profile.Id, holdId, body.Confirmed, body.Acknowledged, key, token), ct);

    private static async Task<IResult> RequestReleaseAsync(Guid workspaceId, Guid holdId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct)
    {
        var access = await OpenAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!TryKey(http, out var key))
        {
            return Problem(400, LegalHoldErrors.IdempotencyRequired);
        }

        var body = await http.Request.ReadFromJsonAsync<ReleaseBody>(ct) ?? new ReleaseBody();
        return ToResult(await service.RequestReleaseAsync(access.Workspace!, access.Profile!.Id, holdId, body.Reason, body.Confirmed, body.Acknowledged, key, ct));
    }

    private static Task<IResult> ApproveReleaseAsync(Guid workspaceId, Guid holdId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct) =>
        ConfirmedAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, body, key, token) =>
            svc.ApproveReleaseAsync(workspace, profile.Id, holdId, body.Confirmed, body.Acknowledged, key, token), ct);

    private static Task<IResult> ListCasesAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.ListCasesAsync(workspace, token), ct);

    private static async Task<IResult> CreateCaseAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct)
    {
        var access = await OpenAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!TryKey(http, out var key))
        {
            return Problem(400, LegalHoldErrors.IdempotencyRequired);
        }

        var body = await http.Request.ReadFromJsonAsync<CaseBody>(ct) ?? new CaseBody();
        return ToResult(await service.CreateCaseAsync(
            access.Workspace!,
            access.Profile!.Id,
            body.Title,
            body.Reason,
            body.Custodians,
            body.ChannelIds,
            body.From,
            body.To,
            body.Text,
            key,
            ct));
    }

    private static async Task<IResult> ExportAsync(Guid workspaceId, Guid caseId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct)
    {
        var access = await OpenAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!TryKey(http, out var key))
        {
            return Problem(400, LegalHoldErrors.IdempotencyRequired);
        }

        var body = await http.Request.ReadFromJsonAsync<ConfirmBody>(ct) ?? new ConfirmBody();
        return ToResult(await service.ExportAsync(access.Workspace!, access.Profile!.Id, caseId, body.Confirmed, body.Acknowledged, key, ct));
    }

    private static Task<IResult> GetExportAsync(Guid workspaceId, Guid caseId, Guid exportId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<LegalHoldOptions> options, LegalHoldService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.GetExportAsync(workspace, caseId, exportId, token), ct);

    private static async Task<IResult> ConfirmedAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<LegalHoldOptions> options,
        LegalHoldService service,
        Func<LegalHoldService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, ConfirmBody, string, CancellationToken, Task<ComplianceResult>> command,
        CancellationToken ct)
    {
        var access = await OpenAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!TryKey(http, out var key))
        {
            return Problem(400, LegalHoldErrors.IdempotencyRequired);
        }

        var body = await http.Request.ReadFromJsonAsync<ConfirmBody>(ct) ?? new ConfirmBody();
        return ToResult(await command(service, access.Workspace!, access.Profile!, body, key, ct));
    }

    private static async Task<IResult> RunAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<LegalHoldOptions> options,
        LegalHoldService service,
        Func<LegalHoldService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, CancellationToken, Task<ComplianceResult>> command,
        CancellationToken ct)
    {
        var access = await OpenAsync(workspaceId, http, db, tenant, permissions, clock, options, ct);
        return access.Error ?? ToResult(await command(service, access.Workspace!, access.Profile!, ct));
    }

    private static async Task<(IResult? Error, VibeChat.Identity.UserProfile? Profile, VibeChat.Tenancy.Workspace? Workspace)> OpenAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<LegalHoldOptions> options,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return (Results.Forbid(), null, null);
        }

        if (!await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Workspace.Read, ct))
        {
            return (Results.Forbid(), null, null);
        }

        if (!options.Value.Enabled)
        {
            return (Problem(404, LegalHoldErrors.Disabled), null, null);
        }

        return (null, profile, workspace);
    }

    private static bool TryKey(HttpContext http, out string key)
    {
        key = string.Empty;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]))
        {
            return false;
        }

        key = keys[0]!;
        return key.Length <= LegalHoldLimits.MaxIdempotencyKeyLength;
    }

    private static IResult ToResult(ComplianceResult result) =>
        result.Error is null
            ? Results.Json(result.Body, statusCode: result.Status)
            : Problem(result.Status, result.Error);

    private static IResult Problem(int status, string error) =>
        Results.Json(new { error }, statusCode: status);

    private sealed class GrantBody
    {
        public Guid UserId { get; set; }
        public string Capability { get; set; } = string.Empty;
    }

    private sealed class HoldBody
    {
        public string ScopeKind { get; set; } = string.Empty;
        public Guid? ScopeId { get; set; }
        public DateTimeOffset? IntervalStart { get; set; }
        public DateTimeOffset? IntervalEnd { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool Confirmed { get; set; }
        public bool Acknowledged { get; set; }
    }

    private sealed class ConfirmBody
    {
        public bool Confirmed { get; set; }
        public bool Acknowledged { get; set; }
    }

    private sealed class ReleaseBody
    {
        public string? Reason { get; set; }
        public bool Confirmed { get; set; }
        public bool Acknowledged { get; set; }
    }

    private sealed class CaseBody
    {
        public string? Title { get; set; }
        public string? Reason { get; set; }
        public Guid[]? Custodians { get; set; }
        public Guid[]? ChannelIds { get; set; }
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }
        public string? Text { get; set; }
    }
}
