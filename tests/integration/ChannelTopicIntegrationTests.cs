using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-118 — forum topics reuse conversation seq, follow/unread and closed posts.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChannelTopicIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Two_topics_have_independent_sequences_and_follow_reaches_inbox()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var channelId = await CreateForumAsync(alice, "Public");

        var first = await CreateTopicAsync(alice, channelId, "Planejamento");
        var second = await CreateTopicAsync(alice, channelId, "Planejamento");
        second.Slug.Should().NotBe(first.Slug);

        var firstSend = await SendAsync(alice, first.Id, "um");
        var secondSend = await SendAsync(bob, second.Id, "dois");
        firstSend.Sequence.Should().Be(1);
        secondSend.Sequence.Should().Be(1);

        await FollowAsync(bob, first.Id);
        (await SendAsync(alice, first.Id, "tres")).Sequence.Should().Be(2);

        var inbox = await bob.GetFromJsonAsync<InboxPageDto>(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/inbox",
            JsonOptions);
        inbox!.Items.Should().Contain(item => item.Kind == "topic" && item.TopicId == first.Id && item.Sequence == 2);

        var persisted = await factory.CreateMigratorDbContext().ConversationSequences
            .IgnoreQueryFilters()
            .Where(x => x.ConversationId == new VibeChat.SharedKernel.ChannelId(first.Id)
                || x.ConversationId == new VibeChat.SharedKernel.ChannelId(second.Id))
            .ToListAsync();
        persisted.Should().HaveCount(2);
        persisted.Select(x => x.LastSequence).Should().BeEquivalentTo([2L, 1L]);

        var outbox = await factory.CreateMigratorDbContext().OutboxMessages
            .IgnoreQueryFilters()
            .Where(x => x.Type == "topic.created" && x.Payload.Contains(first.Id.ToString()))
            .CountAsync();
        outbox.Should().Be(1);
    }

    [Fact]
    public async Task Closed_topic_blocks_member_post_and_stream_channel_is_unchanged()
    {
        using var alice = Client("alice");
        using var demo = Client("demo");
        var channelId = await CreateForumAsync(alice, "Public");
        var topic = await CreateTopicAsync(alice, channelId, "Fechado");
        await SendAsync(alice, topic.Id, "antes");

        var close = await demo.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/close", new { idempotencyKey = "close-topic-1" });
        close.StatusCode.Should().Be(HttpStatusCode.OK);

        var denied = await alice.PostAsJsonAsync(
            $"/api/v1/topics/{topic.Id}/messages",
            new { messageId = Guid.NewGuid(), idempotencyKey = "closed-post-1", body = "nao" });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().Contain("TopicClosed");

        var root = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages",
            new { messageId = Guid.NewGuid(), idempotencyKey = "root-post-1", body = "raiz" });
        root.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await root.Content.ReadAsStringAsync()).Should().Contain("TopicRequired");

        var geral = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId = Guid.NewGuid(), idempotencyKey = $"geral-{Guid.NewGuid():N}", body = "canal normal" });
        geral.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Disabled_flag_hides_topics_without_writing()
    {
        using var disabledFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Conversations:Topics:Enabled", "false"));
        using var alice = disabledFactory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var response = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name = $"off-{Guid.NewGuid():N}"[..20], type = "Public", layout = "Topics" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("TopicsDisabled");
    }

    [Fact]
    public async Task Search_respects_the_parent_channel_acl()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var channelId = await CreateForumAsync(alice, "Private");
        var topic = await CreateTopicAsync(alice, channelId, "Segredo");
        var token = $"topicsecret{Guid.NewGuid():N}";
        await SendAsync(alice, topic.Id, token);

        var aliceHit = await alice.GetFromJsonAsync<SearchPageDto>(
            $"/api/v1/search/messages?workspaceId={SeedData.DemoWorkspaceId.Value}&q={token}&channelId={channelId}",
            JsonOptions);
        aliceHit!.Items.Should().Contain(item => item.BodyPreview.Contains(token));

        var bobHit = await bob.GetFromJsonAsync<SearchPageDto>(
            $"/api/v1/search/messages?workspaceId={SeedData.DemoWorkspaceId.Value}&q={token}",
            JsonOptions);
        bobHit!.Items.Should().BeEmpty();

        var list = await bob.GetAsync($"/api/v1/channels/{channelId}/topics");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Move_follows_the_destination_channel()
    {
        using var alice = Client("alice");
        using var demo = Client("demo");
        using var bob = Client("bob");
        var source = await CreateForumAsync(alice, "Public");
        var destination = await CreateForumAsync(alice, "Private");
        var topic = await CreateTopicAsync(alice, source, "Mover");
        var token = $"moved{Guid.NewGuid():N}";
        await SendAsync(alice, topic.Id, token);
        var grant = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/{destination}/members",
            new { userId = SeedData.DemoUserId.Value });
        grant.StatusCode.Should().Be(HttpStatusCode.OK);

        var move = await demo.PostAsJsonAsync(
            $"/api/v1/topics/{topic.Id}/move",
            new { channelId = destination, idempotencyKey = "move-topic-01" });
        var moveBody = await move.Content.ReadAsStringAsync();
        move.StatusCode.Should().Be(HttpStatusCode.OK, moveBody);

        var after = await alice.GetAsync($"/api/v1/topics/{topic.Id}");
        after.StatusCode.Should().Be(HttpStatusCode.OK, await after.Content.ReadAsStringAsync());
        var history = await alice.GetAsync($"/api/v1/topics/{topic.Id}/messages");
        history.EnsureSuccessStatusCode();
        (await history.Content.ReadAsStringAsync()).Should().Contain(token);

        var leaked = await bob.GetAsync($"/api/v1/topics/{topic.Id}/messages");
        leaked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> CreateForumAsync(HttpClient client, string type)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name = $"forum-{Guid.NewGuid():N}"[..22], type, layout = "Topics" });
        response.EnsureSuccessStatusCode();
        var channel = (await response.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions))!;
        channel.Layout.Should().Be("Topics");
        return channel.Id;
    }

    private static async Task<TopicDto> CreateTopicAsync(HttpClient client, Guid channelId, string title)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/topics",
            new { title, idempotencyKey = $"create-{Guid.NewGuid():N}", tags = new[] { "planejamento" } });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TopicDto>(JsonOptions))!;
    }

    private static async Task<MessageDto> SendAsync(HttpClient client, Guid topicId, string body)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/topics/{topicId}/messages",
            new { messageId = Guid.NewGuid(), idempotencyKey = $"msg-{Guid.NewGuid():N}", body });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<MessageDto>(JsonOptions))!;
    }

    private static async Task FollowAsync(HttpClient client, Guid topicId)
    {
        var response = await client.PostAsync($"/api/v1/topics/{topicId}/subscription", null);
        response.EnsureSuccessStatusCode();
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record ChannelDto(Guid Id, string Layout);
    private sealed record TopicDto(Guid Id, string Slug, string State);
    private sealed record MessageDto(Guid Id, long Sequence);
    private sealed record InboxPageDto(InboxItemDto[] Items);
    private sealed record InboxItemDto(string Kind, Guid? TopicId, long? Sequence);
    private sealed record SearchPageDto(SearchHitDto[] Items);
    private sealed record SearchHitDto(string BodyPreview);
}
