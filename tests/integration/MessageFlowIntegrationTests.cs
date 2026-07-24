using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MessageFlowIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid DemoChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Send_message_persists_and_creates_outbox()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var body = $"integration-persist-{messageId:N}";
        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        accepted.Should().NotBeNull();
        accepted!.Id.Should().Be(messageId);
        accepted.Body.Should().Be(body);
        accepted.Sequence.Should().BeGreaterThan(0);

        var list = await client.GetFromJsonAsync<MessageDto[]>(
            $"/api/v1/channels/{DemoChannelId}/messages?after={accepted.Sequence - 1}",
            JsonOptions);
        list.Should().NotBeNull();
        list!.Should().Contain(m => m.Id == messageId && m.Body == body);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var persisted = await db.Messages.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == new MessageId(messageId));
        persisted.Body.Should().Be(body);

        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == nameof(MessageCreatedEvent))
            .OrderByDescending(x => x.OccurredAt)
            .Take(20)
            .ToListAsync();
        outbox.Should().Contain(x => x.Payload.Contains(messageId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Idempotent_send_returns_existing_sequence()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var idempotencyKey = $"idem-dup-{messageId:N}";
        var payload = new SendMessageRequest(messageId, idempotencyKey, $"idempotent-body-{messageId:N}", null, null);

        var first = await client.PostAsJsonAsync($"/api/v1/channels/{DemoChannelId}/messages", payload);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstDto = await first.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);

        var second = await client.PostAsJsonAsync($"/api/v1/channels/{DemoChannelId}/messages", payload);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var secondDto = await second.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);

        firstDto.Should().NotBeNull();
        secondDto.Should().NotBeNull();
        secondDto!.Sequence.Should().Be(firstDto!.Sequence);
        secondDto.Id.Should().Be(firstDto.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var count = await db.Messages.IgnoreQueryFilters().CountAsync(x => x.Id == new MessageId(messageId));
        count.Should().Be(1);
    }

    [Fact]
    public async Task Toggle_reaction_persists_and_creates_outbox()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var create = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-react-{messageId:N}", $"react-me-{messageId:N}", null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var add = await client.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{messageId}/reactions",
            new ToggleReactionRequestDto("👍"));
        add.StatusCode.Should().Be(HttpStatusCode.OK);
        var added = await add.Content.ReadFromJsonAsync<ToggleReactionResponseDto>(JsonOptions);
        added.Should().NotBeNull();
        added!.Added.Should().BeTrue();
        added.Emoji.Should().Be("👍");
        added.Reactions.Should().ContainSingle(r => r.Emoji == "👍" && r.Count == 1 && r.Me);

        var list = await client.GetFromJsonAsync<MessageDto[]>(
            $"/api/v1/channels/{DemoChannelId}/messages?after=0&limit=100",
            JsonOptions);
        list!.Single(m => m.Id == messageId).Reactions.Should()
            .ContainSingle(r => r.Emoji == "👍" && r.Count == 1 && r.Me);

        var remove = await client.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{messageId}/reactions",
            new ToggleReactionRequestDto("👍"));
        remove.StatusCode.Should().Be(HttpStatusCode.OK);
        var removed = await remove.Content.ReadFromJsonAsync<ToggleReactionResponseDto>(JsonOptions);
        removed!.Added.Should().BeFalse();
        removed.Reactions.Should().BeEmpty();

        var bad = await client.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{messageId}/reactions",
            new ToggleReactionRequestDto("🚀"));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == nameof(ReactionChangedEvent))
            .OrderByDescending(x => x.OccurredAt)
            .Take(20)
            .ToListAsync();
        outbox.Should().Contain(x =>
            x.Payload.Contains(messageId.ToString(), StringComparison.OrdinalIgnoreCase)
            && x.Payload.Contains("\"added\":true", StringComparison.Ordinal)
            && (x.Payload.Contains("👍", StringComparison.Ordinal)
                || x.Payload.Contains("\\uD83D\\uDC4D", StringComparison.Ordinal)));
        (await db.Reactions.IgnoreQueryFilters().CountAsync(x => x.MessageId == new MessageId(messageId)))
            .Should().Be(0);
    }

    [Fact]
    public async Task Soft_delete_hides_message_body()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var create = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-del-{messageId:N}", $"delete-me-{messageId:N}", null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await create.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);

        var delete = await client.DeleteAsync($"/api/v1/channels/{DemoChannelId}/messages/{messageId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<MessageDto[]>(
            $"/api/v1/channels/{DemoChannelId}/messages?after={created!.Sequence - 1}",
            JsonOptions);
        var softDeleted = list!.Single(m => m.Id == messageId);
        softDeleted.Body.Should().BeEmpty();
        softDeleted.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Edit_message_updates_body_and_edited_at()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var create = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-edit-{messageId:N}", $"edit-me-{messageId:N}", null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var editedBody = $"edited-{messageId:N}";
        var edit = await client.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{messageId}",
            new EditMessageRequest(editedBody));
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        var edited = await edit.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        edited.Should().NotBeNull();
        edited!.Body.Should().Be(editedBody);
        edited.EditedAt.Should().NotBeNull();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == nameof(MessageEditedEvent))
            .OrderByDescending(x => x.OccurredAt)
            .Take(20)
            .ToListAsync();
        outbox.Should().Contain(x => x.Payload.Contains(messageId.ToString(), StringComparison.OrdinalIgnoreCase)
            && x.Payload.Contains(editedBody, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Thread_create_reply_uses_separate_sequence_and_outbox()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var parentId = Guid.NewGuid();
        var parentCreate = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(parentId, $"idem-thread-parent-{parentId:N}", $"parent-{parentId:N}", null, null));
        parentCreate.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var parent = await parentCreate.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        parent.Should().NotBeNull();

        var open1 = await client.PostAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{parentId}/threads",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        open1.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread1 = await open1.Content.ReadFromJsonAsync<ThreadDto>(JsonOptions);
        thread1.Should().NotBeNull();
        thread1!.ParentMessageId.Should().Be(parentId);
        thread1.ChannelId.Should().Be(DemoChannelId);

        var open2 = await client.PostAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{parentId}/threads",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        open2.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread2 = await open2.Content.ReadFromJsonAsync<ThreadDto>(JsonOptions);
        thread2!.Id.Should().Be(thread1.Id);

        var replyId = Guid.NewGuid();
        var replyBody = $"thread-reply-{replyId:N}";
        var reply = await client.PostAsJsonAsync(
            $"/api/v1/threads/{thread1.Id}/messages",
            new SendMessageRequest(replyId, $"idem-thread-reply-{replyId:N}", replyBody, parentId, thread1.Id));
        reply.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var replyDto = await reply.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        replyDto.Should().NotBeNull();
        replyDto!.ThreadId.Should().Be(thread1.Id);
        replyDto.ChannelId.Should().Be(DemoChannelId);
        replyDto.ConversationId.Should().Be(thread1.Id);
        replyDto.Sequence.Should().Be(1);

        var channelMessages = await client.GetFromJsonAsync<MessageDto[]>(
            $"/api/v1/channels/{DemoChannelId}/messages?after={parent!.Sequence - 1}",
            JsonOptions);
        channelMessages.Should().NotBeNull();
        channelMessages!.Should().Contain(m => m.Id == parentId && m.ThreadId == thread1.Id && m.ReplyCount >= 1);
        channelMessages.Should().NotContain(m => m.Id == replyId);

        var threadMessages = await client.GetFromJsonAsync<MessageDto[]>(
            $"/api/v1/threads/{thread1.Id}/messages",
            JsonOptions);
        threadMessages.Should().Contain(m => m.Id == replyId && m.Body == replyBody && m.Sequence == 1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var persisted = await db.Messages.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == new MessageId(replyId));
        persisted.ConversationId.Value.Should().Be(thread1.Id);
        persisted.ThreadId.Should().Be(thread1.Id);

        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == nameof(MessageCreatedEvent))
            .OrderByDescending(x => x.OccurredAt)
            .Take(20)
            .ToListAsync();
        outbox.Should().Contain(x =>
            x.Payload.Contains(replyId.ToString(), StringComparison.OrdinalIgnoreCase)
            && x.Payload.Contains(thread1.Id.ToString(), StringComparison.OrdinalIgnoreCase)
            && x.Payload.Contains(DemoChannelId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Spaces_and_channels_can_be_created_with_membership()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var workspaceId = SeedData.DemoWorkspaceId.Value;
        var spaces = await alice.GetFromJsonAsync<SpaceDto[]>(
            $"/api/v1/workspaces/{workspaceId}/spaces",
            JsonOptions);
        spaces.Should().NotBeNull();
        spaces!.Should().Contain(s => s.Id == SeedData.DemoSpaceGeralId);
        spaces.Should().Contain(s => s.Id == SeedData.DemoSpaceEngenhariaId);

        var createSpace = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/spaces",
            new CreateSpaceRequestDto($"Space {Guid.NewGuid():N}"[..20]));
        createSpace.StatusCode.Should().Be(HttpStatusCode.Created);
        var space = await createSpace.Content.ReadFromJsonAsync<SpaceDto>(JsonOptions);
        space.Should().NotBeNull();

        var channelName = $"ch-{Guid.NewGuid():N}"[..16];
        var createChannel = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/channels",
            new CreateChannelRequestDto(channelName, "Public", space!.Id));
        createChannel.StatusCode.Should().Be(HttpStatusCode.Created);
        var channel = await createChannel.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions);
        channel.Should().NotBeNull();
        channel!.SpaceId.Should().Be(space.Id);
        channel.Name.Should().Be(channelName);

        var listed = await alice.GetFromJsonAsync<ChannelDto[]>(
            $"/api/v1/workspaces/{workspaceId}/channels",
            JsonOptions);
        listed.Should().Contain(c => c.Id == channel.Id && c.SpaceId == space.Id);

        var presence = await alice.GetFromJsonAsync<PresenceDto[]>(
            $"/api/v1/workspaces/{workspaceId}/presence",
            JsonOptions);
        presence.Should().NotBeNull();
        presence!.Select(p => p.UserId).Should().Contain(SeedData.AliceUserId.Value);

        var badSpace = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/channels",
            new CreateChannelRequestDto($"bad-{Guid.NewGuid():N}"[..12], "Public", Guid.NewGuid()));
        badSpace.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Direct_message_is_idempotent_and_private_to_members()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var bob = factory.CreateClient();
        bob.DefaultRequestHeaders.Add("X-Dev-User", "bob");
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var workspaceId = SeedData.DemoWorkspaceId.Value;
        var members = await alice.GetFromJsonAsync<WorkspaceMemberDto[]>(
            $"/api/v1/workspaces/{workspaceId}/members",
            JsonOptions);
        members.Should().NotBeNull();
        members!.Select(m => m.UserId).Should().Contain(SeedData.BobUserId.Value);

        var open1 = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/dms",
            new OpenDirectMessageRequest(SeedData.BobUserId.Value));
        open1.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var dm1 = await open1.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions);
        dm1.Should().NotBeNull();
        dm1!.Type.Should().Be("Direct");
        dm1.PeerUserId.Should().Be(SeedData.BobUserId.Value);

        var open2 = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/dms",
            new OpenDirectMessageRequest(SeedData.BobUserId.Value));
        open2.StatusCode.Should().Be(HttpStatusCode.OK);
        var dm2 = await open2.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions);
        dm2!.Id.Should().Be(dm1.Id);

        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{dm1.Id}/messages",
            new SendMessageRequest(messageId, $"idem-dm-{messageId:N}", $"dm-hi-{messageId:N}", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var bobList = await bob.GetFromJsonAsync<MessageDto[]>(
            $"/api/v1/channels/{dm1.Id}/messages",
            JsonOptions);
        bobList.Should().Contain(m => m.Id == messageId);

        var outsider = await demo.GetAsync($"/api/v1/channels/{dm1.Id}/messages");
        outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var aliceChannels = await alice.GetFromJsonAsync<ChannelDto[]>(
            $"/api/v1/workspaces/{workspaceId}/channels",
            JsonOptions);
        aliceChannels.Should().Contain(c => c.Id == dm1.Id);

        var demoChannels = await demo.GetFromJsonAsync<ChannelDto[]>(
            $"/api/v1/workspaces/{workspaceId}/channels",
            JsonOptions);
        demoChannels.Should().NotContain(c => c.Id == dm1.Id);
    }

    [Fact]
    public async Task Health_checks_return_summary()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        HealthSummaryDto? summary = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            summary = await client.GetFromJsonAsync<HealthSummaryDto>("/api/v1/admin/health-summary", JsonOptions);
            if (summary?.Checks.GetValueOrDefault("postgres") == "Healthy"
                && summary.Checks.GetValueOrDefault("redis") == "Healthy"
                && summary.Checks.GetValueOrDefault("minio") == "Healthy")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
        }

        summary.Should().NotBeNull();
        summary!.Checks.Should().ContainKey("postgres").WhoseValue.Should().Be("Healthy");
        summary.Checks.Should().ContainKey("redis").WhoseValue.Should().Be("Healthy");
        summary.Checks.Should().ContainKey("minio").WhoseValue.Should().Be("Healthy");
        summary.Status.Should().Be("Healthy");

        var anonymousHealth = await client.GetAsync("/health");
        anonymousHealth.StatusCode.Should().Be(HttpStatusCode.OK);
        var healthText = await anonymousHealth.Content.ReadAsStringAsync();
        healthText.Should().Be("Healthy");
    }

    [Fact]
    public async Task Attachment_upload_links_to_message_and_allows_download()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var content = "vibechat-attachment-integration"u8.ToArray();
        var initiate = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/attachments",
            new CreateAttachmentUploadRequest("notes.txt", "text/plain", content.Length));
        initiate.StatusCode.Should().Be(HttpStatusCode.OK);
        var upload = await initiate.Content.ReadFromJsonAsync<AttachmentUploadDto>(JsonOptions);
        upload.Should().NotBeNull();
        upload!.UploadUrl.Should().NotBeNullOrWhiteSpace();

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl)
        {
            Content = new ByteArrayContent(content)
        };
        using var putClient = new HttpClient();
        var put = await putClient.SendAsync(putRequest);
        put.IsSuccessStatusCode.Should().BeTrue($"presigned PUT failed: {(int)put.StatusCode}");

        var complete = await client.PostAsync(
            $"/api/v1/channels/{DemoChannelId}/attachments/{upload.AttachmentId}/complete",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        complete.StatusCode.Should().Be(HttpStatusCode.OK);

        var messageId = Guid.NewGuid();
        var send = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-att-{messageId:N}", "com anexo", null, null, [upload.AttachmentId]));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await send.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        accepted.Should().NotBeNull();
        accepted!.Attachments.Should().NotBeNull();
        accepted.Attachments!.Should().ContainSingle(a => a.Id == upload.AttachmentId && a.FileName == "notes.txt");

        var download = await client.GetFromJsonAsync<AttachmentDownloadDto>(
            $"/api/v1/channels/{DemoChannelId}/attachments/{upload.AttachmentId}/download",
            JsonOptions);
        download.Should().NotBeNull();
        download!.DownloadUrl.Should().NotBeNullOrWhiteSpace();

        var getObject = await putClient.GetAsync(download.DownloadUrl);
        getObject.IsSuccessStatusCode.Should().BeTrue();
        var bytes = await getObject.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(content);
    }

    [Fact]
    public async Task Attachment_rejects_disallowed_content_type()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var initiate = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/attachments",
            new CreateAttachmentUploadRequest("malware.exe", "application/x-msdownload", 128));
        initiate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_finds_message_by_fts_term()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var token = $"ftsunique{messageId:N}";
        var create = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-search-{messageId:N}", $"busca {token} no canal", null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var search = await client.GetFromJsonAsync<SearchMessagesDto>(
            $"/api/v1/search/messages?workspaceId={SeedData.DemoWorkspaceId.Value}&q={token}&limit=20",
            JsonOptions);

        search.Should().NotBeNull();
        search!.Items.Should().Contain(x => x.MessageId == messageId && x.ChannelId == DemoChannelId);
        search.Items.Single(x => x.MessageId == messageId).BodyPreview.Should().Contain(token);
    }

    [Fact]
    public async Task Search_excludes_soft_deleted_messages()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var token = $"ftsdeleted{messageId:N}";
        var create = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-search-del-{messageId:N}", $"apagada {token}", null, null));
        create.EnsureSuccessStatusCode();

        var delete = await client.DeleteAsync($"/api/v1/channels/{DemoChannelId}/messages/{messageId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var search = await client.GetFromJsonAsync<SearchMessagesDto>(
            $"/api/v1/search/messages?workspaceId={SeedData.DemoWorkspaceId.Value}&q={token}",
            JsonOptions);

        search.Should().NotBeNull();
        search!.Items.Should().NotContain(x => x.MessageId == messageId);
    }

    [Fact]
    public async Task Tenant_isolation_attempt_is_denied()
    {
        var foreignChannelId = await SeedForeignTenantChannelAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var unknown = await client.GetAsync($"/api/v1/channels/{Guid.NewGuid()}/messages");
        unknown.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreign = await client.GetAsync($"/api/v1/channels/{foreignChannelId}/messages");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var post = await client.PostAsJsonAsync(
            $"/api/v1/channels/{foreignChannelId}/messages",
            new SendMessageRequest(Guid.NewGuid(), $"idem-iso-{Guid.NewGuid():N}", "should-fail", null, null));
        post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> SeedForeignTenantChannelAsync()
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
            Name = $"Foreign Tenant {workspaceId.Value:N}"[..Math.Min(160, $"Foreign Tenant {workspaceId.Value:N}".Length)],
            Slug = $"foreign-{workspaceId.Value:N}"[..Math.Min(120, $"foreign-{workspaceId.Value:N}".Length)],
            AiEnabled = false,
            CreatedAt = now
        });
        db.Channels.Add(new Channel
        {
            Id = channelId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Name = "secret",
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

    private sealed record ReactionSummaryDto(string Emoji, int Count, bool Me);
    private sealed record ToggleReactionRequestDto(string Emoji);
    private sealed record ToggleReactionResponseDto(
        Guid MessageId,
        Guid ChannelId,
        string Emoji,
        bool Added,
        ReactionSummaryDto[] Reactions);

    private sealed record MessageDto(
        Guid Id,
        Guid ChannelId,
        long Sequence,
        Guid AuthorId,
        string Body,
        DateTimeOffset CreatedAt,
        DateTimeOffset? EditedAt,
        DateTimeOffset? DeletedAt,
        AttachmentDto[]? Attachments = null,
        Guid? ThreadId = null,
        Guid? ReplyToMessageId = null,
        int ReplyCount = 0,
        Guid? ConversationId = null,
        ReactionSummaryDto[]? Reactions = null);

    private sealed record ThreadDto(
        Guid Id,
        Guid ChannelId,
        Guid ParentMessageId,
        Guid CreatedBy,
        DateTimeOffset CreatedAt,
        int ReplyCount,
        MessageDto? ParentMessage = null);

    private sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Status);
    private sealed record AttachmentUploadDto(Guid AttachmentId, string UploadUrl, DateTimeOffset ExpiresAt, string FileName, string ContentType);
    private sealed record AttachmentDownloadDto(Guid AttachmentId, string DownloadUrl, DateTimeOffset ExpiresAt, string FileName, string ContentType, long SizeBytes);

    private sealed record ChannelDto(
        Guid Id,
        Guid WorkspaceId,
        string Name,
        string Type,
        Guid? PeerUserId,
        string? PeerDisplayName,
        Guid? SpaceId = null);

    private sealed record SpaceDto(Guid Id, Guid WorkspaceId, string Name, int Order);
    private sealed record CreateSpaceRequestDto(string Name);
    private sealed record CreateChannelRequestDto(string Name, string Type, Guid? SpaceId = null);
    private sealed record PresenceDto(Guid UserId, string Status);

    private sealed record WorkspaceMemberDto(Guid UserId, string DisplayName, string Email, string Role);

    private sealed record HealthSummaryDto(string Status, Dictionary<string, string> Checks);

    private sealed record SearchMessageHitDto(
        Guid MessageId,
        Guid ChannelId,
        string ChannelName,
        string ChannelType,
        long Sequence,
        Guid AuthorUserId,
        string AuthorDisplayName,
        string BodyPreview,
        DateTimeOffset CreatedAt,
        double Rank);

    private sealed record SearchMessagesDto(string Query, int Limit, SearchMessageHitDto[] Items);
}
