using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeChat.AI;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Ai;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class AiBotEndpoints
{
    internal static void MapAiBots(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/ai/bots/workspaces/{workspaceId:guid}", ListAsync)
            .RequirePermission(Permissions.Ai.ManageBots);
        v1.MapPost("/admin/ai/bots/workspaces/{workspaceId:guid}", CreateAsync)
            .RequirePermission(Permissions.Ai.ManageBots);
        v1.MapGet("/admin/ai/bots/workspaces/{workspaceId:guid}/{botId:guid}", GetAsync)
            .RequirePermission(Permissions.Ai.ManageBots);
        v1.MapPut("/admin/ai/bots/workspaces/{workspaceId:guid}/{botId:guid}", UpdateAsync)
            .RequirePermission(Permissions.Ai.ManageBots);
        v1.MapGet("/admin/ai/bots/workspaces/{workspaceId:guid}/{botId:guid}/diff", DiffAsync)
            .RequirePermission(Permissions.Ai.ManageBots);
        v1.MapPost("/admin/ai/bots/workspaces/{workspaceId:guid}/{botId:guid}/publish", PublishAsync)
            .RequirePermission(Permissions.Ai.ManageBots);
        v1.MapPost("/admin/ai/bots/workspaces/{workspaceId:guid}/{botId:guid}/disable", DisableAsync)
            .RequirePermission(Permissions.Ai.ManageBots);
        v1.MapPost("/admin/ai/bots/workspaces/{workspaceId:guid}/{botId:guid}/rollback", RollbackAsync)
            .RequirePermission(Permissions.Ai.ManageBots);
        v1.MapGet("/workspaces/{workspaceId:guid}/ai/bots", ListInvocableAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/ai/bots/{botId:guid}/invocations", InvokeAsync)
            .RequirePermission(Permissions.Message.Read);
    }

    private static Task<IResult> ListAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) => svc.ListAsync(workspace, token), ct);

    private static Task<IResult> GetAsync(Guid workspaceId, Guid botId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) => svc.GetAsync(workspace, botId, token), ct);

    private static Task<IResult> DiffAsync(Guid workspaceId, Guid botId, int fromVersion, int toVersion, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) => svc.DiffAsync(workspace, botId, fromVersion, toVersion, token), ct);

    private static async Task<IResult> CreateAsync(Guid workspaceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct)
    {
        if (!TryMutation(http, body, publishing: false, out var draft, out var hash, out var error))
        {
            return Problem(error == AiBotErrors.IdempotencyRequired ? 400 : 400, error!);
        }

        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.CreateAsync(workspace, profile.Id, draft!, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> UpdateAsync(Guid workspaceId, Guid botId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct)
    {
        if (!TryMutation(http, body, publishing: false, out var draft, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var revision = body.TryGetProperty("expectedRevision", out var value) && value.TryGetInt32(out var number) ? number : 0;
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.UpdateAsync(workspace, profile.Id, botId, revision, draft!, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> PublishAsync(Guid workspaceId, Guid botId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct)
    {
        if (!TryMutation(http, body, publishing: true, out var draft, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var revision = body.TryGetProperty("expectedRevision", out var value) && value.TryGetInt32(out var number) ? number : 0;
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.PublishAsync(workspace, profile.Id, botId, revision, draft!, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> DisableAsync(Guid workspaceId, Guid botId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct)
    {
        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var revision = body.TryGetProperty("expectedRevision", out var value) && value.TryGetInt32(out var number) ? number : 0;
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.DisableAsync(workspace, profile.Id, botId, revision, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> RollbackAsync(Guid workspaceId, Guid botId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct)
    {
        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var revision = body.TryGetProperty("expectedRevision", out var value) && value.TryGetInt32(out var number) ? number : 0;
        var version = body.TryGetProperty("versionNumber", out var target) && target.TryGetInt32(out var versionNumber) ? versionNumber : 0;
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.RollbackAsync(workspace, profile.Id, botId, revision, version, HeaderKey(http), hash, token), ct);
    }

    private static Task<IResult> ListInvocableAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) => svc.ListInvocableAsync(workspace, profile.Id, token), ct);

    private static async Task<IResult> InvokeAsync(Guid workspaceId, Guid botId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiBotCatalogService service, CancellationToken ct)
    {
        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.InvokeAsync(workspace, profile.Id, botId, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> RunAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptions<AiBotOptions> options,
        AiBotCatalogService service,
        Func<AiBotCatalogService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, CancellationToken, Task<AiBotCommandResult>> command,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Problem(404, AiBotErrors.Disabled);
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var result = await command(service, workspace, profile, ct);
        return result.Error is null
            ? Results.Json(result.Body, statusCode: result.Status)
            : Problem(result.Status, result.Error);
    }

    private static bool TryMutation(HttpContext http, JsonElement body, bool publishing, out AiBotDraft? draft, out string hash, out string? error)
    {
        draft = null;
        hash = string.Empty;
        error = AiBotCatalogPolicies.RejectSecretNames(body);
        if (error is not null || !TryKey(http, body, out hash, out error))
        {
            return false;
        }

        var input = body.Deserialize<AiBotDraftInput>(JsonOptions) ?? new AiBotDraftInput();
        if (!AiBotCatalogPolicies.TryNormalize(input, publishing, out var normalized, out error))
        {
            return false;
        }

        draft = normalized;
        return true;
    }

    private static bool TryKey(HttpContext http, JsonElement body, out string hash, out string? error)
    {
        hash = string.Empty;
        error = null;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]) || keys[0]!.Length is < 8 or > 80)
        {
            error = AiBotErrors.IdempotencyRequired;
            return false;
        }

        hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.GetRawText()))).ToLowerInvariant();
        return true;
    }

    private static string HeaderKey(HttpContext http) => http.Request.Headers["Idempotency-Key"].ToString();

    private static IResult Problem(int status, string error) =>
        Results.Json(new { error }, statusCode: status);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
