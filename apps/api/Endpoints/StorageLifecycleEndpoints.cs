using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class StorageLifecycleEndpoints
{
    internal static void MapStorageLifecycle(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/storage", UsageAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/storage/limits", LimitsAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/storage/reservations", ReserveAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/storage/objects", RegisterAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/storage/reconcile", ReconcileAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/storage/gc", CollectAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/storage/restore", RestoreAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/storage/objects/{objectId:guid}/archive", ArchiveAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/workspaces/{workspaceId:guid}/storage/objects/{objectId:guid}/purge", PurgeAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapGet("/workspaces/{workspaceId:guid}/storage/objects/{objectId:guid}/cdn", IssueCdnAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/storage/cdn", ReadCdnAsync)
            .RequirePermission(Permissions.Message.Read);
    }

    private static async Task<IResult> UsageAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var workspace = await OpenAsync(workspaceId, http, db, tenant, clock, ct);
        return workspace is null ? Results.Forbid() : Result(await storage.UsageAsync(workspace, ct));
    }

    private static async Task<IResult> LimitsAsync(
        Guid workspaceId,
        StorageLimitsRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        return Result(await storage.SetLimitsAsync(workspace, profile.Id, request.Kind, request.SoftLimit, request.HardLimit, ct));
    }

    private static async Task<IResult> ReserveAsync(
        Guid workspaceId,
        StorageReserveRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var workspace = await OpenAsync(workspaceId, http, db, tenant, clock, ct);
        return workspace is null
            ? Results.Forbid()
            : Result(await storage.ReserveAsync(workspace, request.Kind, request.Bytes, ct));
    }

    private static async Task<IResult> RegisterAsync(
        Guid workspaceId,
        StorageObjectRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        return Result(await storage.RegisterAsync(
            workspace,
            profile.Id,
            request.Kind,
            request.Bytes,
            request.Referenced,
            request.Clean,
            request.StorageKey,
            request.ChannelId,
            ct));
    }

    private static async Task<IResult> ReconcileAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        return workspace is null
            ? Results.Forbid()
            : Result(await storage.ReconcileAsync(workspace, profile.Id, Key(http), ct));
    }

    private static async Task<IResult> CollectAsync(
        Guid workspaceId,
        StorageGcRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        return workspace is null
            ? Results.Forbid()
            : Result(await storage.CollectAsync(workspace, profile.Id, request.DryRun, Key(http), ct));
    }

    private static async Task<IResult> RestoreAsync(
        Guid workspaceId,
        StorageRestoreRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var workspace = await OpenAsync(workspaceId, http, db, tenant, clock, ct);
        return workspace is null
            ? Results.Forbid()
            : Result(await storage.RestoreAsync(workspace, request.ObjectId, Key(http), ct));
    }

    private static async Task<IResult> ArchiveAsync(
        Guid workspaceId,
        Guid objectId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var workspace = await OpenAsync(workspaceId, http, db, tenant, clock, ct);
        return workspace is null ? Results.Forbid() : Result(await storage.ArchiveAsync(workspace, objectId, ct));
    }

    private static async Task<IResult> PurgeAsync(
        Guid workspaceId,
        Guid objectId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        return workspace is null
            ? Results.Forbid()
            : Result(await storage.PurgeAsync(workspace, profile.Id, objectId, Key(http), ct));
    }

    private static async Task<IResult> IssueCdnAsync(
        Guid workspaceId,
        Guid objectId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var workspace = await OpenAsync(workspaceId, http, db, tenant, clock, ct);
        return workspace is null ? Results.Forbid() : Result(await storage.IssueCdnAsync(workspace, objectId, ct));
    }

    private static async Task<IResult> ReadCdnAsync(
        string? token,
        HttpContext http,
        VibeChatDbContext db,
        IClock clock,
        StorageLifecycleService storage,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        return Result(await storage.ReadCdnAsync(token, profile.Id, ct));
    }

    private static async Task<Workspace?> OpenAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        return await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    }

    private static string? Key(HttpContext http) =>
        http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) && keys.Count == 1
            ? keys[0]
            : null;

    private static IResult Result(StorageCommandResult result) =>
        result.Error is null
            ? Results.Json(result.Body, statusCode: result.Status)
            : Results.Json(new { error = result.Error }, statusCode: result.Status);
}

internal sealed record StorageLimitsRequest(string? Kind, long SoftLimit, long HardLimit);

internal sealed record StorageReserveRequest(string? Kind, long Bytes);

internal sealed record StorageObjectRequest(
    string? Kind,
    long Bytes,
    bool Referenced,
    bool Clean,
    string? StorageKey,
    Guid? ChannelId);

internal sealed record StorageGcRequest(bool DryRun);

internal sealed record StorageRestoreRequest(Guid ObjectId);
