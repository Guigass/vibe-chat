using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ThreadFollowIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Author_reply_and_mention_subscribe_in_same_transaction()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var workspace = SeedData.DemoWorkspaceId.Value;
        var channel = SeedData.DemoChannelId.Value;

        var parentId = Guid.NewGuid();
        var parent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channel}/messages",
            new SendMessageRequest(parentId, $"idem-follow-parent-{parentId:N}", $"parent-{parentId:N}", null, null));
        parent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var open = await alice.PostAsync($"/api/v1/channels/{channel}/messages/{parentId}/threads", null);
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread = await open.Content.ReadFromJsonAsync<ThreadDto>(Json);
        thread.Should().NotBeNull();
        thread!.Following.Should().BeTrue();

        await using (var db = factory.CreateMigratorDbContext())
        {
            var authorSub = await db.ThreadSubscriptions.IgnoreQueryFilters()
                .SingleAsync(x => x.ThreadId == thread.Id && x.UserId == SeedData.AliceUserId);
            authorSub.Source.Should().Be(ThreadSubscriptionSource.Author);
        }

        var replyId = Guid.NewGuid();
        var body = $"reply {MentionTokens.UserBodyToken(SeedData.BobUserId)} {replyId:N}";
        var reply = await alice.PostAsJsonAsync(
            $"/api/v1/threads/{thread.Id}/messages",
            new SendMessageRequest(replyId, $"idem-follow-reply-{replyId:N}", body, parentId, thread.Id));
        reply.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var mention = await db.ThreadSubscriptions.IgnoreQueryFilters()
                .SingleAsync(x => x.ThreadId == thread.Id && x.UserId == SeedData.BobUserId);
            mention.Source.Should().Be(ThreadSubscriptionSource.Mention);
        }

        var following = await bob.GetFromJsonAsync<FollowedPageDto>(
            $"/api/v1/workspaces/{workspace}/threads/following",
            Json);
        following.Should().NotBeNull();
        following!.Items.Should().Contain(x => x.ThreadId == thread.Id && x.UnreadCount >= 1);

        var unfollow = await bob.DeleteAsync($"/api/v1/threads/{thread.Id}/subscription");
        unfollow.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await bob.GetFromJsonAsync<FollowedPageDto>(
            $"/api/v1/workspaces/{workspace}/threads/following",
            Json);
        after!.Items.Should().NotContain(x => x.ThreadId == thread.Id);
    }

    [Fact]
    public async Task Follow_all_threads_subscribes_on_create()
    {
        using var bob = Client("bob");
        using var alice = Client("alice");
        var channel = SeedData.DemoChannelId.Value;

        var pref = await bob.PutAsJsonAsync(
            $"/api/v1/notifications/preferences/channels/{channel}",
            new { followAllThreads = true });
        pref.StatusCode.Should().Be(HttpStatusCode.OK);

        var parentId = Guid.NewGuid();
        var parent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channel}/messages",
            new SendMessageRequest(parentId, $"idem-followall-parent-{parentId:N}", $"all-{parentId:N}", null, null));
        parent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var open = await alice.PostAsync($"/api/v1/channels/{channel}/messages/{parentId}/threads", null);
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread = await open.Content.ReadFromJsonAsync<ThreadDto>(Json);

        await using var db = factory.CreateMigratorDbContext();
        var bobSub = await db.ThreadSubscriptions.IgnoreQueryFilters()
            .SingleAsync(x => x.ThreadId == thread!.Id && x.UserId == SeedData.BobUserId);
        bobSub.Source.Should().Be(ThreadSubscriptionSource.Manual);
    }

    [Fact]
    public async Task Share_to_channel_cites_thread_reply()
    {
        using var alice = Client("alice");
        var channel = SeedData.DemoChannelId.Value;

        var parentId = Guid.NewGuid();
        await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channel}/messages",
            new SendMessageRequest(parentId, $"idem-share-parent-{parentId:N}", $"share-parent-{parentId:N}", null, null));
        var open = await alice.PostAsync($"/api/v1/channels/{channel}/messages/{parentId}/threads", null);
        var thread = await open.Content.ReadFromJsonAsync<ThreadDto>(Json);

        var replyId = Guid.NewGuid();
        var replyBody = $"share-reply-{replyId:N}";
        await alice.PostAsJsonAsync(
            $"/api/v1/threads/{thread!.Id}/messages",
            new SendMessageRequest(replyId, $"idem-share-reply-{replyId:N}", replyBody, parentId, thread.Id));

        var share = await alice.PostAsJsonAsync(
            $"/api/v1/threads/{thread.Id}/messages/{replyId}/share-to-channel",
            new { idempotencyKey = $"idem-share-{replyId:N}", body = "" });
        share.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var shared = await share.Content.ReadFromJsonAsync<JsonElement>(Json);
        shared.GetProperty("replyToMessageId").GetGuid().Should().Be(replyId);
        shared.GetProperty("replyTo").GetProperty("threadId").GetGuid().Should().Be(thread.Id);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record SendMessageRequest(
        Guid MessageId,
        string IdempotencyKey,
        string Body,
        Guid? ReplyToMessageId,
        Guid? ThreadId);

    private sealed record ThreadDto(
        Guid Id,
        Guid ChannelId,
        Guid ParentMessageId,
        Guid CreatedBy,
        DateTimeOffset CreatedAt,
        int ReplyCount,
        bool Following = false);

    private sealed record FollowedThreadDto(Guid ThreadId, int UnreadCount);

    private sealed record FollowedPageDto(FollowedThreadDto[] Items, string? NextCursor, int UnreadTotal);
}
