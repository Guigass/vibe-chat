using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Audit;
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

        await using var db = factory.CreateMigratorDbContext();
        var persisted = await db.Messages.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == new MessageId(messageId));
        persisted.Body.Should().Be(body);

        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == nameof(MessageCreatedEvent))
            .OrderByDescending(x => x.OccurredAt)
            .Take(20)
            .ToListAsync();
        outbox.Should().Contain(x =>
            x.Payload.Contains(messageId.ToString(), StringComparison.OrdinalIgnoreCase)
            && x.Payload.Contains("clientMessageId", StringComparison.Ordinal));
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

        await using var db = factory.CreateMigratorDbContext();
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
            new ToggleReactionRequestDto(":)"));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rocket = await client.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{messageId}/reactions",
            new ToggleReactionRequestDto("🚀"));
        rocket.StatusCode.Should().Be(HttpStatusCode.OK);
        var rocketAdded = await rocket.Content.ReadFromJsonAsync<ToggleReactionResponseDto>(JsonOptions);
        rocketAdded!.Added.Should().BeTrue();
        rocketAdded.Reactions.Should().ContainSingle(r => r.Emoji == "🚀" && r.Count == 1 && r.Me);

        var users = await client.GetFromJsonAsync<ReactionUsersResponseDto>(
            $"/api/v1/channels/{DemoChannelId}/messages/{messageId}/reactions/{Uri.EscapeDataString("🚀")}/users",
            JsonOptions);
        users.Should().NotBeNull();
        users!.Emoji.Should().Be("🚀");
        users.Users.Should().ContainSingle(u => u.DisplayName == "Alice");

        await using var db = factory.CreateMigratorDbContext();
        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == nameof(ReactionChangedEvent))
            .OrderByDescending(x => x.OccurredAt)
            .Take(20)
            .ToListAsync();
        outbox.Should().Contain(x =>
            x.Payload.Contains(messageId.ToString(), StringComparison.OrdinalIgnoreCase)
            && x.Payload.Contains("\"added\":true", StringComparison.Ordinal)
            && (x.Payload.Contains("🚀", StringComparison.Ordinal)
                || x.Payload.Contains("\\uD83D\\uDE80", StringComparison.Ordinal)));
        (await db.Reactions.IgnoreQueryFilters().CountAsync(x => x.MessageId == new MessageId(messageId)))
            .Should().Be(1);
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

        await using var db = factory.CreateMigratorDbContext();
        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == nameof(MessageEditedEvent))
            .OrderByDescending(x => x.OccurredAt)
            .Take(20)
            .ToListAsync();
        outbox.Should().Contain(x => x.Payload.Contains(messageId.ToString(), StringComparison.OrdinalIgnoreCase)
            && x.Payload.Contains(editedBody, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Send_message_accepts_body_at_max_length()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var body = new string('a', MessageBodyPolicies.MaxLength);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-max-{messageId:N}", body, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Send_message_rejects_body_over_max_length_without_persisting()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var body = new string('a', MessageBodyPolicies.MaxLength + 1);

        await using var dbBefore = factory.CreateMigratorDbContext();
        var outboxBefore = await dbBefore.OutboxMessages.IgnoreQueryFilters().CountAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-long-{messageId:N}", body, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("error").GetString().Should().Be("MessageBodyTooLong");
        error.GetProperty("maxLength").GetInt32().Should().Be(MessageBodyPolicies.MaxLength);

        await using var db = factory.CreateMigratorDbContext();
        (await db.Messages.IgnoreQueryFilters().AnyAsync(x => x.Id == new MessageId(messageId))).Should().BeFalse();
        var outboxAfter = await db.OutboxMessages.IgnoreQueryFilters().CountAsync();
        outboxAfter.Should().Be(outboxBefore);
    }

    [Fact]
    public async Task Edit_message_rejects_body_over_max_length()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var create = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-edit-long-{messageId:N}", $"edit-me-{messageId:N}", null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var edit = await client.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{messageId}",
            new EditMessageRequest(new string('b', MessageBodyPolicies.MaxLength + 1)));
        edit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await edit.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("error").GetString().Should().Be("MessageBodyTooLong");
    }

    [Fact]
    public async Task Thread_reply_rejects_body_over_max_length()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var parentId = Guid.NewGuid();
        var parentCreate = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(parentId, $"idem-thread-max-parent-{parentId:N}", $"parent-{parentId:N}", null, null));
        parentCreate.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var threadCreate = await client.PostAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{parentId}/threads",
            null);
        threadCreate.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread = await threadCreate.Content.ReadFromJsonAsync<ThreadDto>(JsonOptions);
        thread.Should().NotBeNull();

        var replyId = Guid.NewGuid();
        var reply = await client.PostAsJsonAsync(
            $"/api/v1/threads/{thread!.Id}/messages",
            new SendMessageRequest(replyId, $"idem-thread-long-{replyId:N}", new string('c', MessageBodyPolicies.MaxLength + 1), parentId, thread.Id));
        reply.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await reply.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("error").GetString().Should().Be("MessageBodyTooLong");
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

        await using var db = factory.CreateMigratorDbContext();
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
    public async Task Owner_can_change_member_role_and_reject_guest()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var workspaceId = SeedData.DemoWorkspaceId.Value;
        var roles = await demo.GetFromJsonAsync<WorkspaceRolesDto>(
            $"/api/v1/workspaces/{workspaceId}/roles",
            JsonOptions);
        roles.Should().NotBeNull();
        roles!.AssignableRoles.Should().Contain(["Member", "Moderator", "Auditor", "Admin"]);
        roles.AssignableRoles.Should().NotContain("Guest");

        var promote = await demo.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/members/{SeedData.BobUserId.Value}/role",
            new UpdateMemberRoleRequestDto("Moderator"));
        promote.StatusCode.Should().Be(HttpStatusCode.OK);
        var promoted = await promote.Content.ReadFromJsonAsync<WorkspaceMemberDto>(JsonOptions);
        promoted.Should().NotBeNull();
        promoted!.Role.Should().Be("Moderator");

        var guest = await demo.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/members/{SeedData.BobUserId.Value}/role",
            new UpdateMemberRoleRequestDto("Guest"));
        guest.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = factory.CreateMigratorDbContext();
        var membership = await db.WorkspaceMembers.IgnoreQueryFilters()
            .SingleAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId && x.UserId == SeedData.BobUserId);
        membership.Role.Should().Be(Role.Moderator);

        var audit = await db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.TenantId == SeedData.DemoTenantId && x.Action == AuditActions.MemberRoleChange)
            .OrderByDescending(x => x.OccurredAt)
            .FirstOrDefaultAsync();
        audit.Should().NotBeNull();

        // Restore Bob to Member so other tests keep the seed assumption.
        var restore = await demo.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/members/{SeedData.BobUserId.Value}/role",
            new UpdateMemberRoleRequestDto("Member"));
        restore.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_conversation_audit_shows_soft_deleted_body()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var messageId = Guid.NewGuid();
        var body = $"admin-audit-body-{messageId:N}";
        var create = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var delete = await alice.DeleteAsync($"/api/v1/channels/{DemoChannelId}/messages/{messageId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var memberHistory = await alice.GetAsync($"/api/v1/channels/{DemoChannelId}/messages?after=0&limit=100");
        memberHistory.EnsureSuccessStatusCode();
        var memberRows = await memberHistory.Content.ReadFromJsonAsync<MessageDto[]>(JsonOptions);
        var memberRow = memberRows!.Single(x => x.Id == messageId);
        memberRow.Body.Should().BeEmpty();
        memberRow.DeletedAt.Should().NotBeNull();

        var list = await demo.GetAsync(
            $"/api/v1/admin/conversations?workspaceId={SeedData.DemoWorkspaceId.Value}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var conversations = await list.Content.ReadFromJsonAsync<AdminConversationsDto>(JsonOptions);
        conversations.Should().NotBeNull();
        conversations!.Items.Should().Contain(x => x.Id == DemoChannelId);

        var adminHistory = await demo.GetAsync(
            $"/api/v1/admin/conversations/{DemoChannelId}/messages?limit=100");
        adminHistory.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminRows = await adminHistory.Content.ReadFromJsonAsync<AdminConversationMessagesDto>(JsonOptions);
        adminRows.Should().NotBeNull();
        var adminRow = adminRows!.Items.Single(x => x.Id == messageId);
        adminRow.Body.Should().Be(body);
        adminRow.DeletedAt.Should().NotBeNull();
        adminRow.DeletedBy.Should().Be(SeedData.AliceUserId.Value);
        adminRow.DeletedByName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Admin_can_export_workspace_with_soft_deleted_body_and_audit()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var messageId = Guid.NewGuid();
        var body = $"export-body-{messageId:N}";
        var create = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var delete = await alice.DeleteAsync($"/api/v1/channels/{DemoChannelId}/messages/{messageId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var export = await demo.GetAsync(
            $"/api/v1/admin/workspaces/{SeedData.DemoWorkspaceId.Value}/export");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        export.Content.Headers.ContentDisposition.Should().NotBeNull();
        export.Content.Headers.ContentDisposition!.FileName.Should().Contain("vibechat-export");

        var zipBytes = await export.Content.ReadAsByteArrayAsync();
        zipBytes.Length.Should().BeGreaterThan(32);

        await using (var zipStream = new MemoryStream(zipBytes))
        using (var zip = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read))
        {
            zip.Entries.Select(e => e.Name).Should().Contain([
                "manifest.json",
                "workspace.json",
                "members.json",
                "spaces.json",
                "channels.json",
                "threads.json",
                "messages.json",
                "attachments.json"
            ]);

            await using var messagesStream = zip.GetEntry("messages.json")!.Open();
            using var doc = await JsonDocument.ParseAsync(messagesStream);
            var messages = doc.RootElement;
            messages.ValueKind.Should().Be(JsonValueKind.Array);
            var exported = messages.EnumerateArray()
                .Single(m => m.GetProperty("id").GetGuid() == messageId);
            exported.GetProperty("body").GetString().Should().Be(body);
            exported.GetProperty("deletedAt").ValueKind.Should().Be(JsonValueKind.String);

            await using var manifestStream = zip.GetEntry("manifest.json")!.Open();
            using var manifestDoc = await JsonDocument.ParseAsync(manifestStream);
            manifestDoc.RootElement.GetProperty("format").GetString()
                .Should().Be("vibechat.workspace.export.v1");
            manifestDoc.RootElement.GetProperty("workspaceId").GetGuid()
                .Should().Be(SeedData.DemoWorkspaceId.Value);
        }

        await using (var db = factory.CreateMigratorDbContext())
        {
            var audit = await db.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.TenantId == SeedData.DemoTenantId && x.Action == AuditActions.WorkspaceExport)
                .OrderByDescending(x => x.OccurredAt)
                .FirstOrDefaultAsync();
            audit.Should().NotBeNull();
            audit!.EntityId.Should().Be(SeedData.DemoWorkspaceId.Value.ToString());
            audit.ActorUserId.Should().Be(SeedData.DemoUserId);
            audit.MetadataJson.Should().Contain(SeedData.DemoWorkspaceId.Value.ToString());
        }
    }

    [Fact]
    public async Task Admin_can_read_masked_settings_and_update_flag_with_audit()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var workspaceId = SeedData.DemoWorkspaceId.Value;
        var get = await demo.GetAsync($"/api/v1/admin/settings?workspaceId={workspaceId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await get.Content.ReadFromJsonAsync<SensitiveSettingsDto>(JsonOptions);
        settings.Should().NotBeNull();
        settings!.WorkspaceId.Should().Be(workspaceId);
        settings.Ai.ApiKeyConfigured.Should().BeTrue();
        settings.Ai.ApiKeyMask.Should().Be("••••ey99");
        settings.Ai.ApiKeyMask.Should().NotContain("sk-test");
        settings.Email.SmtpPasswordConfigured.Should().BeTrue();
        settings.Email.SmtpPasswordMask.Should().Be("••••rd42");
        settings.Email.Enabled.Should().BeFalse();
        settings.Webhooks.Status.Should().Be("unconfigured");
        settings.Webhooks.SecretsWritable.Should().BeTrue();
        settings.Webhooks.SecretConfigured.Should().BeFalse();
        settings.Retention.ProcessEnabled.Should().BeTrue();
        settings.Retention.Enabled.Should().BeFalse();
        settings.Retention.DefaultRetentionDays.Should().Be(90);
        settings.Retention.RetentionDays.Should().Be(90);

        var originalEnabled = settings.Ai.WorkspaceEnabled;
        var put = await demo.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new
            {
                workspaceId,
                ai = new { workspaceEnabled = !originalEnabled, provider = "Mock" }
            });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await put.Content.ReadFromJsonAsync<SensitiveSettingsDto>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Ai.WorkspaceEnabled.Should().Be(!originalEnabled);
        updated.Ai.ApiKeyMask.Should().Be("••••ey99");

        var secretPut = await demo.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new { workspaceId, ai = new { apiKey = "should-never-store" } });
        secretPut.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var audit = await db.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.TenantId == SeedData.DemoTenantId && x.Action == AuditActions.SettingsChange)
                .OrderByDescending(x => x.OccurredAt)
                .FirstOrDefaultAsync();
            audit.Should().NotBeNull();
            audit!.MetadataJson.Should().Contain("ai.workspaceEnabled");
            audit.MetadataJson.Should().NotContain("should-never-store");
            audit.MetadataJson.Should().NotContain("sk-test-secret-key99");
        }

        // Restore seed assumption for other tests.
        var restore = await demo.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new { workspaceId, ai = new { workspaceEnabled = originalEnabled, provider = "Mock" } });
        restore.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_can_configure_webhook_with_masked_secret()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var workspaceId = SeedData.DemoWorkspaceId.Value;
        const string secret = "webhook-signing-secret-99";

        var put = await demo.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new
            {
                workspaceId,
                webhooks = new
                {
                    enabled = true,
                    url = "https://hooks.example.test/vibechat",
                    secret
                }
            });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await put.Content.ReadFromJsonAsync<SensitiveSettingsDto>(JsonOptions);
        settings.Should().NotBeNull();
        settings!.Webhooks.Status.Should().Be("active");
        settings.Webhooks.Enabled.Should().BeTrue();
        settings.Webhooks.Url.Should().Be("https://hooks.example.test/vibechat");
        settings.Webhooks.SecretConfigured.Should().BeTrue();
        settings.Webhooks.SecretMask.Should().Be("••••t-99");
        settings.Webhooks.SecretMask.Should().NotContain(secret);
        (await put.Content.ReadAsStringAsync()).Should().NotContain(secret);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.OutboundWebhookEndpoints.IgnoreQueryFilters()
                .SingleAsync(x => x.TenantId == SeedData.DemoTenantId);
            row.Enabled.Should().BeTrue();
            row.Secret.Should().Be(secret);

            var audit = await db.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.TenantId == SeedData.DemoTenantId && x.Action == AuditActions.SettingsChange)
                .OrderByDescending(x => x.OccurredAt)
                .FirstOrDefaultAsync();
            audit.Should().NotBeNull();
            audit!.MetadataJson.Should().Contain("webhooks.enabled");
            audit.MetadataJson.Should().NotContain(secret);
        }

        // Disable without rotating secret.
        var disable = await demo.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new { workspaceId, webhooks = new { enabled = false } });
        disable.StatusCode.Should().Be(HttpStatusCode.OK);
        var disabled = await disable.Content.ReadFromJsonAsync<SensitiveSettingsDto>(JsonOptions);
        disabled!.Webhooks.Status.Should().Be("disabled");
        disabled.Webhooks.SecretConfigured.Should().BeTrue();
        disabled.Webhooks.SecretMask.Should().Be("••••t-99");

        // Restore seed assumption (unconfigured) for sibling tests sharing the factory DB.
        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.OutboundWebhookEndpoints.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.TenantId == SeedData.DemoTenantId);
            if (row is not null)
            {
                db.OutboundWebhookEndpoints.Remove(row);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Admin_can_configure_message_retention_and_purge_soft_deletes()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var workspaceId = SeedData.DemoWorkspaceId.Value;
        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-purge-{messageId:N}", $"purge-me-{messageId:N}", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var del = await alice.DeleteAsync($"/api/v1/channels/{DemoChannelId}/messages/{messageId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var keepId = Guid.NewGuid();
        var keepSend = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(keepId, $"idem-keep-{keepId:N}", $"keep-{keepId:N}", null, null));
        keepSend.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var keepDel = await alice.DeleteAsync($"/api/v1/channels/{DemoChannelId}/messages/{keepId}");
        keepDel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var put = await demo.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new { workspaceId, retention = new { enabled = true, retentionDays = 30 } });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await put.Content.ReadFromJsonAsync<SensitiveSettingsDto>(JsonOptions);
        settings.Should().NotBeNull();
        settings!.Retention.Enabled.Should().BeTrue();
        settings.Retention.RetentionDays.Should().Be(30);
        settings.Retention.ProcessEnabled.Should().BeTrue();

        var invalid = await demo.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new { workspaceId, retention = new { retentionDays = 0 } });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var old = await db.Messages.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == new MessageId(messageId));
            old.DeletedAt = DateTimeOffset.UtcNow.AddDays(-40);
            var recent = await db.Messages.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == new MessageId(keepId));
            recent.DeletedAt = DateTimeOffset.UtcNow.AddDays(-5);
            await db.SaveChangesAsync();
        }

        var processor = factory.Services.GetRequiredService<MessageRetentionPurgeProcessor>();
        var purged = await processor.ProcessBatchAsync(CancellationToken.None);
        purged.Should().BeGreaterThanOrEqualTo(1);

        await using (var db = factory.CreateMigratorDbContext())
        {
            (await db.Messages.IgnoreQueryFilters()
                .AnyAsync(x => x.Id == new MessageId(messageId))).Should().BeFalse();
            (await db.Messages.IgnoreQueryFilters()
                .AnyAsync(x => x.Id == new MessageId(keepId))).Should().BeTrue();

            var audit = await db.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.TenantId == SeedData.DemoTenantId && x.Action == AuditActions.MessagePurge)
                .OrderByDescending(x => x.OccurredAt)
                .FirstOrDefaultAsync();
            audit.Should().NotBeNull();
            audit!.MetadataJson.Should().Contain("retentionDays");

            var policy = await db.MessageRetentionSettings.IgnoreQueryFilters()
                .SingleAsync(x => x.TenantId == SeedData.DemoTenantId);
            policy.Enabled = false;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Owner_can_invite_member_and_claim_pending_on_login()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var workspaceId = SeedData.DemoWorkspaceId.Value;
        var email = $"carol-{Guid.NewGuid():N}@vibechat.local";

        var invite = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/members",
            new InviteMemberRequestDto(email, "Carol", "Moderator"));
        invite.StatusCode.Should().Be(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<WorkspaceMemberDto>(JsonOptions);
        invited.Should().NotBeNull();
        invited!.Email.Should().Be(email);
        invited.Role.Should().Be("Moderator");
        invited.DisplayName.Should().Be("Carol");

        var duplicate = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/members",
            new InviteMemberRequestDto(email, "Carol", "Member"));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var guestInvite = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/members",
            new InviteMemberRequestDto($"guest-{Guid.NewGuid():N}@vibechat.local", "Guesty", "Guest"));
        guestInvite.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var profile = await db.UserProfiles.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == new UserId(invited.UserId));
            profile.Subject.Should().Be($"pending:{email}");

            var audit = await db.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.TenantId == SeedData.DemoTenantId && x.Action == AuditActions.MemberInvite)
                .OrderByDescending(x => x.OccurredAt)
                .FirstOrDefaultAsync();
            audit.Should().NotBeNull();
        }

        using var carol = factory.CreateClient();
        carol.DefaultRequestHeaders.Add("X-Dev-User", $"carol-{Guid.NewGuid():N}");
        carol.DefaultRequestHeaders.Add("X-Dev-Email", email);
        carol.DefaultRequestHeaders.Add("X-Dev-Name", "Carol Claimed");

        var me = await carol.GetFromJsonAsync<MeDto>("/api/v1/me", JsonOptions);
        me.Should().NotBeNull();
        me!.UserId.Should().Be(invited.UserId);
        me.Email.Should().Be(email);

        var workspaces = await carol.GetFromJsonAsync<WorkspaceDto[]>("/api/v1/workspaces", JsonOptions);
        workspaces.Should().Contain(w => w.Id == workspaceId && w.Role == "Moderator");

        await using (var db = factory.CreateMigratorDbContext())
        {
            var profile = await db.UserProfiles.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == new UserId(invited.UserId));
            profile.Subject.Should().StartWith("dev:");
            profile.Subject.Should().NotStartWith("pending:");
        }
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
    public async Task Audio_attachment_upload_links_to_message_with_metadata()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var content = new byte[] { 0x1a, 0x45, 0xdf, 0xa3, 0x01, 0x00, 0x00, 0x00 };
        var waveform = Enumerable.Range(0, 20).Select(i => i * 5).ToArray();
        var initiate = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/attachments",
            new CreateAttachmentUploadRequest(
                "voice.webm",
                "audio/webm",
                content.Length,
                Kind: "Audio",
                DurationMs: 10_000,
                Waveform: waveform));
        initiate.StatusCode.Should().Be(HttpStatusCode.OK);
        var upload = await initiate.Content.ReadFromJsonAsync<AttachmentUploadDto>(JsonOptions);
        upload.Should().NotBeNull();

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, upload!.UploadUrl)
        {
            Content = new ByteArrayContent(content)
        };
        using var putClient = new HttpClient();
        (await putClient.SendAsync(putRequest)).IsSuccessStatusCode.Should().BeTrue();

        var complete = await client.PostAsync(
            $"/api/v1/channels/{DemoChannelId}/attachments/{upload.AttachmentId}/complete",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        complete.StatusCode.Should().Be(HttpStatusCode.OK);

        var messageId = Guid.NewGuid();
        var send = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-audio-{messageId:N}", string.Empty, null, null, [upload.AttachmentId]));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await send.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        accepted.Should().NotBeNull();
        accepted!.Attachments.Should().ContainSingle(a =>
            a.Id == upload.AttachmentId
            && a.Kind == "Audio"
            && a.DurationMs == 10_000
            && a.Waveform != null
            && a.Waveform.Length > 0);
    }

    [Fact]
    public async Task Ai_transcribe_audio_attachment_outside_send_path()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var content = new byte[] { 0x1a, 0x45, 0xdf, 0xa3, 0x02, 0x00, 0x00, 0x00 };
        var initiate = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/attachments",
            new CreateAttachmentUploadRequest("voice.webm", "audio/webm", content.Length, Kind: "Audio", DurationMs: 5_000));
        initiate.EnsureSuccessStatusCode();
        var upload = await initiate.Content.ReadFromJsonAsync<AttachmentUploadDto>(JsonOptions);

        using var putClient = new HttpClient();
        (await putClient.SendAsync(new HttpRequestMessage(HttpMethod.Put, upload!.UploadUrl)
        {
            Content = new ByteArrayContent(content)
        })).EnsureSuccessStatusCode();

        await client.PostAsync(
            $"/api/v1/channels/{DemoChannelId}/attachments/{upload.AttachmentId}/complete",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        var messageId = Guid.NewGuid();
        (await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-tr-{messageId:N}", string.Empty, null, null, [upload.AttachmentId])))
            .EnsureSuccessStatusCode();

        var transcribe = await client.PostAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/{DemoChannelId}/messages/{messageId}/attachments/{upload.AttachmentId}/transcribe",
            content: null);
        transcribe.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await transcribe.Content.ReadFromJsonAsync<AiTranscribeDto>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Text.Should().NotBeNullOrWhiteSpace();
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
    public async Task SendMessage_persists_multiple_attachments()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await UploadReadyAttachmentAsync(
                client,
                $"file-{i}.txt",
                System.Text.Encoding.UTF8.GetBytes($"content-{i}")));
        }

        var messageId = Guid.NewGuid();
        var send = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-multi-att-{messageId:N}", "três anexos", null, null, ids.ToArray()));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await send.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        accepted.Should().NotBeNull();
        accepted!.Attachments.Should().NotBeNull();
        accepted.Attachments!.Select(x => x.Id).Should().BeEquivalentTo(ids);
    }

    [Fact]
    public async Task SendMessage_rejects_too_many_attachments()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var ids = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToArray();
        var messageId = Guid.NewGuid();
        var send = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-too-many-{messageId:N}", "overflow", null, null, ids));
        send.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await send.Content.ReadAsStringAsync();
        body.Should().Contain("TooManyAttachments");
    }

    private static async Task<Guid> UploadReadyAttachmentAsync(HttpClient client, string fileName, byte[] content)
    {
        var initiate = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/attachments",
            new CreateAttachmentUploadRequest(fileName, "text/plain", content.Length));
        initiate.EnsureSuccessStatusCode();
        var upload = await initiate.Content.ReadFromJsonAsync<AttachmentUploadDto>(JsonOptions);
        upload.Should().NotBeNull();

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, upload!.UploadUrl)
        {
            Content = new ByteArrayContent(content)
        };
        using var putClient = new HttpClient();
        (await putClient.SendAsync(putRequest)).EnsureSuccessStatusCode();

        var complete = await client.PostAsync(
            $"/api/v1/channels/{DemoChannelId}/attachments/{upload.AttachmentId}/complete",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        complete.EnsureSuccessStatusCode();
        return upload.AttachmentId;
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
    public async Task Search_finds_markdown_wrapped_term()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var token = $"mdterm{messageId:N}";
        var create = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-md-{messageId:N}", $"**{token}** destacado", null, null));
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
    public async Task Ai_summarize_uses_mock_provider_outside_send_path()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        (await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-ai-{messageId:N}", $"ai-summary-{messageId:N}", null, null)))
            .EnsureSuccessStatusCode();

        var summarize = await client.PostAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/{DemoChannelId}/ai/summarize",
            content: null);
        summarize.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await summarize.Content.ReadFromJsonAsync<AiSummaryDto>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Summary.Should().StartWith("Mock summary:");
    }

    [Fact]
    public async Task Ai_suggest_reply_uses_mock_provider_outside_send_path()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        (await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-ai-sug-{messageId:N}", $"ai-suggest-{messageId:N}", null, null)))
            .EnsureSuccessStatusCode();

        var suggest = await client.PostAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/{DemoChannelId}/ai/suggest-reply",
            content: null);
        suggest.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await suggest.Content.ReadFromJsonAsync<AiSuggestReplyDto>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Suggestion.Should().StartWith("Mock suggestion:");
    }

    [Fact]
    public async Task Send_message_with_two_mentions_persists_two_rows_in_same_transaction()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var body = $"mention-{messageId:N} {MentionTokens.UserBodyToken(SeedData.BobUserId)} {MentionTokens.UserBodyToken(SeedData.DemoUserId)}";
        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-mentions-{messageId:N}", body, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.MessageMentions.IgnoreQueryFilters()
            .Where(x => x.MessageId == new MessageId(messageId))
            .ToListAsync();
        rows.Should().HaveCountGreaterThanOrEqualTo(2);
        rows.Should().Contain(x => x.MentionedUserId == SeedData.BobUserId);
        rows.Should().Contain(x => x.MentionedUserId == SeedData.DemoUserId);

        var outbox = (await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == nameof(MessageCreatedEvent))
            .OrderByDescending(x => x.OccurredAt)
            .Take(20)
            .ToListAsync())
            .First(x => x.Payload.Contains(messageId.ToString(), StringComparison.OrdinalIgnoreCase));
        outbox.Payload.Should().Contain("mentionedUserIds");
        outbox.Payload.Should().Contain(SeedData.BobUserId.Value.ToString());
    }

    [Fact]
    public async Task Channel_members_autocomplete_is_scoped_to_channel()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var members = await client.GetFromJsonAsync<ChannelMemberDto[]>(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/{DemoChannelId}/members?query=bo",
            JsonOptions);
        members.Should().NotBeNull();
        members!.Select(x => x.UserId).Should().Contain(SeedData.BobUserId.Value);
        members.Select(x => x.UserId).Should().NotContain(SeedData.AliceUserId.Value);
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
        await using var db = factory.CreateMigratorDbContext();
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

    private sealed record AiSummaryDto(string Summary);
    private sealed record AiTranscribeDto(string Text, string Language, string Provider);
    private sealed record AiSuggestReplyDto(string Suggestion);
    private sealed record ReactionSummaryDto(string Emoji, int Count, bool Me);
    private sealed record ReactionUserDto(Guid UserId, string DisplayName);
    private sealed record ReactionUsersResponseDto(string Emoji, ReactionUserDto[] Users, int Total);
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

    private sealed record AttachmentDto(
        Guid Id,
        string FileName,
        string ContentType,
        long SizeBytes,
        string Status,
        string Kind = "File",
        int? DurationMs = null,
        int[]? Waveform = null);
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
    private sealed record WorkspaceRolesDto(string[] AssignableRoles);
    private sealed record UpdateMemberRoleRequestDto(string Role);
    private sealed record InviteMemberRequestDto(string Email, string? DisplayName = null, string? Role = null);
    private sealed record MeDto(Guid UserId, string Subject, string Email, string DisplayName, string[] Roles);
    private sealed record WorkspaceDto(Guid Id, string Name, string Slug, string Role);
    private sealed record SensitiveSettingsDto(
        Guid WorkspaceId,
        AiSensitiveSettingsDto Ai,
        EmailSensitiveSettingsDto Email,
        WebhooksSensitiveSettingsDto Webhooks,
        RetentionSensitiveSettingsDto Retention);
    private sealed record AiSensitiveSettingsDto(
        bool ProcessEnabled,
        string ProcessSource,
        bool WorkspaceEnabled,
        string Provider,
        bool ApiKeyConfigured,
        string? ApiKeyMask,
        bool SecretsWritable);
    private sealed record EmailSensitiveSettingsDto(
        bool Enabled,
        string Source,
        string SmtpHost,
        int SmtpPort,
        string SmtpUsername,
        bool SmtpUsernameConfigured,
        bool SmtpPasswordConfigured,
        string? SmtpPasswordMask,
        string SmtpFrom,
        bool UseStartTls,
        bool SecretsWritable);
    private sealed record WebhooksSensitiveSettingsDto(
        string Status,
        bool Enabled,
        string Url,
        bool UrlConfigured,
        bool SecretConfigured,
        string? SecretMask,
        bool SecretsWritable,
        string Message);
    private sealed record RetentionSensitiveSettingsDto(
        bool ProcessEnabled,
        string ProcessSource,
        bool Enabled,
        int RetentionDays,
        int DefaultRetentionDays,
        string Message);
    private sealed record AdminConversationItemDto(Guid Id, Guid WorkspaceId, string Name, string Type);
    private sealed record AdminConversationsDto(AdminConversationItemDto[] Items);
    private sealed record AdminConversationMessageItemDto(
        Guid Id,
        Guid ChannelId,
        Guid ConversationId,
        long Sequence,
        Guid AuthorId,
        string AuthorName,
        string Body,
        DateTimeOffset CreatedAt,
        DateTimeOffset? EditedAt,
        DateTimeOffset? DeletedAt,
        Guid? DeletedBy,
        string? DeletedByName);
    private sealed record AdminConversationMessagesDto(AdminConversationMessageItemDto[] Items);

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

    private sealed record ChannelMemberDto(Guid UserId, string DisplayName, string Email);
}
