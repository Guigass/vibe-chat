using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class SiemEndpoints
{
    internal static void MapSiem(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/siem/destinations", ListAsync)
            .RequirePermission(Permissions.Security.AuditExport);
        v1.MapPost("/workspaces/{workspaceId:guid}/siem/destinations", CreateAsync)
            .RequirePermission(Permissions.Security.AuditExport);
        v1.MapPost("/workspaces/{workspaceId:guid}/siem/destinations/{destinationId:guid}/rotate", RotateAsync)
            .RequirePermission(Permissions.Security.AuditExport);
        v1.MapPost("/workspaces/{workspaceId:guid}/siem/destinations/{destinationId:guid}/revoke", RevokeAsync)
            .RequirePermission(Permissions.Security.AuditExport);
        v1.MapPost("/workspaces/{workspaceId:guid}/siem/destinations/{destinationId:guid}/test", TestAsync)
            .RequirePermission(Permissions.Security.AuditExport);
        v1.MapPost("/workspaces/{workspaceId:guid}/siem/destinations/{destinationId:guid}/backfill", BackfillAsync)
            .RequirePermission(Permissions.Security.AuditExport);
    }

    private static Task<IResult> ListAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<SiemOptions> options, SiemExportService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.ListAsync(workspace, token), ct);

    private static async Task<IResult> CreateAsync(Guid workspaceId, CreateBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<SiemOptions> options, SiemExportService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, SiemErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.CreateAsync(workspace, profile.Id, body.Name, body.Url, body.Transport, body.Categories, body.MinSeverity, body.Minimize, key, token), ct);
    }

    private static async Task<IResult> RotateAsync(Guid workspaceId, Guid destinationId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<SiemOptions> options, SiemExportService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, SiemErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.RotateAsync(workspace, profile.Id, destinationId, key, token), ct);
    }

    private static async Task<IResult> RevokeAsync(Guid workspaceId, Guid destinationId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<SiemOptions> options, SiemExportService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, SiemErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.RevokeAsync(workspace, profile.Id, destinationId, key, token), ct);
    }

    private static async Task<IResult> TestAsync(Guid workspaceId, Guid destinationId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<SiemOptions> options, SiemExportService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, SiemErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.TestAsync(workspace, profile.Id, destinationId, key, token), ct);
    }

    private static async Task<IResult> BackfillAsync(Guid workspaceId, Guid destinationId, BackfillBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<SiemOptions> options, SiemExportService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, SiemErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.BackfillAsync(workspace, profile.Id, destinationId, body.Since, body.Limit, key, token), ct);
    }

    private static async Task<IResult> RunAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<SiemOptions> options,
        SiemExportService service,
        Func<SiemExportService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, CancellationToken, Task<SiemCommandResult>> command,
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
            return Problem(404, SiemErrors.Disabled);
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
        return key.Length <= SiemLimits.MaxIdempotencyKeyLength;
    }

    private static IResult Problem(int status, string error) =>
        Results.Json(new { error }, statusCode: status);

    private sealed class CreateBody
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? Transport { get; set; }
        public string[]? Categories { get; set; }
        public string? MinSeverity { get; set; }
        public bool? Minimize { get; set; }
    }

    private sealed class BackfillBody
    {
        public DateTimeOffset? Since { get; set; }
        public int? Limit { get; set; }
    }
}
