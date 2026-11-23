using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using VibeChat.AI;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Ai;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class BotKnowledgeEndpoints
{
    internal static void MapBotKnowledge(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/ai/knowledge/workspaces/{workspaceId:guid}", ListAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapPost("/admin/ai/knowledge/workspaces/{workspaceId:guid}", CreateAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapPost("/admin/ai/knowledge/workspaces/{workspaceId:guid}/retrieve", RetrieveAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapGet("/admin/ai/knowledge/workspaces/{workspaceId:guid}/{sourceId:guid}", GetAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapPost("/admin/ai/knowledge/workspaces/{workspaceId:guid}/{sourceId:guid}/pause", PauseAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapPost("/admin/ai/knowledge/workspaces/{workspaceId:guid}/{sourceId:guid}/resume", ResumeAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapPost("/admin/ai/knowledge/workspaces/{workspaceId:guid}/{sourceId:guid}/reindex", ReindexAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapPost("/admin/ai/knowledge/workspaces/{workspaceId:guid}/{sourceId:guid}/delete", DeleteAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapPost("/admin/ai/knowledge/workspaces/{workspaceId:guid}/{sourceId:guid}/purge", PurgeAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapPost("/admin/ai/knowledge/workspaces/{workspaceId:guid}/{sourceId:guid}/grants", GrantAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
        v1.MapPost("/admin/ai/knowledge/workspaces/{workspaceId:guid}/{sourceId:guid}/grants/{grantId:guid}/revoke", RevokeAsync)
            .RequirePermission(Permissions.Ai.ManageKnowledge);
    }

    private static Task<IResult> ListAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) => svc.ListAsync(workspace, token), ct);

    private static Task<IResult> GetAsync(Guid workspaceId, Guid sourceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) => svc.GetAsync(workspace, sourceId, token), ct);

    private static async Task<IResult> CreateAsync(Guid workspaceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct)
    {
        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var input = ReadAccept(body);
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.CreateAsync(workspace, profile.Id, input, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> RetrieveAsync(Guid workspaceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct)
    {
        var botId = body.TryGetProperty("botId", out var bot) && bot.TryGetGuid(out var id) ? id : Guid.Empty;
        var query = body.TryGetProperty("query", out var value) ? value.GetString() : null;
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) =>
            svc.RetrieveAsync(workspace, botId, query, token), ct);
    }

    private static Task<IResult> PauseAsync(Guid workspaceId, Guid sourceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct) =>
        Mutate(workspaceId, body, http, db, tenant, clock, options, service, (svc, workspace, profile, hash, token) =>
            svc.PauseAsync(workspace, profile.Id, sourceId, HeaderKey(http), hash, token), ct);

    private static Task<IResult> ResumeAsync(Guid workspaceId, Guid sourceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct) =>
        Mutate(workspaceId, body, http, db, tenant, clock, options, service, (svc, workspace, profile, hash, token) =>
            svc.ResumeAsync(workspace, profile.Id, sourceId, HeaderKey(http), hash, token), ct);

    private static Task<IResult> ReindexAsync(Guid workspaceId, Guid sourceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct) =>
        Mutate(workspaceId, body, http, db, tenant, clock, options, service, (svc, workspace, profile, hash, token) =>
            svc.ReindexAsync(workspace, profile.Id, sourceId, HeaderKey(http), hash, token), ct);

    private static Task<IResult> DeleteAsync(Guid workspaceId, Guid sourceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct) =>
        Mutate(workspaceId, body, http, db, tenant, clock, options, service, (svc, workspace, profile, hash, token) =>
            svc.DeleteAsync(workspace, profile.Id, sourceId, purge: false, HeaderKey(http), hash, token), ct);

    private static Task<IResult> PurgeAsync(Guid workspaceId, Guid sourceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct) =>
        Mutate(workspaceId, body, http, db, tenant, clock, options, service, (svc, workspace, profile, hash, token) =>
            svc.DeleteAsync(workspace, profile.Id, sourceId, purge: true, HeaderKey(http), hash, token), ct);

    private static async Task<IResult> GrantAsync(Guid workspaceId, Guid sourceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct)
    {
        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var botId = body.TryGetProperty("botId", out var bot) && bot.TryGetGuid(out var id) ? id : Guid.Empty;
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.GrantAsync(workspace, profile.Id, sourceId, botId, HeaderKey(http), hash, token), ct);
    }

    private static Task<IResult> RevokeAsync(Guid workspaceId, Guid sourceId, Guid grantId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<BotKnowledgeOptions> options, BotKnowledgeService service, CancellationToken ct) =>
        Mutate(workspaceId, body, http, db, tenant, clock, options, service, (svc, workspace, profile, hash, token) =>
            svc.RevokeAsync(workspace, profile.Id, sourceId, grantId, HeaderKey(http), hash, token), ct);

    private static async Task<IResult> Mutate(
        Guid workspaceId,
        JsonElement body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptions<BotKnowledgeOptions> options,
        BotKnowledgeService service,
        Func<BotKnowledgeService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, string, CancellationToken, Task<BotKnowledgeCommandResult>> command,
        CancellationToken ct)
    {
        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            command(svc, workspace, profile, hash, token), ct);
    }

    private static async Task<IResult> RunAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptions<BotKnowledgeOptions> options,
        BotKnowledgeService service,
        Func<BotKnowledgeService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, CancellationToken, Task<BotKnowledgeCommandResult>> command,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Problem(404, BotKnowledgeErrors.Disabled);
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

    private static BotKnowledgeAccept ReadAccept(JsonElement body)
    {
        var node = JsonNode.Parse(body.GetRawText())?.AsObject() ?? new JsonObject();
        foreach (var name in new[] { "pageId", "fileId" })
        {
            if (node[name] is JsonValue value
                && value.TryGetValue<string>(out var text)
                && string.IsNullOrWhiteSpace(text))
            {
                node.Remove(name);
            }
        }

        return node.Deserialize<BotKnowledgeAccept>(JsonOptions) ?? new BotKnowledgeAccept();
    }

    private static bool TryKey(HttpContext http, JsonElement body, out string hash, out string? error)
    {
        hash = string.Empty;
        error = null;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]) || keys[0]!.Length is < 8 or > 80)
        {
            error = BotKnowledgeErrors.IdempotencyRequired;
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
