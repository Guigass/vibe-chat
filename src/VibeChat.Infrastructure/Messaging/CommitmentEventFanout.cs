using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.BuildingBlocks;
using VibeChat.Messaging;
using VibeChat.Realtime;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure.Messaging;

public static class CommitmentEventFanout
{
    public static async Task DispatchAsync(
        IServiceProvider services,
        IChatPublisher publisher,
        OutboxMessage outbox,
        CancellationToken cancellationToken)
    {
        var root = JsonNode.Parse(outbox.Payload)?.AsObject()
            ?? throw new InvalidOperationException("Invalid commitment outbox payload");
        var tenantId = new TenantId(RequiredGuid(root, "tenantId"));
        var visibility = root["visibility"]?.GetValue<string>() ?? CommitmentPolicies.VisibilityToken(CommitmentVisibility.Channel);
        if (visibility == CommitmentPolicies.VisibilityToken(CommitmentVisibility.Restricted))
        {
            var hub = services.GetService<IHubContext<ChatHub>>();
            if (hub is null)
            {
                return;
            }

            var users = new HashSet<Guid>();
            AddUser(users, root, "createdBy");
            AddUser(users, root, "ownerUserId");
            foreach (var userId in users)
            {
                await hub.Clients.Group(ChatHub.UserGroup(tenantId, new UserId(userId)))
                    .SendAsync(outbox.Type, root, cancellationToken);
            }

            return;
        }

        var channelId = new ChannelId(RequiredGuid(root, "channelId"));
        await publisher.PublishAsync(new RealtimeMessage(outbox.Type, tenantId, channelId, root), cancellationToken);
    }

    private static void AddUser(HashSet<Guid> users, JsonObject root, string name)
    {
        if (root[name] is JsonValue value && value.TryGetValue<Guid>(out var userId) && userId != Guid.Empty)
        {
            users.Add(userId);
        }
    }

    private static Guid RequiredGuid(JsonObject root, string name) =>
        root[name]?.GetValue<Guid>() ?? throw new InvalidOperationException($"Commitment outbox missing {name}");
}
