using System.Text.Json;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class PluginSdkEndpoints
{
    internal static void MapPluginSdk(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/developer/sdk", Catalog)
            .RequirePermission(Permissions.Integration.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/developer/sdk/verify", Verify)
            .RequirePermission(Permissions.Integration.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/developer/sdk/contract-tests", ContractTests)
            .RequirePermission(Permissions.Integration.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/developer/sdk/sandbox", Sandbox)
            .RequirePermission(Permissions.Integration.Read);
    }

    private static async Task<IResult> Catalog(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        PluginSdkService sdk,
        CancellationToken ct)
    {
        if (!sdk.Enabled) return Disabled(http);
        if (await WorkspaceAsync(workspaceId, http, db, tenant, clock, ct) is null) return Results.Forbid();
        return Payload(http, StatusCodes.Status200OK, sdk.Catalog(clock.UtcNow));
    }

    private static async Task<IResult> Verify(
        Guid workspaceId,
        SdkVerifyBody request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        PluginSdkService sdk,
        CancellationToken ct)
    {
        if (!sdk.Enabled) return Disabled(http);
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null) return Results.Forbid();

        var now = clock.UtcNow;
        var body = sdk.Verify(request.Body, request.Signature, request.DeliveryId, request.DeliveredAt, request.SeenDeliveryIds, now);
        audit.Add(Audit(workspace.TenantId, profile.Id, AuditActions.DeveloperSdkVerify, now, workspace.Id.Value, 200));
        await db.SaveChangesAsync(ct);
        return Payload(http, StatusCodes.Status200OK, body);
    }

    private static async Task<IResult> ContractTests(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        PluginSdkService sdk,
        CancellationToken ct)
    {
        if (!sdk.Enabled) return Disabled(http);
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null) return Results.Forbid();

        var now = clock.UtcNow;
        var report = sdk.ContractReport(now);
        audit.Add(Audit(workspace.TenantId, profile.Id, AuditActions.DeveloperSdkContract, now, workspace.Id.Value, 200));
        await db.SaveChangesAsync(ct);
        return Payload(http, StatusCodes.Status200OK, report);
    }

    private static async Task<IResult> Sandbox(
        Guid workspaceId,
        SdkSandboxBody request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        PluginSdkService sdk,
        CancellationToken ct)
    {
        if (!sdk.Enabled) return Disabled(http);
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null) return Results.Forbid();

        var decision = sdk.Sandbox(request.Operation, request.IdempotencyKey ?? HeaderKey(http));
        var now = clock.UtcNow;
        audit.Add(Audit(workspace.TenantId, profile.Id, AuditActions.DeveloperSdkSandbox, now, workspace.Id.Value, decision.Status));
        await db.SaveChangesAsync(ct);
        if (!decision.Ok)
        {
            return Error(http, decision.Status, decision.Error!);
        }

        return Payload(http, StatusCodes.Status200OK, decision.Body!);
    }

    private static async Task<Workspace?> WorkspaceAsync(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        return await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    }

    private static string? HeaderKey(HttpContext http) =>
        http.Request.Headers.TryGetValue("Idempotency-Key", out var value) ? value.ToString() : null;

    private static AuditEvent Audit(TenantId tenantId, UserId actorId, string action, DateTimeOffset now, Guid workspaceId, int status) => new()
    {
        TenantId = tenantId,
        ActorUserId = actorId,
        Action = action,
        EntityType = "PluginSdk",
        MetadataJson = JsonSerializer.Serialize(new
        {
            workspaceId = workspaceId.ToString("D"),
            status,
            synthetic = true
        }),
        OccurredAt = now
    };

    private static IResult Disabled(HttpContext http) =>
        Error(http, StatusCodes.Status404NotFound, PluginSdkErrors.Disabled);

    private static IResult Error(HttpContext http, int status, string error)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { error }, statusCode: status);
    }

    private static IResult Payload(HttpContext http, int status, object body)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(body, statusCode: status);
    }

    private sealed record SdkVerifyBody(
        string? Body,
        string? Signature,
        string? DeliveryId,
        DateTimeOffset? DeliveredAt,
        string[]? SeenDeliveryIds);

    private sealed record SdkSandboxBody(string? Operation, Guid? ChannelId, string? Body, string? IdempotencyKey, Guid? TenantId);
}
