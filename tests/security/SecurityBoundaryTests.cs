using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class SecurityBoundaryTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Cross_tenant_access_is_denied()
    {
        var foreignChannelId = await SeedCrossTenantChannelAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var get = await client.GetAsync($"/api/v1/channels/{foreignChannelId}/messages");
        get.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var post = await client.PostAsJsonAsync(
            $"/api/v1/channels/{foreignChannelId}/messages",
            new SendMessageRequest(Guid.NewGuid(), $"sec-tenant-{Guid.NewGuid():N}", "cross-tenant", null, null));
        post.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var channels = await client.GetAsync($"/api/v1/workspaces/{Guid.NewGuid()}/channels");
        channels.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_workspace_channel_access_is_denied()
    {
        var (foreignWorkspaceId, foreignChannelId) = await SeedSiblingWorkspaceChannelAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        // Alice is a member of the demo workspace, but not of the sibling workspace in the same tenant.
        var workspaceChannels = await client.GetAsync($"/api/v1/workspaces/{foreignWorkspaceId}/channels");
        workspaceChannels.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var messages = await client.GetAsync($"/api/v1/channels/{foreignChannelId}/messages");
        messages.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var post = await client.PostAsJsonAsync(
            $"/api/v1/channels/{foreignChannelId}/messages",
            new SendMessageRequest(Guid.NewGuid(), $"sec-ws-{Guid.NewGuid():N}", "cross-workspace", null, null));
        post.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Demo owner of the sibling workspace can access it.
        using var ownerClient = factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var ownerGet = await ownerClient.GetAsync($"/api/v1/channels/{foreignChannelId}/messages");
        ownerGet.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> SeedCrossTenantChannelAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var now = DateTimeOffset.UtcNow;

        var workspaceId = WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);
        var channelId = ChannelId.New();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Sec Tenant {workspaceId.Value:N}"[..Math.Min(160, $"Sec Tenant {workspaceId.Value:N}".Length)],
            Slug = $"sec-tenant-{workspaceId.Value:N}"[..Math.Min(120, $"sec-tenant-{workspaceId.Value:N}".Length)],
            AiEnabled = false,
            CreatedAt = now
        });
        db.Channels.Add(new Channel
        {
            Id = channelId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Name = "isolated",
            Type = ChannelType.Public,
            CreatedAt = now,
            CreatedBy = SeedData.DemoUserId
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            UserId = SeedData.DemoUserId,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });

        await db.SaveChangesAsync();
        return channelId.Value;
    }

    private async Task<(Guid WorkspaceId, Guid ChannelId)> SeedSiblingWorkspaceChannelAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var now = DateTimeOffset.UtcNow;

        // Same tenant as demo seed, but a workspace Alice is not a member of.
        var workspaceId = WorkspaceId.New();
        var channelId = ChannelId.New();
        var tenantId = SeedData.DemoTenantId;

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Sibling WS {workspaceId.Value:N}"[..Math.Min(160, $"Sibling WS {workspaceId.Value:N}".Length)],
            Slug = $"sibling-{workspaceId.Value:N}"[..Math.Min(120, $"sibling-{workspaceId.Value:N}".Length)],
            AiEnabled = false,
            CreatedAt = now
        });
        db.Channels.Add(new Channel
        {
            Id = channelId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Name = "owners-only",
            Type = ChannelType.Public,
            CreatedAt = now,
            CreatedBy = SeedData.DemoUserId
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            UserId = SeedData.DemoUserId,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });

        await db.SaveChangesAsync();
        return (workspaceId.Value, channelId.Value);
    }
}
