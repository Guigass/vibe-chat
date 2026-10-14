using System.Text.Json.Nodes;
using VibeChat.BuildingBlocks;
using VibeChat.Messaging;
using VibeChat.Realtime;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure.Messaging;

public static class KnowledgeEventFanout
{
    public static async Task DispatchAsync(
        IChatPublisher publisher,
        OutboxMessage outbox,
        CancellationToken cancellationToken)
    {
        var root = JsonNode.Parse(outbox.Payload)?.AsObject()
            ?? throw new InvalidOperationException("Invalid knowledge outbox payload");
        if (root["channelId"] is not JsonValue channelValue || !channelValue.TryGetValue<Guid>(out var channelRaw) || channelRaw == Guid.Empty)
        {
            return;
        }

        var tenantId = new TenantId(root["tenantId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Knowledge outbox missing tenantId"));
        await publisher.PublishAsync(
            new RealtimeMessage(outbox.Type, tenantId, new ChannelId(channelRaw), root),
            cancellationToken);
    }
}
