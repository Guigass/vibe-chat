using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class PolicyPackEndpoints
{
    internal static void MapPolicyPacks(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/policy-packs/catalog", CatalogAsync)
            .RequirePermission(Permissions.Compliance.Policy);
        v1.MapGet("/workspaces/{workspaceId:guid}/policy-packs", CurrentAsync)
            .RequirePermission(Permissions.Compliance.Policy);
        v1.MapPost("/workspaces/{workspaceId:guid}/policy-packs/preview", PreviewAsync)
            .RequirePermission(Permissions.Compliance.Policy);
        v1.MapPost("/workspaces/{workspaceId:guid}/policy-packs/apply", ApplyAsync)
            .RequirePermission(Permissions.Compliance.Policy);
        v1.MapPost("/workspaces/{workspaceId:guid}/policy-packs/rollback", RollbackAsync)
            .RequirePermission(Permissions.Compliance.Policy);
        v1.MapGet("/workspaces/{workspaceId:guid}/policy-packs/export", ExportAsync)
            .RequirePermission(Permissions.Compliance.Policy);
        v1.MapPost("/workspaces/{workspaceId:guid}/policy-packs/import", ImportAsync)
            .RequirePermission(Permissions.Compliance.Policy);
        v1.MapGet("/workspaces/{workspaceId:guid}/policy-packs/drift", DriftAsync)
            .RequirePermission(Permissions.Compliance.Policy);
    }

    private static Task<IResult> CatalogAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<PolicyPackOptions> options, PolicyPackService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, _, _, _) => Task.FromResult(svc.Catalog()), ct);

    private static Task<IResult> CurrentAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<PolicyPackOptions> options, PolicyPackService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.CurrentAsync(workspace, token), ct);

    private static Task<IResult> PreviewAsync(Guid workspaceId, ApplyBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<PolicyPackOptions> options, PolicyPackService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, _, _, _) => Task.FromResult(svc.Preview(body.PackId, body.Version, body.Overrides)), ct);

    private static async Task<IResult> ApplyAsync(Guid workspaceId, ApplyBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<PolicyPackOptions> options, PolicyPackService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, PolicyPackErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.ApplyAsync(workspace, profile.Id, body.PackId, body.Version, body.Overrides, body.Confirm, key, token), ct);
    }

    private static async Task<IResult> RollbackAsync(Guid workspaceId, ConfirmBody body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<PolicyPackOptions> options, PolicyPackService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, PolicyPackErrors.IdempotencyRequired);
        }

        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.RollbackAsync(workspace, profile.Id, body.Confirm, key, token), ct);
    }

    private static Task<IResult> ExportAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<PolicyPackOptions> options, PolicyPackService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.ExportAsync(workspace, token), ct);

    private static async Task<IResult> ImportAsync(Guid workspaceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<PolicyPackOptions> options, PolicyPackService service, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Problem(400, PolicyPackErrors.IdempotencyRequired);
        }

        var confirm = body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("confirm", out var flag)
            && flag.ValueKind == JsonValueKind.True;
        return await RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, profile, token) =>
            svc.ImportAsync(workspace, profile.Id, body, confirm, key, token), ct);
    }

    private static Task<IResult> DriftAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IClock clock, IOptions<PolicyPackOptions> options, PolicyPackService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, permissions, clock, options, service, (svc, workspace, _, token) => svc.DriftAsync(workspace, token), ct);

    private static async Task<IResult> RunAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IClock clock,
        IOptions<PolicyPackOptions> options,
        PolicyPackService service,
        Func<PolicyPackService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, CancellationToken, Task<PolicyPackCommandResult>> command,
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
            return Problem(404, PolicyPackErrors.Disabled);
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
        return key.Length <= PolicyPackLimits.MaxIdempotencyKeyLength;
    }

    private static IResult Problem(int status, string error) =>
        Results.Json(new { error }, statusCode: status);

    private sealed class ApplyBody
    {
        public string? PackId { get; set; }
        public int Version { get; set; }
        public JsonElement Overrides { get; set; }
        public bool Confirm { get; set; }
    }

    private sealed class ConfirmBody
    {
        public bool Confirm { get; set; }
    }
}
