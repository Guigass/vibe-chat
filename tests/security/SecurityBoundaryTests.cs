using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Audit;
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
    public async Task Cross_tenant_cannot_toggle_reactions()
    {
        var foreignChannelId = await SeedCrossTenantChannelAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var react = await client.PutAsJsonAsync(
            $"/api/v1/channels/{foreignChannelId}/messages/{Guid.NewGuid()}/reactions",
            new { emoji = "👍" });
        react.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_cannot_read_admin_audit_events()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var response = await alice.GetAsync("/api/v1/admin/audit-events?limit=10");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_cannot_read_admin_conversation_audit()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var list = await alice.GetAsync("/api/v1/admin/conversations");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var history = await alice.GetAsync(
            $"/api/v1/admin/conversations/{SeedData.DemoChannelId.Value}/messages?limit=10");
        history.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_conversation_audit_is_tenant_scoped()
    {
        var foreignChannelId = await SeedCrossTenantChannelAsync();

        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var list = await demo.GetAsync("/api/v1/admin/conversations?limit=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var conversations = await list.Content.ReadFromJsonAsync<AdminConversationsDto>();
        conversations.Should().NotBeNull();
        conversations!.Items.Should().NotContain(x => x.Id == foreignChannelId);

        var foreignHistory = await demo.GetAsync($"/api/v1/admin/conversations/{foreignChannelId}/messages");
        foreignHistory.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_can_audit_direct_message_without_channel_membership()
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

        var memberPath = await demo.GetAsync($"/api/v1/channels/{dm!.Id}/messages");
        memberPath.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var secret = $"admin-audit-dm-{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{dm.Id}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", secret, null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var adminHistory = await demo.GetAsync($"/api/v1/admin/conversations/{dm.Id}/messages?limit=50");
        adminHistory.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await adminHistory.Content.ReadFromJsonAsync<AdminConversationMessagesDto>();
        payload.Should().NotBeNull();
        payload!.Items.Should().Contain(x => x.Id == messageId && x.Body == secret);
    }

    [Fact]
    public async Task Member_cannot_read_or_write_sensitive_settings()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var get = await alice.GetAsync(
            $"/api/v1/admin/settings?workspaceId={SeedData.DemoWorkspaceId.Value}");
        get.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var put = await alice.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new
            {
                workspaceId = SeedData.DemoWorkspaceId.Value,
                ai = new { workspaceEnabled = true, provider = "Mock" }
            });
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_tenant_cannot_read_sensitive_settings()
    {
        var foreignWorkspaceId = await SeedCrossTenantWorkspaceAsync();

        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var response = await alice.GetAsync($"/api/v1/admin/settings?workspaceId={foreignWorkspaceId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var put = await alice.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new
            {
                workspaceId = foreignWorkspaceId,
                email = new { enabled = true }
            });
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_cannot_self_elevate_role()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var response = await alice.PutAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.AliceUserId.Value}/role",
            new { role = "Admin" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var roles = await alice.GetAsync($"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/roles");
        roles.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_tenant_cannot_change_member_role()
    {
        var foreignWorkspaceId = await SeedCrossTenantWorkspaceAsync();

        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var response = await alice.PutAsJsonAsync(
            $"/api/v1/workspaces/{foreignWorkspaceId}/members/{SeedData.DemoUserId.Value}/role",
            new { role = "Moderator" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_cannot_invite_workspace_member()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var response = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members",
            new { email = $"intruder-{Guid.NewGuid():N}@vibechat.local", role = "Member" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_tenant_cannot_invite_workspace_member()
    {
        var foreignWorkspaceId = await SeedCrossTenantWorkspaceAsync();

        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var response = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{foreignWorkspaceId}/members",
            new { email = $"x-tenant-{Guid.NewGuid():N}@vibechat.local", role = "Member" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_audit_events_are_tenant_scoped()
    {
        var foreignAction = $"foreign.audit.{Guid.NewGuid():N}";
        await SeedForeignTenantAuditEventAsync(foreignAction);

        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        // Ensure at least one local audit event exists (admin login path on /me).
        (await demo.GetAsync("/api/v1/me")).EnsureSuccessStatusCode();

        var response = await demo.GetAsync("/api/v1/admin/audit-events?limit=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuditEventsDto>();
        payload.Should().NotBeNull();
        payload!.Items.Should().NotBeEmpty();
        payload.Items.Should().NotContain(x => x.Action == foreignAction);
        payload.Items.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Action));
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

        var aliceSearch = await alice.GetAsync(
            $"/api/v1/search/messages?workspaceId={SeedData.DemoWorkspaceId.Value}&q={token}");
        var aliceBody = await aliceSearch.Content.ReadAsStringAsync();
        aliceSearch.IsSuccessStatusCode.Should().BeTrue($"search failed: {(int)aliceSearch.StatusCode} {aliceBody}");
        var aliceHits = System.Text.Json.JsonSerializer.Deserialize<SearchMessagesDto>(
            aliceBody,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        aliceHits.Should().NotBeNull();
        aliceHits!.Items.Should().Contain(x => x.MessageId == messageId);

        var demoHits = await demo.GetFromJsonAsync<SearchMessagesDto>(
            $"/api/v1/search/messages?workspaceId={SeedData.DemoWorkspaceId.Value}&q={token}");
        demoHits.Should().NotBeNull();
        demoHits!.Items.Should().NotContain(x => x.MessageId == messageId);
    }

    [Fact]
    public async Task Cross_tenant_cannot_open_or_reply_in_thread()
    {
        var foreignChannelId = await SeedCrossTenantChannelAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var open = await client.PostAsync(
            $"/api/v1/channels/{foreignChannelId}/messages/{Guid.NewGuid()}/threads",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        open.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var reply = await client.PostAsJsonAsync(
            $"/api/v1/threads/{Guid.NewGuid()}/messages",
            new SendMessageRequest(Guid.NewGuid(), $"sec-thread-{Guid.NewGuid():N}", "nope", null, null));
        reply.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        var history = await client.GetAsync($"/api/v1/threads/{Guid.NewGuid()}/messages");
        history.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cross_tenant_cannot_summarize_channel_with_ai()
    {
        var (foreignWorkspaceId, foreignChannelId) = await SeedCrossTenantWorkspaceWithMessageAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var summarize = await client.PostAsync(
            $"/api/v1/workspaces/{foreignWorkspaceId}/channels/{foreignChannelId}/ai/summarize",
            content: null);
        summarize.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_tenant_cannot_list_or_create_spaces()
    {
        var (foreignWorkspaceId, _) = await SeedCrossTenantWorkspaceWithMessageAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var list = await client.GetAsync($"/api/v1/workspaces/{foreignWorkspaceId}/spaces");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var createSpace = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{foreignWorkspaceId}/spaces",
            new { name = "intruder" });
        createSpace.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var createChannel = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{foreignWorkspaceId}/channels",
            new { name = "intruder-ch", type = "Public" });
        createChannel.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var presence = await client.GetAsync($"/api/v1/workspaces/{foreignWorkspaceId}/presence");
        presence.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
    private sealed record AuditEventItemDto(Guid Id, string Action, string EntityType, string? EntityId, Guid? ActorUserId, DateTimeOffset OccurredAt, string MetadataJson);
    private sealed record AuditEventsDto(AuditEventItemDto[] Items);
    private sealed record AdminConversationItemDto(Guid Id, Guid WorkspaceId, string Name, string Type);
    private sealed record AdminConversationsDto(AdminConversationItemDto[] Items);
    private sealed record AdminConversationMessageItemDto(
        Guid Id,
        Guid ChannelId,
        long Sequence,
        Guid AuthorId,
        string Body,
        DateTimeOffset? DeletedAt,
        Guid? DeletedBy);
    private sealed record AdminConversationMessagesDto(AdminConversationMessageItemDto[] Items);

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

    private async Task SeedForeignTenantAuditEventAsync(string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var workspaceId = WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);
        var now = DateTimeOffset.UtcNow;

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Audit Tenant {workspaceId.Value:N}"[..Math.Min(160, $"Audit Tenant {workspaceId.Value:N}".Length)],
            Slug = $"audit-tenant-{workspaceId.Value:N}"[..Math.Min(120, $"audit-tenant-{workspaceId.Value:N}".Length)],
            AiEnabled = false,
            CreatedAt = now
        });
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = SeedData.DemoUserId,
            Action = action,
            EntityType = "Workspace",
            EntityId = workspaceId.Value.ToString(),
            MetadataJson = "{}",
            OccurredAt = now
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCrossTenantWorkspaceAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var now = DateTimeOffset.UtcNow;

        var workspaceId = WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Sec Role Tenant {workspaceId.Value:N}"[..Math.Min(160, $"Sec Role Tenant {workspaceId.Value:N}".Length)],
            Slug = $"sec-role-{workspaceId.Value:N}"[..Math.Min(120, $"sec-role-{workspaceId.Value:N}".Length)],
            AiEnabled = false,
            CreatedAt = now
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
        return workspaceId.Value;
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
