using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class IntegrationPluginEndpoints
{
    internal static void MapIntegrationPlugins(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/plugins", ListPlugins)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/plugins", InstallPlugin)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPatch("/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}", UpdatePlugin)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapDelete("/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}", UninstallPlugin)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}/rotate", RotatePlugin)
            .RequirePermission(Permissions.Workspace.Admin);
    }

    private static async Task<IResult> ListPlugins(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return IntegrationBotEndpoints.Disabled();
        }

        var workspace = await ResolveWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var plugins = await db.InstalledPlugins.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.InstalledAt)
            .ToListAsync(ct);
        var responses = new List<InstalledPluginResponse>(plugins.Count);
        foreach (var plugin in plugins)
        {
            responses.Add(await ToResponseAsync(db, plugin, ct));
        }

        return Results.Ok(responses);
    }

    private static async Task<IResult> InstallPlugin(
        Guid workspaceId,
        InstallPluginRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return IntegrationBotEndpoints.Disabled();
        }

        if (!TryResolveManifest(request, out var manifest, out var error))
        {
            return Results.BadRequest(new { error });
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var channels = await IntegrationBotEndpoints.LoadGrantChannelsAsync(db, workspace.Id, request.ChannelIds, ct);
        if (channels is null)
        {
            return Results.BadRequest(new { error = "InvalidChannelScope" });
        }

        var taken = await db.InstalledPlugins.AnyAsync(
            x => x.WorkspaceId == workspace.Id && x.PluginId == manifest!.Id,
            ct);
        if (taken)
        {
            return Results.Conflict(new { error = PluginManifestRules.AlreadyInstalled });
        }

        var count = await db.InstalledPlugins.CountAsync(x => x.WorkspaceId == workspace.Id, ct);
        if (count >= PluginManifestRules.MaxPluginsPerWorkspace)
        {
            return Results.Conflict(new { error = PluginManifestRules.LimitReached });
        }

        var now = clock.UtcNow;
        var (bot, raw, last4) = IntegrationBotEndpoints.ProvisionBot(
            db, workspace.TenantId, workspace.Id, manifest!.Name, request.AllowDms, now);
        await IntegrationBotEndpoints.ReplaceScopesAsync(db, bot, channels, now, ct);
        var plugin = new InstalledPlugin
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            PluginId = manifest.Id,
            Name = manifest.Name,
            Version = manifest.Version,
            ManifestJson = manifest.CanonicalJson,
            Capabilities = manifest.Capabilities,
            BotId = bot.Id,
            Enabled = true,
            InstalledAt = now,
            UpdatedAt = now
        };
        db.InstalledPlugins.Add(plugin);
        audit.Add(PluginAudit(workspace.TenantId, profile.Id, AuditActions.IntegrationPluginInstall, plugin, now));
        await db.SaveChangesAsync(ct);
        return Results.Created(
            $"/api/v1/admin/workspaces/{workspace.Id.Value}/plugins/{plugin.Id}",
            ToSecret(plugin, bot.AllowDms, channels.Select(x => x.Id.Value).ToArray(), raw, last4));
    }

    private static async Task<IResult> UpdatePlugin(
        Guid workspaceId,
        Guid installedId,
        UpdateInstalledPluginRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return IntegrationBotEndpoints.Disabled();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var plugin = await db.InstalledPlugins.FirstOrDefaultAsync(
            x => x.Id == installedId && x.WorkspaceId == workspace.Id,
            ct);
        if (plugin is null)
        {
            return Results.NotFound(new { error = PluginManifestRules.NotFound });
        }

        var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == plugin.BotId && x.WorkspaceId == workspace.Id, ct);
        if (bot is null)
        {
            return Results.NotFound(new { error = PluginManifestRules.NotFound });
        }

        var now = clock.UtcNow;
        plugin.Enabled = request.Enabled;
        plugin.UpdatedAt = now;
        bot.Enabled = request.Enabled;
        audit.Add(PluginAudit(
            workspace.TenantId,
            profile.Id,
            request.Enabled ? AuditActions.IntegrationPluginEnable : AuditActions.IntegrationPluginDisable,
            plugin,
            now));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToResponseAsync(db, plugin, ct));
    }

    private static async Task<IResult> UninstallPlugin(
        Guid workspaceId,
        Guid installedId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return IntegrationBotEndpoints.Disabled();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var plugin = await db.InstalledPlugins.FirstOrDefaultAsync(
            x => x.Id == installedId && x.WorkspaceId == workspace.Id,
            ct);
        if (plugin is null)
        {
            return Results.NotFound(new { error = PluginManifestRules.NotFound });
        }

        var now = clock.UtcNow;
        await IntegrationBotEndpoints.RevokeActiveTokensAsync(db, plugin.BotId, now, ct);
        var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == plugin.BotId, ct);
        if (bot is not null)
        {
            bot.Enabled = false;
        }

        audit.Add(PluginAudit(workspace.TenantId, profile.Id, AuditActions.IntegrationPluginUninstall, plugin, now));
        db.InstalledPlugins.Remove(plugin);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RotatePlugin(
        Guid workspaceId,
        Guid installedId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<BotIntegrationOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return IntegrationBotEndpoints.Disabled();
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var plugin = await db.InstalledPlugins.FirstOrDefaultAsync(
            x => x.Id == installedId && x.WorkspaceId == workspace.Id,
            ct);
        if (plugin is null)
        {
            return Results.NotFound(new { error = PluginManifestRules.NotFound });
        }

        var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == plugin.BotId && x.WorkspaceId == workspace.Id, ct);
        if (bot is null)
        {
            return Results.NotFound(new { error = PluginManifestRules.NotFound });
        }

        var now = clock.UtcNow;
        await IntegrationBotEndpoints.RevokeActiveTokensAsync(db, bot.Id, now, ct);
        var (raw, token) = IntegrationBotEndpoints.IssueToken(bot, now);
        db.IntegrationBotTokens.Add(token);
        plugin.UpdatedAt = now;
        audit.Add(PluginAudit(workspace.TenantId, profile.Id, AuditActions.IntegrationPluginRotate, plugin, now));
        await db.SaveChangesAsync(ct);
        var channelIds = await db.IntegrationBotChannelScopes.AsNoTracking()
            .Where(x => x.BotId == bot.Id)
            .Select(x => x.ChannelId.Value)
            .ToArrayAsync(ct);
        return Results.Ok(ToSecret(plugin, bot.AllowDms, channelIds, raw, token.Last4));
    }

    private static bool TryResolveManifest(InstallPluginRequest request, out PluginManifestDocument? manifest, out string error)
    {
        var hasBuiltin = !string.IsNullOrWhiteSpace(request.BuiltinId);
        var hasManifest = request.Manifest is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null };
        if (hasBuiltin == hasManifest)
        {
            manifest = null;
            error = PluginManifestRules.Invalid;
            return false;
        }

        if (hasBuiltin)
        {
            return PluginManifestRules.TryGetBuiltin(request.BuiltinId, out manifest, out error);
        }

        return PluginManifestRules.TryParse(request.Manifest!.Value.GetRawText(), out manifest, out error);
    }

    private static async Task<Workspace?> ResolveWorkspaceAsync(
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

    private static AuditEvent PluginAudit(TenantId tenantId, UserId actorId, string action, InstalledPlugin plugin, DateTimeOffset now) =>
        new()
        {
            TenantId = tenantId,
            ActorUserId = actorId,
            Action = action,
            EntityType = "InstalledPlugin",
            EntityId = plugin.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { pluginId = plugin.PluginId, botId = plugin.BotId }),
            OccurredAt = now
        };

    private static async Task<InstalledPluginResponse> ToResponseAsync(VibeChatDbContext db, InstalledPlugin plugin, CancellationToken ct)
    {
        var bot = await db.IntegrationBots.AsNoTracking().FirstAsync(x => x.Id == plugin.BotId, ct);
        var channelIds = await db.IntegrationBotChannelScopes.AsNoTracking()
            .Where(x => x.BotId == plugin.BotId)
            .Select(x => x.ChannelId.Value)
            .ToArrayAsync(ct);
        var token = await db.IntegrationBotTokens.AsNoTracking()
            .Where(x => x.BotId == plugin.BotId && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return new InstalledPluginResponse(
            plugin.Id,
            plugin.PluginId,
            plugin.Name,
            plugin.Version,
            plugin.Capabilities,
            plugin.Enabled,
            plugin.BotId,
            bot.AllowDms,
            channelIds,
            token is not null,
            token?.Last4,
            plugin.InstalledAt,
            plugin.UpdatedAt);
    }

    private static InstalledPluginSecretResponse ToSecret(
        InstalledPlugin plugin,
        bool allowDms,
        Guid[] channelIds,
        string raw,
        string last4) =>
        new(
            plugin.Id,
            plugin.PluginId,
            plugin.Name,
            plugin.Version,
            plugin.Capabilities,
            plugin.Enabled,
            plugin.BotId,
            allowDms,
            channelIds,
            raw,
            last4,
            plugin.InstalledAt,
            plugin.UpdatedAt);
}
