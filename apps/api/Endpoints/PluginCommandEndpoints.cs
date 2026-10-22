using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.ConversationsEndpointHelpers;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class PluginCommandEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void MapPluginCommands(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/plugin-commands", Invoke)
            .RequirePermission(Permissions.Message.Send);
    }

    private static async Task<IResult> Invoke(
        Guid workspaceId,
        Guid channelId,
        InvokePluginCommandRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IMessageWriter messages,
        IPluginCallbackDispatcher callbacks,
        IIdempotencyStore idempotency,
        IAuditWriter audit,
        IRateLimiter rateLimiter,
        RateLimitSettingsResolver rateLimits,
        IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform,
        IClock clock,
        CancellationToken ct)
    {
        if (!bots.Value.Enabled)
        {
            return IntegrationBotEndpoints.Disabled();
        }

        if (!platform.Value.Enabled)
        {
            return Results.NotFound(new { error = PluginPlatformErrors.Disabled });
        }

        if (!PluginManifestRules.TrySplitCommand(request.Name, out var pluginSlug, out var commandName))
        {
            return Results.NotFound(new { error = PluginPlatformErrors.CommandNotFound });
        }

        var key = (request.IdempotencyKey ?? string.Empty).Trim();
        if (key.Length is < 1 or > 80)
        {
            return Results.BadRequest(new { error = "IdempotencyKeyRequired" });
        }

        var args = (request.Args ?? string.Empty).Trim();
        if (args.Length is < 1 or > 4000)
        {
            return Results.BadRequest(new { error = PluginPlatformErrors.ArgsRequired });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.WorkspaceId.Value != workspaceId)
        {
            return Results.Forbid();
        }

        var plugin = await db.InstalledPlugins.FirstOrDefaultAsync(
            item => item.WorkspaceId == channel.WorkspaceId && item.PluginId == pluginSlug,
            ct);
        if (plugin is null || !plugin.Enabled)
        {
            return Results.NotFound(new { error = PluginPlatformErrors.CommandNotFound });
        }

        if (!plugin.Capabilities.Contains(PluginManifestRules.CapabilityCommandsSlash, StringComparer.Ordinal)
            || !PluginManifestRules.TryParse(plugin.ManifestJson, out var manifest, out _)
            || manifest is null
            || manifest.Commands.All(item => item.Name != commandName))
        {
            return Results.Json(new { error = PluginPlatformErrors.CapabilityDenied }, statusCode: StatusCodes.Status403Forbidden);
        }

        var inScope = await db.IntegrationBotChannelScopes.AnyAsync(
            scope => scope.BotId == plugin.BotId && scope.ChannelId == channel.Id,
            ct);
        if (!inScope)
        {
            return Results.Json(new { error = PluginPlatformErrors.OutOfScope }, statusCode: StatusCodes.Status403Forbidden);
        }

        var storeKey = $"plugin-cmd:{plugin.Id:N}:{key}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{commandName}|{args}"))).ToLowerInvariant();
        var existing = await idempotency.FindAsync(channel.TenantId, storeKey, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            {
                return Results.Conflict(new { error = "IdempotencyConflict" });
            }

            var replay = JsonSerializer.Deserialize<PluginCommandResponse>(existing.ResultJson, Json);
            return replay is null
                ? Results.Conflict(new { error = "IdempotencyConflict" })
                : Results.Json(replay, statusCode: StatusCodes.Status202Accepted);
        }

        var rate = await rateLimits.ResolveAsync(channel.TenantId, ct);
        var allowed = await rateLimiter.TryAcquireAsync(
            RateLimitKeys.SendMessage(channel.TenantId, profile.Id),
            rate.SendPerMinute,
            TimeSpan.FromMinutes(1),
            ct);
        if (!allowed)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        Guid? messageId = null;
        long? sequence = null;
        var posted = false;
        if (PluginManifestRules.IsProductSlash(manifest, commandName))
        {
            var bot = await db.IntegrationBots.FirstOrDefaultAsync(item => item.Id == plugin.BotId && item.Enabled, ct);
            if (bot is null)
            {
                return Results.NotFound(new { error = PluginPlatformErrors.CommandNotFound });
            }

            var sent = await messages.SendAsync(
                new SendMessageCommand(
                    channel.TenantId,
                    bot.UserId,
                    channel.Id,
                    MessageId.New(),
                    $"plugin:{plugin.Id:N}:{key}",
                    args,
                    null,
                    null,
                    null,
                    AuthorIsBot: true),
                ct);
            messageId = sent.MessageId.Value;
            sequence = sent.Sequence;
            posted = true;
        }

        var delivered = false;
        if (manifest.Callback is not null)
        {
            var deliveryId = Guid.NewGuid();
            var payload = JsonSerializer.Serialize(new
            {
                type = PluginCallbackDispatcher.CommandEvent,
                pluginId = plugin.PluginId,
                command = commandName,
                args,
                channelId = channel.Id.Value,
                workspaceId = channel.WorkspaceId.Value,
                actorUserId = profile.Id.Value,
                deliveryId
            }, Json);
            delivered = await callbacks.TryDispatchCommandAsync(plugin, payload, deliveryId, ct);
            if (!delivered)
            {
                return Results.Json(new { error = PluginPlatformErrors.CallbackFailed }, statusCode: StatusCodes.Status502BadGateway);
            }
        }

        if (!posted && !delivered)
        {
            return Results.Json(new { error = PluginPlatformErrors.CapabilityDenied }, statusCode: StatusCodes.Status403Forbidden);
        }

        var now = clock.UtcNow;
        var response = new PluginCommandResponse(
            PluginManifestRules.QualifiedCommand(plugin.PluginId, commandName),
            posted,
            messageId,
            sequence,
            delivered);
        audit.Add(new AuditEvent
        {
            TenantId = channel.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.IntegrationPluginCommand,
            EntityType = "InstalledPlugin",
            EntityId = plugin.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { pluginId = plugin.PluginId, command = commandName, channelId = channel.Id.Value }),
            OccurredAt = now
        });
        await idempotency.StoreAsync(new IdempotencyRecord(
            channel.TenantId,
            storeKey,
            hash,
            JsonSerializer.Serialize(response, Json),
            now), ct);
        await db.SaveChangesAsync(ct);
        return Results.Accepted($"/api/v1/channels/{channel.Id.Value}/messages", response);
    }
}
