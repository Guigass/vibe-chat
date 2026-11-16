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

internal static class AiSkillEndpoints
{
    internal static void MapAiSkills(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/ai/skills/workspaces/{workspaceId:guid}", ListAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapPost("/admin/ai/skills/workspaces/{workspaceId:guid}", CreateAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapPost("/admin/ai/skills/workspaces/{workspaceId:guid}/import", ImportAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapPost("/admin/ai/skills/workspaces/{workspaceId:guid}/compose", ComposeAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapGet("/admin/ai/skills/workspaces/{workspaceId:guid}/{skillId:guid}", GetAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapPut("/admin/ai/skills/workspaces/{workspaceId:guid}/{skillId:guid}", UpdateAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapGet("/admin/ai/skills/workspaces/{workspaceId:guid}/{skillId:guid}/diff", DiffAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapGet("/admin/ai/skills/workspaces/{workspaceId:guid}/{skillId:guid}/export", ExportAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapPost("/admin/ai/skills/workspaces/{workspaceId:guid}/{skillId:guid}/publish", PublishAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapPost("/admin/ai/skills/workspaces/{workspaceId:guid}/{skillId:guid}/deprecate", DeprecateAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapPost("/admin/ai/skills/workspaces/{workspaceId:guid}/{skillId:guid}/clone", CloneAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
        v1.MapPost("/admin/ai/skills/workspaces/{workspaceId:guid}/{skillId:guid}/rollback", RollbackAsync)
            .RequirePermission(Permissions.Ai.ManageSkills);
    }

    private static Task<IResult> ListAsync(Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) => svc.ListAsync(workspace, token), ct);

    private static Task<IResult> GetAsync(Guid workspaceId, Guid skillId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) => svc.GetAsync(workspace, skillId, token), ct);

    private static Task<IResult> DiffAsync(Guid workspaceId, Guid skillId, int fromVersion, int toVersion, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) => svc.DiffAsync(workspace, skillId, fromVersion, toVersion, token), ct);

    private static Task<IResult> ExportAsync(Guid workspaceId, Guid skillId, int version, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct) =>
        RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) => svc.ExportAsync(workspace, skillId, version, token), ct);

    private static async Task<IResult> ComposeAsync(Guid workspaceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct)
    {
        var ids = body.TryGetProperty("skillVersionIds", out var value)
            ? value.Deserialize<Guid[]>(JsonOptions) ?? []
            : [];
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, _, token) =>
            svc.ComposeAsync(workspace, ids, token), ct);
    }

    private static async Task<IResult> CreateAsync(Guid workspaceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct)
    {
        if (!TryMutation(http, body, publishing: false, out var draft, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.CreateAsync(workspace, profile.Id, draft!, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> UpdateAsync(Guid workspaceId, Guid skillId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct)
    {
        if (!TryMutation(http, body, publishing: false, out var draft, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var revision = Revision(body);
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.UpdateAsync(workspace, profile.Id, skillId, revision, draft!, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> PublishAsync(Guid workspaceId, Guid skillId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct)
    {
        if (!TryMutation(http, body, publishing: true, out var draft, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var revision = Revision(body);
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.PublishAsync(workspace, profile.Id, skillId, revision, draft!, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> DeprecateAsync(Guid workspaceId, Guid skillId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct)
    {
        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var revision = Revision(body);
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.DeprecateAsync(workspace, profile.Id, skillId, revision, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> CloneAsync(Guid workspaceId, Guid skillId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct)
    {
        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var version = body.TryGetProperty("versionNumber", out var target) && target.TryGetInt32(out var number) ? number : 0;
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.CloneAsync(workspace, profile.Id, skillId, version, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> RollbackAsync(Guid workspaceId, Guid skillId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct)
    {
        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        var revision = Revision(body);
        var version = body.TryGetProperty("versionNumber", out var target) && target.TryGetInt32(out var number) ? number : 0;
        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.RollbackAsync(workspace, profile.Id, skillId, revision, version, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> ImportAsync(Guid workspaceId, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IOptions<AiBotOptions> options, AiSkillCatalogService service, CancellationToken ct)
    {
        var rejected = AiSkillCatalogPolicies.RejectSecretOrCode(body);
        if (rejected is not null)
        {
            return Problem(400, rejected);
        }

        if (!TryKey(http, body, out var hash, out var error))
        {
            return Problem(400, error!);
        }

        if (!AiSkillCatalogPolicies.TryImport(body, out var imported, out error))
        {
            return Problem(400, error!);
        }

        return await RunAsync(workspaceId, http, db, tenant, clock, options, service, (svc, workspace, profile, token) =>
            svc.ImportAsync(workspace, profile.Id, imported, HeaderKey(http), hash, token), ct);
    }

    private static async Task<IResult> RunAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptions<AiBotOptions> options,
        AiSkillCatalogService service,
        Func<AiSkillCatalogService, VibeChat.Tenancy.Workspace, VibeChat.Identity.UserProfile, CancellationToken, Task<AiSkillCommandResult>> command,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Problem(404, AiSkillErrors.Disabled);
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

    private static bool TryMutation(HttpContext http, JsonElement body, bool publishing, out AiSkillDraft? draft, out string hash, out string? error)
    {
        draft = null;
        hash = string.Empty;
        error = AiSkillCatalogPolicies.RejectSecretOrCode(body);
        if (error is not null || !TryKey(http, body, out hash, out error))
        {
            return false;
        }

        var input = body.Deserialize<AiSkillDraftInput>(JsonOptions) ?? new AiSkillDraftInput();
        if (!AiSkillCatalogPolicies.TryNormalize(input, publishing, out var normalized, out error))
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
            error = AiSkillErrors.IdempotencyRequired;
            return false;
        }

        hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.GetRawText()))).ToLowerInvariant();
        return true;
    }

    private static int Revision(JsonElement body) =>
        body.TryGetProperty("expectedRevision", out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string HeaderKey(HttpContext http) => http.Request.Headers["Idempotency-Key"].ToString();

    private static IResult Problem(int status, string error) =>
        Results.Json(new { error }, statusCode: status);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
