using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-186 — cross-tenant e acesso após remoção (history + JoinChannel).</summary>
[Collection(SecurityCollection.Name)]
public sealed class ChannelMembershipSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Removed_member_loses_history_and_hub_and_foreign_user_is_rejected()
    {
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var create = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name = $"sec-{Guid.NewGuid():N}"[..24], type = "Private" });
        create.EnsureSuccessStatusCode();
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions))!;

        var foreignUserId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.UserProfiles.Add(new UserProfile
            {
                Id = new UserId(foreignUserId),
                Subject = $"pending:foreign-{foreignUserId:N}@x.test",
                Email = $"foreign-{foreignUserId:N}@x.test",
                DisplayName = "Foreign",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var foreignAdd = await alice.PostAsJsonAsync(
            Roster(channel.Id),
            new { userId = foreignUserId });
        foreignAdd.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (_, foreignChannelId) = await SeedForeignChannelAsync();
        var crossList = await alice.GetAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/{foreignChannelId}/members");
        crossList.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var added = await alice.PostAsJsonAsync(Roster(channel.Id), new { userId = SeedData.BobUserId.Value });
        added.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var hub = CreateHubConnection("bob"))
        {
            await hub.StartAsync();
            await hub.InvokeAsync("JoinChannel", SeedData.DemoTenantId.Value, channel.Id);
        }

        var removed = await alice.DeleteAsync($"{Roster(channel.Id)}/{SeedData.BobUserId.Value}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var history = await bob.GetAsync($"/api/v1/channels/{channel.Id}/messages");
        history.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var after = CreateHubConnection("bob");
        await after.StartAsync();
        var join = async () => await after.InvokeAsync("JoinChannel", SeedData.DemoTenantId.Value, channel.Id);
        await join.Should().ThrowAsync<HubException>();
    }

    private static string Roster(Guid channelId) =>
        $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/{channelId}/members";

    private HttpClient CreateClient(string devUser)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", devUser);
        return client;
    }

    private HubConnection CreateHubConnection(string devUser) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress!, "hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Headers["X-Dev-User"] = devUser;
            })
            .Build();

    private async Task<(Guid TenantId, Guid ChannelId)> SeedForeignChannelAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);
        var channelId = ChannelId.New();
        db.Workspaces.Add(new VibeChat.Tenancy.Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Sec {workspaceId.Value:N}"[..32],
            Slug = $"sec-{workspaceId.Value:N}"[..32],
            AiEnabled = false,
            CreatedAt = now
        });
        db.Channels.Add(new VibeChat.Conversations.Channel
        {
            Id = channelId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Name = "isolated",
            Type = VibeChat.Conversations.ChannelType.Private,
            CreatedAt = now,
            CreatedBy = SeedData.DemoUserId
        });
        await db.SaveChangesAsync();
        return (tenantId.Value, channelId.Value);
    }

    private sealed record ChannelDto(Guid Id);
}
