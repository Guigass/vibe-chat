using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class DeveloperPortalEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void MapDeveloperPortal(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/developer/catalog", Catalog)
            .RequirePermission(Permissions.Integration.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/developer/openapi", OpenApi)
            .RequirePermission(Permissions.Integration.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/developer/contract-kit", ContractKit)
            .RequirePermission(Permissions.Integration.Read);
        v1.MapGet("/workspaces/{workspaceId:guid}/developer/credentials", ListCredentials)
            .RequirePermission(Permissions.Integration.Read);
        v1.MapPost("/workspaces/{workspaceId:guid}/developer/credentials", CreateCredential)
            .RequirePermission(Permissions.Integration.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/developer/credentials/{credentialId:guid}/rotate", RotateCredential)
            .RequirePermission(Permissions.Integration.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/developer/credentials/{credentialId:guid}/revoke", RevokeCredential)
            .RequirePermission(Permissions.Integration.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/developer/explore", Explore)
            .RequirePermission(Permissions.Integration.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/developer/webhook-inspector", InspectWebhook)
            .RequirePermission(Permissions.Integration.Manage);
        v1.MapGet("/workspaces/{workspaceId:guid}/developer/usage", Usage)
            .RequirePermission(Permissions.Integration.Read);
    }

    private static async Task<IResult> Catalog(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, DeveloperPortalService portal, CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        if (await WorkspaceAsync(workspaceId, http, db, tenant, clock, ct) is null) return Results.Forbid();
        return Payload(http, StatusCodes.Status200OK, portal.Catalog());
    }

    private static async Task<IResult> OpenApi(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, DeveloperPortalService portal, CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        if (await WorkspaceAsync(workspaceId, http, db, tenant, clock, ct) is null) return Results.Forbid();
        return Payload(http, StatusCodes.Status200OK, portal.OpenApi());
    }

    private static async Task<IResult> ContractKit(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, DeveloperPortalService portal, CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        if (await WorkspaceAsync(workspaceId, http, db, tenant, clock, ct) is null) return Results.Forbid();
        return Payload(http, StatusCodes.Status200OK, portal.ContractKit());
    }

    private static async Task<IResult> ListCredentials(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, DeveloperPortalService portal, CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        var workspace = await WorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (workspace is null) return Results.Forbid();
        return Payload(http, StatusCodes.Status200OK, await portal.ListAsync(workspace.Id, ct));
    }

    private static async Task<IResult> Usage(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, DeveloperPortalService portal, CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        var workspace = await WorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (workspace is null) return Results.Forbid();
        return Payload(http, StatusCodes.Status200OK, await portal.UsageAsync(workspace.TenantId, workspace.Id, ct));
    }

    private static async Task<IResult> CreateCredential(
        Guid workspaceId,
        CreateDeveloperCredentialRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IIdempotencyStore idempotency,
        IClock clock,
        DeveloperPortalService portal,
        CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        if (!BotIntegrationPolicies.TryNormalizeName(request.Name, out var name))
        {
            return Error(http, StatusCodes.Status400BadRequest, DeveloperPortalErrors.InvalidName);
        }

        if (!TryIdempotencyKey(http, $"dev:c:{workspaceId:N}:", out var key))
        {
            return Error(http, StatusCodes.Status400BadRequest, DeveloperPortalErrors.IdempotencyKeyRequired);
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null) return Results.Forbid();

        var channels = await IntegrationBotEndpoints.LoadGrantChannelsAsync(db, workspace.Id, request.ChannelIds, ct);
        if (channels is null)
        {
            return Error(http, StatusCodes.Status400BadRequest, DeveloperPortalErrors.InvalidChannelScope);
        }

        var channelIds = channels.Select(x => x.Id.Value).Order().ToArray();
        var hash = Hash("create", name, request.AllowDms, channelIds);
        var replay = await ReplayAsync(http, idempotency, workspace.TenantId, key, hash, ct);
        if (replay is not null) return replay;

        var now = clock.UtcNow;
        var (bot, raw, last4) = IntegrationBotEndpoints.ProvisionBot(db, workspace.TenantId, workspace.Id, name, request.AllowDms, now);
        await IntegrationBotEndpoints.ReplaceScopesAsync(db, bot, channels, now, ct);
        var view = new DeveloperCredentialView(bot.Id, bot.Name, bot.Enabled, bot.AllowDms, channelIds, true, last4, null, false, bot.CreatedAt);
        audit.Add(Audit(workspace.TenantId, profile.Id, AuditActions.DeveloperCredentialCreate, bot.Id, now, workspace.Id.Value, 201, false, null));
        await idempotency.StoreAsync(new IdempotencyRecord(workspace.TenantId, key, hash, Redacted(view), now), ct);
        await db.SaveChangesAsync(ct);
        return Payload(http, StatusCodes.Status201Created, SecretBody(view, raw, idempotent: false));
    }

    private static async Task<IResult> RotateCredential(
        Guid workspaceId,
        Guid credentialId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IIdempotencyStore idempotency,
        IClock clock,
        DeveloperPortalService portal,
        CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        if (!TryIdempotencyKey(http, $"dev:r:{credentialId:N}:", out var key))
        {
            return Error(http, StatusCodes.Status400BadRequest, DeveloperPortalErrors.IdempotencyKeyRequired);
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null) return Results.Forbid();

        var hash = Hash("rotate", credentialId.ToString("N"), false, []);
        var replay = await ReplayAsync(http, idempotency, workspace.TenantId, key, hash, ct);
        if (replay is not null) return replay;

        var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == credentialId && x.WorkspaceId == workspace.Id, ct);
        if (bot is null) return Error(http, StatusCodes.Status404NotFound, DeveloperPortalErrors.NotFound);

        var now = clock.UtcNow;
        await IntegrationBotEndpoints.RevokeActiveTokensAsync(db, bot.Id, now, ct);
        var (raw, token) = IntegrationBotEndpoints.IssueToken(bot, now);
        db.IntegrationBotTokens.Add(token);
        var view = await portal.ViewAsync(bot, ct);
        view = view with { TokenConfigured = true, Last4 = token.Last4, Revoked = false, LastUsedAt = null };
        audit.Add(Audit(workspace.TenantId, profile.Id, AuditActions.DeveloperCredentialRotate, bot.Id, now, workspace.Id.Value, 200, false, null));
        await idempotency.StoreAsync(new IdempotencyRecord(workspace.TenantId, key, hash, Redacted(view), now), ct);
        await db.SaveChangesAsync(ct);
        return Payload(http, StatusCodes.Status200OK, SecretBody(view, raw, idempotent: false));
    }

    private static async Task<IResult> RevokeCredential(
        Guid workspaceId,
        Guid credentialId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        DeveloperPortalService portal,
        CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null) return Results.Forbid();

        var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == credentialId && x.WorkspaceId == workspace.Id, ct);
        if (bot is null) return Error(http, StatusCodes.Status404NotFound, DeveloperPortalErrors.NotFound);

        var now = clock.UtcNow;
        await IntegrationBotEndpoints.RevokeActiveTokensAsync(db, bot.Id, now, ct);
        audit.Add(Audit(workspace.TenantId, profile.Id, AuditActions.DeveloperCredentialRevoke, bot.Id, now, workspace.Id.Value, 200, false, null));
        await db.SaveChangesAsync(ct);
        return Payload(http, StatusCodes.Status200OK, await portal.ViewAsync(bot, ct));
    }

    private static async Task<IResult> Explore(
        Guid workspaceId,
        DeveloperExploreRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        DeveloperPortalService portal,
        CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null) return Results.Forbid();

        var bot = await db.IntegrationBots.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.CredentialId && x.WorkspaceId == workspace.Id, ct);
        if (bot is null) return Error(http, StatusCodes.Status404NotFound, DeveloperPortalErrors.NotFound);

        var active = await db.IntegrationBotTokens.AsNoTracking()
            .AnyAsync(x => x.BotId == bot.Id && x.RevokedAt == null, ct);
        var channels = await db.IntegrationBotChannelScopes.AsNoTracking()
            .Where(x => x.BotId == bot.Id)
            .Select(x => x.ChannelId.Value)
            .ToArrayAsync(ct);
        var decision = DeveloperPortalCatalog.Decide(bot.Enabled, !active, bot.AllowDms, channels, request.Operation, request.ChannelId);
        var now = clock.UtcNow;
        audit.Add(Audit(
            workspace.TenantId,
            profile.Id,
            AuditActions.DeveloperExplore,
            bot.Id,
            now,
            workspace.Id.Value,
            decision.StatusCode,
            true,
            decision.Operation ?? request.Operation));
        await db.SaveChangesAsync(ct);
        if (!decision.Allowed)
        {
            return Error(http, decision.StatusCode, decision.Error!);
        }

        return Payload(http, StatusCodes.Status200OK, DeveloperPortalCatalog.SyntheticCall(bot.Id, decision.Operation!));
    }

    private static async Task<IResult> InspectWebhook(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        DeveloperPortalService portal,
        CancellationToken ct)
    {
        if (!portal.Enabled) return Disabled(http);
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null) return Results.Forbid();

        var now = clock.UtcNow;
        var payload = DeveloperPortalCatalog.SyntheticWebhook();
        audit.Add(Audit(workspace.TenantId, profile.Id, AuditActions.DeveloperWebhookInspect, Guid.Empty, now, workspace.Id.Value, 200, true, "webhook.inspect"));
        await db.SaveChangesAsync(ct);
        return Payload(http, StatusCodes.Status200OK, payload);
    }

    private static async Task<Workspace?> WorkspaceAsync(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        return await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    }

    private static bool TryIdempotencyKey(HttpContext http, string prefix, out string key)
    {
        key = string.Empty;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var raw))
        {
            return false;
        }

        var trimmed = raw.ToString().Trim();
        if (trimmed.Length is < 1 or > 120)
        {
            return false;
        }

        key = prefix + trimmed;
        return key.Length <= 200;
    }

    private static async Task<IResult?> ReplayAsync(
        HttpContext http, IIdempotencyStore idempotency, TenantId tenantId, string key, string hash, CancellationToken ct)
    {
        var existing = await idempotency.FindAsync(tenantId, key, ct);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            return Error(http, StatusCodes.Status409Conflict, DeveloperPortalErrors.IdempotencyConflict);
        }

        if (DeveloperPortalCatalog.ContainsSecret(existing.ResultJson))
        {
            return Error(http, StatusCodes.Status409Conflict, DeveloperPortalErrors.IdempotencyConflict);
        }

        http.Response.Headers.CacheControl = "no-store";
        return Results.Content(existing.ResultJson, "application/json", statusCode: StatusCodes.Status200OK);
    }

    private static string Hash(string operation, string name, bool allowDms, Guid[] channelIds)
    {
        var raw = $"{operation}|{name}|{allowDms}|{string.Join(',', channelIds)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string Redacted(DeveloperCredentialView view) =>
        JsonSerializer.Serialize(SecretBody(view, token: null, idempotent: true), Json);

    private static object SecretBody(DeveloperCredentialView view, string? token, bool idempotent) => new
    {
        view.Id,
        view.Name,
        view.Enabled,
        view.AllowDms,
        view.ChannelIds,
        view.TokenConfigured,
        view.Last4,
        view.LastUsedAt,
        view.Revoked,
        view.CreatedAt,
        Token = token,
        Idempotent = idempotent
    };

    private static AuditEvent Audit(
        TenantId tenantId,
        UserId actorId,
        string action,
        Guid credentialId,
        DateTimeOffset now,
        Guid workspaceId,
        int status,
        bool synthetic,
        string? operation) => new()
        {
            TenantId = tenantId,
            ActorUserId = actorId,
            Action = action,
            EntityType = "IntegrationCredential",
            EntityId = credentialId == Guid.Empty ? null : credentialId.ToString("D"),
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId = workspaceId.ToString("D"),
                credentialId = credentialId == Guid.Empty ? null : credentialId.ToString("D"),
                status,
                synthetic,
                operation
            }),
            OccurredAt = now
        };

    private static IResult Disabled(HttpContext http) =>
        Error(http, StatusCodes.Status404NotFound, DeveloperPortalErrors.Disabled);

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

    private sealed record CreateDeveloperCredentialRequest(string? Name, Guid[]? ChannelIds, bool AllowDms);

    private sealed record DeveloperExploreRequest(Guid CredentialId, string? Operation, Guid? ChannelId);
}
