using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
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

    [Fact]
    public async Task Non_author_cannot_edit_or_delete_message()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var bob = factory.CreateClient();
        bob.DefaultRequestHeaders.Add("X-Dev-User", "bob");

        var messageId = Guid.NewGuid();
        var create = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(messageId, $"sec-edit-{messageId:N}", $"owned-by-alice-{messageId:N}", null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var edit = await bob.PutAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}",
            new EditMessageRequest("hijack"));
        edit.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var delete = await bob.DeleteAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}");
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_tenant_cannot_download_or_initiate_attachments()
    {
        var foreignChannelId = await SeedCrossTenantChannelAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var initiate = await client.PostAsJsonAsync(
            $"/api/v1/channels/{foreignChannelId}/attachments",
            new CreateAttachmentUploadRequest("secret.txt", "text/plain", 12));
        initiate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var download = await client.GetAsync(
            $"/api/v1/channels/{foreignChannelId}/attachments/{Guid.NewGuid()}/download");
        download.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Non_member_cannot_download_attachment_from_direct_message()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var open = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/dms",
            new OpenDirectMessageRequest(SeedData.BobUserId.Value));
        open.EnsureSuccessStatusCode();
        var dm = await open.Content.ReadFromJsonAsync<ChannelDto>();
        dm.Should().NotBeNull();

        var content = "dm-secret"u8.ToArray();
        var initiate = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{dm!.Id}/attachments",
            new CreateAttachmentUploadRequest("dm.txt", "text/plain", content.Length));
        initiate.EnsureSuccessStatusCode();
        var upload = await initiate.Content.ReadFromJsonAsync<AttachmentUploadDto>();
        upload.Should().NotBeNull();

        using var putClient = new HttpClient();
        var put = await putClient.PutAsync(upload!.UploadUrl, new ByteArrayContent(content));
        put.IsSuccessStatusCode.Should().BeTrue();

        var complete = await alice.PostAsync(
            $"/api/v1/channels/{dm.Id}/attachments/{upload.AttachmentId}/complete",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        complete.EnsureSuccessStatusCode();

        var forbidden = await demo.GetAsync(
            $"/api/v1/channels/{dm.Id}/attachments/{upload.AttachmentId}/download");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_tenant_search_is_denied_for_foreign_workspace()
    {
        var (foreignWorkspaceId, _) = await SeedCrossTenantWorkspaceWithMessageAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var response = await client.GetAsync(
            $"/api/v1/search/messages?workspaceId={foreignWorkspaceId}&q=secret");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Non_member_cannot_find_direct_message_via_search()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var open = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/dms",
            new OpenDirectMessageRequest(SeedData.BobUserId.Value));
        open.EnsureSuccessStatusCode();
        var dm = await open.Content.ReadFromJsonAsync<ChannelDto>();
        dm.Should().NotBeNull();

        var messageId = Guid.NewGuid();
        var token = $"dmsearch{messageId:N}";
        var create = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{dm!.Id}/messages",
            new SendMessageRequest(messageId, $"sec-search-dm-{messageId:N}", $"privado {token}", null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var aliceHits = await alice.GetFromJsonAsync<SearchMessagesDto>(
            $"/api/v1/search/messages?workspaceId={SeedData.DemoWorkspaceId.Value}&q={token}");
        aliceHits.Should().NotBeNull();
        aliceHits!.Items.Should().Contain(x => x.MessageId == messageId);

        var demoHits = await demo.GetFromJsonAsync<SearchMessagesDto>(
            $"/api/v1/search/messages?workspaceId={SeedData.DemoWorkspaceId.Value}&q={token}");
        demoHits.Should().NotBeNull();
        demoHits!.Items.Should().NotContain(x => x.MessageId == messageId);
    }

    [Fact]
    public async Task Direct_message_is_hidden_from_non_members()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var open = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/dms",
            new OpenDirectMessageRequest(SeedData.BobUserId.Value));
        open.EnsureSuccessStatusCode();
        var dm = await open.Content.ReadFromJsonAsync<ChannelDto>();
        dm.Should().NotBeNull();

        var forbidden = await demo.GetAsync($"/api/v1/channels/{dm!.Id}/messages");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var post = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{dm.Id}/messages",
            new SendMessageRequest(Guid.NewGuid(), $"sec-dm-{Guid.NewGuid():N}", "nope", null, null));
        post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ChannelDto(Guid Id, Guid WorkspaceId, string Name, string Type);
    private sealed record AttachmentUploadDto(Guid AttachmentId, string UploadUrl);
    private sealed record SearchMessageHitDto(Guid MessageId, Guid ChannelId, string BodyPreview);
    private sealed record SearchMessagesDto(string Query, int Limit, SearchMessageHitDto[] Items);

    private async Task<(Guid WorkspaceId, Guid ChannelId)> SeedCrossTenantWorkspaceWithMessageAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var now = DateTimeOffset.UtcNow;

        var workspaceId = WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);
        var channelId = ChannelId.New();
        var messageId = MessageId.New();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Sec Search Tenant {workspaceId.Value:N}"[..Math.Min(160, $"Sec Search Tenant {workspaceId.Value:N}".Length)],
            Slug = $"sec-search-{workspaceId.Value:N}"[..Math.Min(120, $"sec-search-{workspaceId.Value:N}".Length)],
            AiEnabled = false,
            CreatedAt = now
        });
        db.Channels.Add(new Channel
        {
            Id = channelId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Name = "isolated-search",
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
        db.Messages.Add(new Message
        {
            Id = messageId,
            TenantId = tenantId,
            ConversationId = channelId,
            AuthorId = SeedData.DemoUserId,
            Sequence = 1,
            Body = "secret cross tenant payload",
            CreatedAt = now
        });

        await db.SaveChangesAsync();
        return (workspaceId.Value, channelId.Value);
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
