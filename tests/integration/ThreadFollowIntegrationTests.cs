using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-102 — seguir thread: auto-follow, manual follow/unfollow, unread count, push bypass.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ThreadFollowIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid DemoChannelId = SeedData.DemoChannelId.Value;
    private static readonly Guid DemoWorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Replying_in_thread_auto_follows_with_own_message_marked_read()
    {
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var thread = await OpenThreadAsync(alice, alice, "root-reply");
        var reply = await SendThreadReplyAsync(bob, thread.Id, "bob-reply");

        var threadState = await bob.GetFromJsonAsync<ThreadDto>($"/api/v1/threads/{thread.Id}", JsonOptions);
        threadState!.Following.Should().BeTrue();
        threadState.FollowSource.Should().Be("Reply");

        var following = await GetFollowingAsync(bob);
        var entry = following.Items.Should().ContainSingle(x => x.ThreadId == thread.Id).Subject;
        entry.UnreadCount.Should().Be(0, "the replier's own message counts as read");
        _ = reply;
    }

    [Fact]
    public async Task Mention_in_thread_auto_follows_with_unread_history()
    {
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var thread = await OpenThreadAsync(alice, alice, "root-mention");
        // Two replies before Bob is ever mentioned — they should count as unread once he's pulled in.
        await SendThreadReplyAsync(alice, thread.Id, "alice-1");
        await SendThreadReplyAsync(alice, thread.Id, "alice-2");
        await SendThreadReplyAsync(alice, thread.Id, $"hey {MentionTokens.UserBodyToken(SeedData.BobUserId)}");

        var threadState = await bob.GetFromJsonAsync<ThreadDto>($"/api/v1/threads/{thread.Id}", JsonOptions);
        threadState!.Following.Should().BeTrue();
        threadState.FollowSource.Should().Be("Mention");

        var following = await GetFollowingAsync(bob);
        var entry = following.Items.Should().ContainSingle(x => x.ThreadId == thread.Id).Subject;
        entry.UnreadCount.Should().Be(3, "all three replies predate Bob ever reading the thread");
    }

    [Fact]
    public async Task Root_author_auto_follows_when_someone_else_opens_the_thread()
    {
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-root-{messageId:N}", $"root-{messageId:N}", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Bob (not Alice, the root author) is the one who opens the thread.
        var open = await bob.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{messageId}/threads",
            new { });
        open.EnsureSuccessStatusCode();
        var thread = (await open.Content.ReadFromJsonAsync<ThreadDto>(JsonOptions))!;

        var aliceThreadState = await alice.GetFromJsonAsync<ThreadDto>($"/api/v1/threads/{thread.Id}", JsonOptions);
        aliceThreadState!.Following.Should().BeTrue("Alice authored the root message");
        aliceThreadState.FollowSource.Should().Be("Author");

        var bobThreadState = await bob.GetFromJsonAsync<ThreadDto>($"/api/v1/threads/{thread.Id}", JsonOptions);
        bobThreadState!.Following.Should().BeFalse("opening a thread does not itself follow it");
    }

    [Fact]
    public async Task Manual_follow_and_unfollow_round_trip()
    {
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var thread = await OpenThreadAsync(alice, alice, "root-manual");

        var follow = await bob.PostAsync($"/api/v1/threads/{thread.Id}/subscription", null);
        follow.StatusCode.Should().Be(HttpStatusCode.OK);
        var followed = await follow.Content.ReadFromJsonAsync<ThreadSubscriptionDto>(JsonOptions);
        followed!.Source.Should().Be("Manual");

        (await GetFollowingAsync(bob)).Items.Should().Contain(x => x.ThreadId == thread.Id);

        var unfollow = await bob.DeleteAsync($"/api/v1/threads/{thread.Id}/subscription");
        unfollow.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetFollowingAsync(bob)).Items.Should().NotContain(x => x.ThreadId == thread.Id);
    }

    [Fact]
    public async Task Follow_all_threads_subscribes_new_threads_but_not_retroactively()
    {
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var preexisting = await OpenThreadAsync(alice, alice, "root-before-optin");

        var optIn = await bob.PutAsJsonAsync(
            $"/api/v1/notifications/preferences/channels/{DemoChannelId}/follow-all-threads",
            new { enabled = true });
        optIn.StatusCode.Should().Be(HttpStatusCode.NoContent);

        try
        {
            var newThread = await OpenThreadAsync(alice, alice, "root-after-optin");

            var following = await GetFollowingAsync(bob);
            following.Items.Should().Contain(x => x.ThreadId == newThread.Id, "created after opt-in");
            following.Items.Should().NotContain(x => x.ThreadId == preexisting.Id, "not retroactive");
        }
        finally
        {
            await bob.PutAsJsonAsync(
                $"/api/v1/notifications/preferences/channels/{DemoChannelId}/follow-all-threads",
                new { enabled = false });
        }
    }

    [Fact]
    public async Task Read_cursor_advance_clears_unread_count()
    {
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var thread = await OpenThreadAsync(alice, alice, "root-cursor");
        var follow = await bob.PostAsync($"/api/v1/threads/{thread.Id}/subscription", null);
        var followed = (await follow.Content.ReadFromJsonAsync<ThreadSubscriptionDto>(JsonOptions))!;

        await SendThreadReplyAsync(alice, thread.Id, "alice-after-follow-1");
        await SendThreadReplyAsync(alice, thread.Id, "alice-after-follow-2");

        var afterReplies = await GetFollowingAsync(bob);
        afterReplies.Items.Should().ContainSingle(x => x.ThreadId == thread.Id).Subject.UnreadCount.Should().Be(2);

        var advance = await bob.PutAsJsonAsync(
            $"/api/v1/threads/{thread.Id}/subscription/read-cursor",
            new { lastReadSequence = followed.LastReadSeq + 2 });
        advance.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterRead = await GetFollowingAsync(bob);
        afterRead.Items.Should().ContainSingle(x => x.ThreadId == thread.Id).Subject.UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task Cross_tenant_membership_loss_hides_thread_from_following_list()
    {
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var thread = await OpenThreadAsync(alice, alice, "root-leave");
        var private_ = await CreatePrivateChannelWithThreadAsync(alice, bob);

        (await GetFollowingAsync(bob)).Items.Should().Contain(x => x.ThreadId == private_.ThreadId);

        // Bob leaves the private channel directly at the DB layer (fastest way to simulate a
        // membership change without adding a bespoke "leave private channel" flow to this test).
        await using (var db = factory.CreateMigratorDbContext())
        {
            var membership = await db.ChannelMembers.IgnoreQueryFilters()
                .SingleAsync(x => x.ChannelId == new VibeChat.SharedKernel.ChannelId(private_.ChannelId) && x.UserId == SeedData.BobUserId);
            db.ChannelMembers.Remove(membership);
            await db.SaveChangesAsync();
        }

        (await GetFollowingAsync(bob)).Items.Should().NotContain(x => x.ThreadId == private_.ThreadId);
        _ = thread;
    }

    [Fact]
    public async Task Reply_in_followed_thread_dispatches_push_without_a_mention()
    {
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var thread = await OpenThreadAsync(alice, alice, "root-push");
        var bobEndpoint = UniqueEndpoint();
        await RegisterPushAsync(bob, bobEndpoint);
        await bob.PostAsync($"/api/v1/threads/{thread.Id}/subscription", null);
        recorder.Reset();

        var plainReply = await SendThreadReplyAsync(alice, thread.Id, "alice-plain-reply-no-mention");
        await DrainOutboxAsync();

        var deliveries = await WaitForEndpointAsync(recorder, bobEndpoint);
        deliveries.Should().NotBeEmpty("Bob follows the thread, so a plain reply still notifies him");
        _ = plainReply;
    }

    private async Task<ThreadDto> OpenThreadAsync(HttpClient rootAuthor, HttpClient opener, string label)
    {
        var messageId = Guid.NewGuid();
        var send = await rootAuthor.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-{label}-{messageId:N}", $"{label}-{messageId:N}", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var open = await opener.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages/{messageId}/threads",
            new { });
        open.EnsureSuccessStatusCode();
        return (await open.Content.ReadFromJsonAsync<ThreadDto>(JsonOptions))!;
    }

    private static async Task<Guid> SendThreadReplyAsync(HttpClient client, Guid threadId, string body)
    {
        var messageId = Guid.NewGuid();
        var send = await client.PostAsJsonAsync(
            $"/api/v1/threads/{threadId}/messages",
            new SendMessageRequest(messageId, $"idem-reply-{messageId:N}", $"{body}-{messageId:N}", null, threadId));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return messageId;
    }

    private async Task<(Guid ChannelId, Guid ThreadId)> CreatePrivateChannelWithThreadAsync(HttpClient alice, HttpClient bob)
    {
        var create = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{DemoWorkspaceId}/channels",
            new { name = $"priv-{Guid.NewGuid():N}", type = "Private" });
        create.EnsureSuccessStatusCode();
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions))!;

        // No API adds a member to a Private channel directly in this slice — seed at the DB layer,
        // same as the B-093 membership-loss security test does.
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.ChannelMembers.Add(new VibeChat.Conversations.ChannelMember
            {
                Id = Guid.NewGuid(),
                TenantId = SeedData.DemoTenantId,
                ChannelId = new VibeChat.SharedKernel.ChannelId(channel.Id),
                UserId = SeedData.BobUserId,
                JoinedAt = DateTimeOffset.UtcNow,
                JoinedSeq = 0
            });
            await db.SaveChangesAsync();
        }

        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channel.Id}/messages",
            new SendMessageRequest(messageId, $"idem-priv-{messageId:N}", $"priv-{messageId:N}", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var open = await bob.PostAsJsonAsync(
            $"/api/v1/channels/{channel.Id}/messages/{messageId}/threads",
            new { });
        open.EnsureSuccessStatusCode();
        var thread = (await open.Content.ReadFromJsonAsync<ThreadDto>(JsonOptions))!;

        var follow = await bob.PostAsync($"/api/v1/threads/{thread.Id}/subscription", null);
        follow.StatusCode.Should().Be(HttpStatusCode.OK);

        return (channel.Id, thread.Id);
    }

    private static async Task<FollowedThreadsPageDto> GetFollowingAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<FollowedThreadsPageDto>(
            $"/api/v1/workspaces/{DemoWorkspaceId}/threads/following", JsonOptions);
        return response!;
    }

    private HttpClient CreateClient(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static string UniqueEndpoint() => $"https://push.example.test/{Guid.NewGuid():N}";

    private static async Task RegisterPushAsync(HttpClient client, string endpoint)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/notifications/push/subscriptions",
            new
            {
                endpoint,
                p256dh = "dGVzdC1wMjU2ZGgtYWxpY2UtMjI=",
                auth = "dGVzdC1hdXRoLWtleS1hbGljZQ==",
                userAgent = "VibeChat-Tests"
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task DrainOutboxAsync()
    {
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        for (var i = 0; i < 8; i++)
        {
            var processed = await processor.ProcessBatchAsync(CancellationToken.None);
            if (processed == 0)
            {
                break;
            }
        }
    }

    private static async Task<List<PushDeliveryRequest>> WaitForEndpointAsync(RecordingPushSender recorder, string endpoint)
    {
        for (var i = 0; i < 20; i++)
        {
            var hit = recorder.Attempts.Where(x => x.Endpoint == endpoint).ToList();
            if (hit.Count > 0)
            {
                return hit;
            }

            await Task.Delay(50);
        }

        return recorder.Attempts.Where(x => x.Endpoint == endpoint).ToList();
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
        object? ParentMessage,
        bool Following,
        string? FollowSource);

    private sealed record ThreadSubscriptionDto(Guid ThreadId, bool Following, string Source, long LastReadSeq);

    private sealed record FollowedThreadDto(
        Guid ThreadId,
        Guid ChannelId,
        string ChannelName,
        string ChannelType,
        string RootPreview,
        bool RootDeleted,
        long UnreadCount,
        DateTimeOffset LastActivityAt);

    private sealed record FollowedThreadsPageDto(FollowedThreadDto[] Items, string? NextCursor);

    private sealed record ChannelDto(Guid Id, string Name, string Type);
}
