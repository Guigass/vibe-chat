using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Integrations;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public interface IPluginCallbackDispatcher
{
    Task TryDispatchEventAsync(
        TenantId tenantId,
        string eventName,
        Guid deliveryId,
        string payloadJson,
        Guid channelId,
        CancellationToken cancellationToken);

    Task<bool> TryDispatchCommandAsync(
        InstalledPlugin plugin,
        string payloadJson,
        Guid deliveryId,
        CancellationToken cancellationToken);
}

/// <summary>
/// HTTPS callback for plugin events and slash commands (B-066). HMAC matches B-048.
/// Failures are logged without the secret or the body.
/// </summary>
public sealed class PluginCallbackDispatcher(
    VibeChatDbContext db,
    RuntimeSecretProtector secrets,
    IHttpClientFactory httpClientFactory,
    IOptions<PluginPlatformOptions> options,
    ILogger<PluginCallbackDispatcher> logger) : IPluginCallbackDispatcher
{
    public const string HttpClientName = "PluginCallbacks";
    public const string CommandEvent = "plugin.command.invoked";

    public async Task TryDispatchEventAsync(
        TenantId tenantId,
        string eventName,
        Guid deliveryId,
        string payloadJson,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !WebhookEventTypes.IsSubscribable(eventName))
        {
            return;
        }

        List<InstalledPlugin> plugins;
        try
        {
            plugins = await db.InstalledPlugins.AsNoTracking()
                .Where(plugin => plugin.TenantId == tenantId && plugin.Enabled && plugin.CallbackUrl != null)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Plugin callback lookup failed for tenant {TenantId}", tenantId.Value);
            return;
        }

        Guid? authorId = ReadAuthor(payloadJson);
        foreach (var plugin in plugins)
        {
            if (!plugin.Capabilities.Contains(PluginManifestRules.CapabilityEventsSubscribe, StringComparer.Ordinal)
                || !plugin.SubscribedEvents.Contains(eventName, StringComparer.Ordinal)
                || !plugin.CallbackSecret.IsPresent)
            {
                continue;
            }

            var bot = await db.IntegrationBots.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == plugin.BotId && item.TenantId == tenantId, cancellationToken);
            if (bot is null || !bot.Enabled || (authorId is Guid author && author == bot.UserId.Value))
            {
                continue;
            }

            var inScope = await db.IntegrationBotChannelScopes.AsNoTracking().AnyAsync(
                scope => scope.BotId == plugin.BotId && scope.ChannelId == new ChannelId(channelId),
                cancellationToken);
            if (!inScope)
            {
                continue;
            }

            await DeliverAsync(plugin, eventName, deliveryId, payloadJson, cancellationToken);
        }
    }

    public Task<bool> TryDispatchCommandAsync(
        InstalledPlugin plugin,
        string payloadJson,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(plugin.CallbackUrl) || !plugin.CallbackSecret.IsPresent)
        {
            return Task.FromResult(false);
        }

        return DeliverAsync(plugin, CommandEvent, deliveryId, payloadJson, cancellationToken);
    }

    private async Task<bool> DeliverAsync(
        InstalledPlugin plugin,
        string eventName,
        Guid deliveryId,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(plugin.CallbackUrl)
                || !await PluginManifestRules.IsDeliveryAllowedAsync(plugin.CallbackUrl, cancellationToken))
            {
                logger.LogWarning("Plugin callback blocked for plugin {PluginId}", plugin.Id);
                return false;
            }

            var secret = secrets.Unprotect(
                plugin.CallbackSecret,
                RuntimeSecretKinds.PluginCallbackSecret,
                plugin.TenantId,
                plugin.WorkspaceId,
                plugin.Id.ToString("D"));
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, plugin.CallbackUrl)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation(WebhookDelivery.EventHeader, eventName);
            request.Headers.TryAddWithoutValidation(WebhookDelivery.DeliveryIdHeader, deliveryId.ToString("D"));
            request.Headers.TryAddWithoutValidation(
                WebhookDelivery.SignatureHeader,
                WebhookDelivery.ComputeSignature(secret, payloadJson));
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode || (int)response.StatusCode is >= 300 and < 400)
            {
                logger.LogWarning(
                    "Plugin callback {DeliveryId} for plugin {PluginId} returned {StatusCode}",
                    deliveryId,
                    plugin.Id,
                    (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Plugin callback {DeliveryId} for plugin {PluginId} failed", deliveryId, plugin.Id);
            return false;
        }
    }

    private static Guid? ReadAuthor(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("authorId", out var author) && author.TryGetGuid(out var id))
            {
                return id;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
