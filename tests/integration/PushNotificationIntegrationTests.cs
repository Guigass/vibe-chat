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

[Collection(IntegrationCollection.Name)]
public sealed class PushNotificationIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid DemoChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Public_key_is_enabled_in_test_host_and_subscriptions_round_trip()
    {
        await ResetPushStateAsync();
        using var alice = CreateClient("alice");

        var key = await alice.GetFromJsonAsync<PushPublicKeyDto>(
            "/api/v1/notifications/push/public-key",
            JsonOptions);
        key.Should().NotBeNull();
        key!.Enabled.Should().BeTrue();
        key.PublicKey.Should().NotBeNullOrWhiteSpace();

        var endpoint = UniqueEndpoint();
        var created = await RegisterAsync(alice, endpoint);
        created.Endpoint.Should().Be(endpoint);

        var list = await alice.GetFromJsonAsync<PushSubscriptionDto[]>(
            "/api/v1/notifications/push/subscriptions",
            JsonOptions);
        list.Should().ContainSingle(x => x.Id == created.Id && x.Endpoint == endpoint);

        var deleted = await alice.DeleteAsync($"/api/v1/notifications/push/subscriptions/{created.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var empty = await alice.GetFromJsonAsync<PushSubscriptionDto[]>(
            "/api/v1/notifications/push/subscriptions",
            JsonOptions);
        empty.Should().NotContain(x => x.Id == created.Id);
    }

    [Fact]
    public async Task Kill_switch_returns_enabled_false_without_error()
    {
        await using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Push:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var response = await client.GetAsync("/api/v1/notifications/push/public-key");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var key = await response.Content.ReadFromJsonAsync<PushPublicKeyDto>(JsonOptions);
        key.Should().NotBeNull();
        key!.Enabled.Should().BeFalse();
        key.PublicKey.Should().BeNull();
    }

    [Fact]
    public async Task Mention_in_channel_dispatches_push_plain_channel_message_does_not()
    {
        await ResetPushStateAsync();
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var bobEndpoint = UniqueEndpoint();
        await RegisterAsync(bob, bobEndpoint);
        recorder.Reset();

        var plainId = Guid.NewGuid();
        var plain = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(plainId, $"idem-push-plain-{plainId:N}", $"plain-{plainId:N}", null, null));
        plain.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        recorder.Attempts.Should().NotContain(x => x.Endpoint == bobEndpoint);

        recorder.Reset();
        var mentionId = Guid.NewGuid();
        var body = $"hey {MentionTokens.UserBodyToken(SeedData.BobUserId)} push-{mentionId:N}";
        var mention = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(mentionId, $"idem-push-mention-{mentionId:N}", body, null, null));
        mention.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();

        var deliveries = await WaitForEndpointAsync(recorder, bobEndpoint);
        deliveries.Should().NotBeEmpty();
        deliveries[0].PayloadJson.Should().Contain(mentionId.ToString());
        deliveries[0].PayloadJson.Should().Contain("notification");
        deliveries[0].PayloadJson.Should().NotContain(bobEndpoint);
    }

    [Fact]
    public async Task Direct_message_dispatches_push_to_peer()
    {
        await ResetPushStateAsync();
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var open = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/dms",
            new OpenDirectMessageRequest(SeedData.BobUserId.Value));
        open.EnsureSuccessStatusCode();
        var dm = await open.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions);
        dm.Should().NotBeNull();

        var bobEndpoint = UniqueEndpoint();
        await RegisterAsync(bob, bobEndpoint);
        recorder.Reset();

        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{dm!.Id}/messages",
            new SendMessageRequest(messageId, $"idem-push-dm-{messageId:N}", $"dm-{messageId:N}", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();

        var deliveries = await WaitForEndpointAsync(recorder, bobEndpoint);
        deliveries.Should().NotBeEmpty();
        recorder.Attempts.Should().NotContain(x => x.Endpoint != bobEndpoint && x.PayloadJson.Contains(messageId.ToString()));
    }

    [Fact]
    public async Task Read_cursor_suppresses_push()
    {
        await ResetPushStateAsync();
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var bobEndpoint = UniqueEndpoint();
        await RegisterAsync(bob, bobEndpoint);

        var cursor = await bob.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/read-cursor",
            new { lastReadSequence = 999_999_999L });
        cursor.StatusCode.Should().Be(HttpStatusCode.OK);

        recorder.Reset();
        var mentionId = Guid.NewGuid();
        var body = $"cursor {MentionTokens.UserBodyToken(SeedData.BobUserId)} {mentionId:N}";
        var mention = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(mentionId, $"idem-push-cursor-{mentionId:N}", body, null, null));
        mention.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();

        recorder.Attempts.Should().NotContain(x => x.Endpoint == bobEndpoint);
    }

    [Fact]
    public async Task Gone_subscription_is_deleted()
    {
        await ResetPushStateAsync();
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var bobEndpoint = UniqueEndpoint();
        var created = await RegisterAsync(bob, bobEndpoint);
        recorder.Reset();
        recorder.NextStatusByEndpoint[bobEndpoint] = PushSendStatus.Gone;

        var mentionId = Guid.NewGuid();
        var body = $"gone {MentionTokens.UserBodyToken(SeedData.BobUserId)} {mentionId:N}";
        var mention = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(mentionId, $"idem-push-gone-{mentionId:N}", body, null, null));
        mention.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        await WaitForEndpointAsync(recorder, bobEndpoint);

        await using var db = factory.CreateMigratorDbContext();
        var remaining = await db.PushSubscriptions.IgnoreQueryFilters()
            .AnyAsync(x => x.Id == created.Id);
        remaining.Should().BeFalse();
    }

    private HttpClient CreateClient(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static string UniqueEndpoint() => $"https://push.example.test/{Guid.NewGuid():N}";

    private static async Task<PushSubscriptionDto> RegisterAsync(HttpClient client, string endpoint)
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
        var row = await response.Content.ReadFromJsonAsync<PushSubscriptionDto>(JsonOptions);
        row.Should().NotBeNull();
        return row!;
    }

    private async Task ResetPushStateAsync()
    {
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        recorder.Reset();
        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.PushSubscriptions.IgnoreQueryFilters().ToListAsync();
        if (rows.Count > 0)
        {
            db.PushSubscriptions.RemoveRange(rows);
        }

        var cursors = await db.ReadCursors.IgnoreQueryFilters()
            .Where(x => x.ChannelId == SeedData.DemoChannelId)
            .ToListAsync();
        foreach (var cursor in cursors)
        {
            cursor.LastReadSequence = 0;
        }

        if (rows.Count > 0 || cursors.Count > 0)
        {
            await db.SaveChangesAsync();
        }
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

    private static async Task<List<PushDeliveryRequest>> WaitForEndpointAsync(
        RecordingPushSender recorder,
        string endpoint)
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

    private sealed record PushPublicKeyDto(bool Enabled, string? PublicKey);

    private sealed record PushSubscriptionDto(
        Guid Id,
        string Endpoint,
        string? UserAgent,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastSeenAt);

    private sealed record ChannelDto(Guid Id, string Name, string Type);
}
