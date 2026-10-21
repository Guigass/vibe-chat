using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class DelegatedAdminEndpoints
{
    internal static void MapDelegatedAdmin(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/delegated-admin/catalog", CatalogAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/delegated-admin/me", MeAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/delegated-admin/assignments", ListAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/delegated-admin/assignments", GrantAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/delegated-admin/assignments/{assignmentId:guid}/revoke", RevokeAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/delegated-admin/spaces/{spaceId:guid}", SpaceAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/quotas", UsageAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/quotas/usage", UsageAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapPut("/workspaces/{workspaceId:guid}/quotas/{resource}", PutQuotaAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/quotas/{resource}/reserve", ReserveAsync)
            .RequirePermission(Permissions.Workspace.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/delegated-admin/alerts", AlertsAsync)
            .RequirePermission(Permissions.Workspace.Read);
    }

    private static Task<IResult> CatalogAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, _, _, _) => Task.FromResult(svc.Catalog()), service, ct);

    private static Task<IResult> MeAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, workspace, profile, token) => svc.MeAsync(workspace, profile.Id, token), service, ct);

    private static Task<IResult> ListAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, workspace, profile, token) => svc.ListAssignmentsAsync(workspace, profile.Id, token), service, ct);

    private static Task<IResult> GrantAsync(Guid workspaceId, GrantBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Task.FromResult(Problem(400, DelegatedAdminErrors.IdempotencyRequired));
        }

        return RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, workspace, profile, token) =>
            svc.GrantAsync(workspace, profile.Id, body.GranteeId, body.Scope, body.SpaceId, body.Reason, body.ExpiresAt, body.BreakGlass, key, token), service, ct);
    }

    private static Task<IResult> RevokeAsync(Guid workspaceId, Guid assignmentId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, workspace, profile, token) => svc.RevokeAsync(workspace, profile.Id, assignmentId, token), service, ct);

    private static Task<IResult> SpaceAsync(Guid workspaceId, Guid spaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, workspace, profile, token) => svc.SpaceAsync(workspace, profile.Id, spaceId, token), service, ct);

    private static Task<IResult> UsageAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, workspace, profile, token) => svc.UsageAsync(workspace, profile.Id, token), service, ct);

    private static Task<IResult> PutQuotaAsync(Guid workspaceId, string resource, QuotaBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, workspace, profile, token) => svc.PutQuotaAsync(workspace, profile.Id, resource, body.SoftLimit, body.HardLimit, token), service, ct);

    private static Task<IResult> ReserveAsync(Guid workspaceId, string resource, ReserveBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, workspace, profile, token) => svc.ReserveAsync(workspace, profile.Id, resource, body.Delta, token), service, ct);

    private static Task<IResult> AlertsAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, DelegatedAdminService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, (svc, workspace, profile, token) => svc.AlertsAsync(workspace, profile.Id, token), service, ct);

    private static async Task<IResult> RunAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        Func<DelegatedAdminService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, CancellationToken, Task<DelegatedAdminCommandResult>> command,
        DelegatedAdminService service,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null || !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Workspace.Read, ct))
        {
            return Results.Forbid();
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
        return key.Length <= DelegatedAdminLimits.MaxIdempotencyKeyLength;
    }

    private static IResult Problem(int status, string error) =>
        Results.Json(new { error }, statusCode: status);

    private sealed class GrantBody
    {
        public Guid GranteeId { get; set; }
        public string? Scope { get; set; }
        public Guid? SpaceId { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool BreakGlass { get; set; }
    }

    private sealed class QuotaBody
    {
        public long SoftLimit { get; set; }
        public long HardLimit { get; set; }
    }

    private sealed class ReserveBody
    {
        public long Delta { get; set; }
    }
}
