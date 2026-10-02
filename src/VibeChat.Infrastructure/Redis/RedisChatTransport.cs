using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using StackExchange.Redis;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using NpgsqlTypes;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Role = VibeChat.SharedKernel.Role;

namespace VibeChat.Infrastructure;

public sealed class SignalRChatPublisher(IHubContext<ChatHub> hubContext) : IChatPublisher
{
    public Task PublishAsync(RealtimeMessage message, CancellationToken cancellationToken) =>
        hubContext.Clients.Group(ChatHub.ChannelGroup(message.TenantId, message.ChannelId))
            .SendAsync(
                message.EventName,
                RealtimePayloadNormalization.Normalize(message.Payload),
                cancellationToken);
}

internal static class RealtimePayloadNormalization
{
    public static object Normalize(object? payload) => payload switch
    {
        null => new JsonObject(),
        JsonNode node => node,
        JsonElement element => JsonNode.Parse(element.GetRawText()) ?? new JsonObject(),
        string json when !string.IsNullOrWhiteSpace(json) => JsonNode.Parse(json) ?? new JsonObject(),
        _ => payload
    };
}

public sealed class RedisChannelChatPublisher(RedisConnection redis) : IChatPublisher
{
    public const string ChannelName = "vibechat:realtime";

    public async Task PublishAsync(RealtimeMessage message, CancellationToken cancellationToken)
    {
        var subscriber = await redis.GetSubscriberAsync();
        if (subscriber is null)
        {
            return;
        }

        await subscriber.PublishAsync(
            RedisChannel.Literal(ChannelName),
            JsonSerializer.Serialize(new RedisRealtimeEnvelope(
                message.EventName,
                message.TenantId.Value,
                message.ChannelId.Value,
                RealtimePayloadNormalization.Normalize(message.Payload))));
    }
}

public sealed record RedisRealtimeEnvelope(string EventName, Guid TenantId, Guid ChannelId, object Payload, Guid? UserId = null);

public sealed class RedisSignalRBridge(RedisConnection redis, IHubContext<ChatHub> hubContext, ILogger<RedisSignalRBridge> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = await redis.GetSubscriberAsync();
        if (subscriber is null)
        {
            return;
        }

        await subscriber.SubscribeAsync(RedisChannel.Literal(RedisChannelChatPublisher.ChannelName), async (_, value) =>
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<RedisRealtimeEnvelope>(value.ToString());
                if (envelope is null)
                {
                    return;
                }

                // Normalize payload to JsonNode so the JS client receives an object, not a string.
                var payload = RealtimePayloadNormalization.Normalize(envelope.Payload);
                if (envelope.UserId is Guid userId && userId != Guid.Empty)
                {
                    await hubContext.Clients.Group(ChatHub.UserGroup(new TenantId(envelope.TenantId), new UserId(userId)))
                        .SendAsync(envelope.EventName, payload, stoppingToken);
                    return;
                }

                await hubContext.Clients.Group(ChatHub.ChannelGroup(
                        new TenantId(envelope.TenantId),
                        new ChannelId(envelope.ChannelId)))
                    .SendAsync(envelope.EventName, payload, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis realtime bridge failed to publish event");
            }
        });
    }
}
