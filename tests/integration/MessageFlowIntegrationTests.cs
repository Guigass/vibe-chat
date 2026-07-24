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

    private sealed record MessageDto(
        Guid Id,
        Guid ChannelId,
        long Sequence,
        Guid AuthorId,
        string Body,
        DateTimeOffset CreatedAt,
        DateTimeOffset? EditedAt,
        DateTimeOffset? DeletedAt);

    private sealed record ChannelDto(
        Guid Id,
        Guid WorkspaceId,
        string Name,
        string Type,
        Guid? PeerUserId,
        string? PeerDisplayName);

    private sealed record WorkspaceMemberDto(Guid UserId, string DisplayName, string Email, string Role);

    private sealed record HealthSummaryDto(string Status, Dictionary<string, string> Checks);
}
