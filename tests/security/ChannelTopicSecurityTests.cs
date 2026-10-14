using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-118 — topic rows stay inside the parent channel ACL and the tenant.</summary>
[Collection(SecurityCollection.Name)]
public sealed class ChannelTopicSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Foreign_tenant_topic_is_not_readable()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var topicId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var workspaceId = WorkspaceId.New();
            var tenantId = new TenantId(workspaceId.Value);
            var channelId = ChannelId.New();
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                TenantId = tenantId,
                Name = $"Topic {workspaceId.Value:N}"[..24],
                Slug = $"t-{workspaceId.Value:N}"[..24],
                AiEnabled = false,
                CreatedAt = now
            });
            db.Channels.Add(new Channel
            {
                Id = channelId,
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                Name = "foreign-forum",
                Type = ChannelType.Private,
                Layout = ChannelLayout.Topics,
                CreatedAt = now,
                CreatedBy = SeedData.DemoUserId
            });
            db.ChannelTopics.Add(new ChannelTopic
            {
                Id = topicId,
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                ChannelId = channelId,
                ConversationId = new ChannelId(topicId),
                Title = "vazamento",
                Slug = "vazamento",
                State = TopicState.Open,
                Tags = "secreto",
                ParticipantIds = string.Empty,
                CreatedBy = SeedData.DemoUserId,
                CreatedAt = now,
                LastActivityAt = now
            });
            await db.SaveChangesAsync();
        }

        var response = await alice.GetAsync($"/api/v1/topics/{topicId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("vazamento");
        body.Should().NotContain("secreto");
    }

    [Fact]
    public async Task Member_cannot_close_without_manage_permission()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var create = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name = $"secforum-{Guid.NewGuid():N}"[..20], type = "Public", layout = "Topics" });
        create.EnsureSuccessStatusCode();
        var channel = await create.Content.ReadFromJsonAsync<ChannelDto>();
        var topic = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channel!.Id}/topics",
            new { title = "Permissao", idempotencyKey = $"perm-{Guid.NewGuid():N}" });
        topic.EnsureSuccessStatusCode();
        var created = await topic.Content.ReadFromJsonAsync<TopicDto>();
        var close = await alice.PostAsJsonAsync($"/api/v1/topics/{created!.Id}/close", new { idempotencyKey = "member-close-1" });
        close.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ChannelDto(Guid Id);
    private sealed record TopicDto(Guid Id);
}
