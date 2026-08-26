using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
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
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        // B-187: push kill switch is process_settings.PushEnabled, not Push:Enabled env.
        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.ProcessSettings.SingleAsync(x => x.Id == ProcessSettings.SingletonId);
            row.PushEnabled = false;
            await db.SaveChangesAsync();
        }

        factory.Services.GetRequiredService<IRuntimeSettingsCacheInvalidator>().InvalidateProcess();

        try
        {
            var response = await client.GetAsync("/api/v1/notifications/push/public-key");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var key = await response.Content.ReadFromJsonAsync<PushPublicKeyDto>(JsonOptions);
            key.Should().NotBeNull();
            key!.Enabled.Should().BeFalse();
            key.PublicKey.Should().BeNull();
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var row = await db.ProcessSettings.SingleAsync(x => x.Id == ProcessSettings.SingletonId);
            row.PushEnabled = true;
            await db.SaveChangesAsync();
            factory.Services.GetRequiredService<IRuntimeSettingsCacheInvalidator>().InvalidateProcess();
        }
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

    [Fact]
    public async Task Get_and_update_global_preferences_round_trip()
    {
        await ResetPushStateAsync();
        using var alice = CreateClient("alice");

        var defaults = await alice.GetFromJsonAsync<NotificationPreferencesDto>(
            "/api/v1/notifications/preferences", JsonOptions);
        defaults.Should().NotBeNull();
        defaults!.Level.Should().Be("MentionsAndDms");
        defaults.HidePreview.Should().BeFalse();
        defaults.ChannelOverrides.Should().BeEmpty();

        var contact = SeedData.BobUserId.Value;
        var put = await alice.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new
            {
                level = "All",
                hidePreview = true,
                dndEnabled = true,
                dndStart = "20:00:00",
                dndEnd = "08:00:00",
                dndDays = 0,
                timeZone = "UTC",
                digestEnabled = true,
                priorityContactUserIds = new[] { contact }
            });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await put.Content.ReadFromJsonAsync<NotificationPreferencesDto>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Level.Should().Be("All");
        updated.HidePreview.Should().BeTrue();
        updated.DndEnabled.Should().BeTrue();
        updated.TimeZone.Should().Be("UTC");
        updated.PriorityContactUserIds.Should().ContainSingle(x => x == contact);

        var reread = await alice.GetFromJsonAsync<NotificationPreferencesDto>(
            "/api/v1/notifications/preferences", JsonOptions);
        reread!.Level.Should().Be("All");

        var invalidTz = await alice.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new { level = "All", dndEnabled = true, dndStart = "20:00:00", dndEnd = "08:00:00", dndDays = 0, timeZone = "Not/AZone" });
        invalidTz.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Global_level_all_notifies_plain_channel_message_none_suppresses_mention()
    {
        await ResetPushStateAsync();
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var bobEndpoint = UniqueEndpoint();
        await RegisterAsync(bob, bobEndpoint);

        var setAll = await bob.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new { level = "All", dndEnabled = false, dndDays = 0 });
        setAll.StatusCode.Should().Be(HttpStatusCode.OK);

        recorder.Reset();
        var plainId = Guid.NewGuid();
        var plain = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(plainId, $"idem-push-all-{plainId:N}", $"plain-{plainId:N}", null, null));
        plain.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        (await WaitForEndpointAsync(recorder, bobEndpoint)).Should().NotBeEmpty("level=All notifies plain channel messages too");

        var setNone = await bob.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new { level = "None", dndEnabled = false, dndDays = 0 });
        setNone.StatusCode.Should().Be(HttpStatusCode.OK);

        recorder.Reset();
        var mentionId = Guid.NewGuid();
        var body = $"hey {MentionTokens.UserBodyToken(SeedData.BobUserId)} {mentionId:N}";
        var mention = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(mentionId, $"idem-push-none-{mentionId:N}", body, null, null));
        mention.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        recorder.Attempts.Should().NotContain(x => x.Endpoint == bobEndpoint, "level=None suppresses even a mention");
    }

    [Fact]
    public async Task Channel_override_all_lets_plain_message_through_and_delete_reverts_to_default()
    {
        await ResetPushStateAsync();
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var bobEndpoint = UniqueEndpoint();
        await RegisterAsync(bob, bobEndpoint);

        var overridePut = await bob.PutAsJsonAsync(
            $"/api/v1/notifications/preferences/channels/{DemoChannelId}",
            new { level = "All" });
        overridePut.StatusCode.Should().Be(HttpStatusCode.OK);
        var overrideDto = await overridePut.Content.ReadFromJsonAsync<ChannelOverrideDto>(JsonOptions);
        overrideDto!.Level.Should().Be("All");
        overrideDto.MutedUntil.Should().BeNull();

        recorder.Reset();
        var plainId = Guid.NewGuid();
        var plain = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(plainId, $"idem-chan-all-{plainId:N}", $"plain-{plainId:N}", null, null));
        plain.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        (await WaitForEndpointAsync(recorder, bobEndpoint)).Should().NotBeEmpty("channel override All beats the MentionsAndDms global default");

        var deleteOverride = await bob.DeleteAsync($"/api/v1/notifications/preferences/channels/{DemoChannelId}");
        deleteOverride.StatusCode.Should().Be(HttpStatusCode.NoContent);

        recorder.Reset();
        var plainId2 = Guid.NewGuid();
        var plain2 = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(plainId2, $"idem-chan-default-{plainId2:N}", $"plain-{plainId2:N}", null, null));
        plain2.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        recorder.Attempts.Should().NotContain(x => x.Endpoint == bobEndpoint, "override removed — back to MentionsAndDms default");
    }

    [Fact]
    public async Task Muting_a_channel_suppresses_push_until_mutedUntil_expires()
    {
        await ResetPushStateAsync();
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var bobEndpoint = UniqueEndpoint();
        await RegisterAsync(bob, bobEndpoint);

        var mute = await bob.PutAsJsonAsync(
            $"/api/v1/notifications/preferences/channels/{DemoChannelId}",
            new { level = "None", duration = "OneHour" });
        mute.StatusCode.Should().Be(HttpStatusCode.OK);
        var muteDto = await mute.Content.ReadFromJsonAsync<ChannelOverrideDto>(JsonOptions);
        muteDto!.MutedUntil.Should().NotBeNull();
        muteDto.MutedUntil!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));

        recorder.Reset();
        var mentionId = Guid.NewGuid();
        var body = $"muted {MentionTokens.UserBodyToken(SeedData.BobUserId)} {mentionId:N}";
        var mention = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(mentionId, $"idem-mute-{mentionId:N}", body, null, null));
        mention.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        recorder.Attempts.Should().NotContain(x => x.Endpoint == bobEndpoint, "even a mention stays muted while the channel is silenced");

        // "Silencia por 1h volta sozinho": simulate the mute already having expired — no cleanup job needed.
        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.ChannelNotificationPreferences
                .SingleAsync(x => x.ChannelId == SeedData.DemoChannelId && x.UserId == SeedData.BobUserId);
            row.MutedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        recorder.Reset();
        var mentionId2 = Guid.NewGuid();
        var body2 = $"unmuted {MentionTokens.UserBodyToken(SeedData.BobUserId)} {mentionId2:N}";
        var mention2 = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(mentionId2, $"idem-unmute-{mentionId2:N}", body2, null, null));
        mention2.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        (await WaitForEndpointAsync(recorder, bobEndpoint)).Should().NotBeEmpty("expired mute falls back to the global default, which notifies on mention");
    }

    [Fact]
    public async Task Dnd_window_suppresses_push_and_priority_contact_bypasses_it()
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

        var bobEndpoint = UniqueEndpoint();
        await RegisterAsync(bob, bobEndpoint);

        // A 2h UTC window centered on "now" so the test is deterministic regardless of wall-clock time.
        var nowUtc = DateTimeOffset.UtcNow;
        var dndStart = TimeOnly.FromDateTime(nowUtc.AddHours(-1).UtcDateTime);
        var dndEnd = TimeOnly.FromDateTime(nowUtc.AddHours(1).UtcDateTime);
        var dnd = await bob.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new
            {
                level = "MentionsAndDms",
                dndEnabled = true,
                dndStart = dndStart.ToString("HH:mm:ss"),
                dndEnd = dndEnd.ToString("HH:mm:ss"),
                dndDays = 0,
                timeZone = "UTC",
                priorityContactUserIds = Array.Empty<Guid>()
            });
        dnd.StatusCode.Should().Be(HttpStatusCode.OK);

        recorder.Reset();
        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{dm!.Id}/messages",
            new SendMessageRequest(messageId, $"idem-dnd-{messageId:N}", $"dnd-{messageId:N}", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        recorder.Attempts.Should().NotContain(x => x.Endpoint == bobEndpoint, "DND window covers now — DM push is suppressed");

        var withPriority = await bob.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new
            {
                level = "MentionsAndDms",
                dndEnabled = true,
                dndStart = dndStart.ToString("HH:mm:ss"),
                dndEnd = dndEnd.ToString("HH:mm:ss"),
                dndDays = 0,
                timeZone = "UTC",
                priorityContactUserIds = new[] { SeedData.AliceUserId.Value }
            });
        withPriority.StatusCode.Should().Be(HttpStatusCode.OK);

        recorder.Reset();
        var messageId2 = Guid.NewGuid();
        var send2 = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{dm.Id}/messages",
            new SendMessageRequest(messageId2, $"idem-dnd-bypass-{messageId2:N}", $"dnd-bypass-{messageId2:N}", null, null));
        send2.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        (await WaitForEndpointAsync(recorder, bobEndpoint)).Should().NotBeEmpty("alice is a priority contact — her DM bypasses DND");
    }

    [Fact]
    public async Task Hide_preview_removes_message_body_from_push_payload()
    {
        await ResetPushStateAsync();
        var recorder = factory.Services.GetRequiredService<RecordingPushSender>();
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");

        var bobEndpoint = UniqueEndpoint();
        await RegisterAsync(bob, bobEndpoint);

        var setHide = await bob.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new { level = "MentionsAndDms", hidePreview = true, dndEnabled = false, dndDays = 0 });
        setHide.StatusCode.Should().Be(HttpStatusCode.OK);

        recorder.Reset();
        var mentionId = Guid.NewGuid();
        var secret = $"secret-body-{mentionId:N}";
        var body = $"{secret} {MentionTokens.UserBodyToken(SeedData.BobUserId)}";
        var mention = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(mentionId, $"idem-hide-{mentionId:N}", body, null, null));
        mention.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();

        var deliveries = await WaitForEndpointAsync(recorder, bobEndpoint);
        deliveries.Should().NotBeEmpty();
        deliveries[0].PayloadJson.Should().NotContain(secret);
        using var doc = JsonDocument.Parse(deliveries[0].PayloadJson);
        doc.RootElement.GetProperty("notification").GetProperty("body").GetString().Should().BeEmpty();
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

        // B-097: other tests in this collection mutate alice/bob's notification preferences —
        // reset them here too so every test (old and new) starts from the MentionsAndDms default
        // regardless of xUnit's execution order within the shared collection fixture.
        var prefs = await db.NotificationPreferences.IgnoreQueryFilters()
            .Where(x => x.UserId == SeedData.AliceUserId || x.UserId == SeedData.BobUserId)
            .ToListAsync();
        if (prefs.Count > 0)
        {
            db.NotificationPreferences.RemoveRange(prefs);
        }

        var overrides = await db.ChannelNotificationPreferences.IgnoreQueryFilters()
            .Where(x => x.UserId == SeedData.AliceUserId || x.UserId == SeedData.BobUserId)
            .ToListAsync();
        if (overrides.Count > 0)
        {
            db.ChannelNotificationPreferences.RemoveRange(overrides);
        }

        if (rows.Count > 0 || cursors.Count > 0 || prefs.Count > 0 || overrides.Count > 0)
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

    private sealed record ChannelOverrideDto(Guid ChannelId, string Level, DateTimeOffset? MutedUntil);

    private sealed record NotificationPreferencesDto(
        string Level,
        bool HidePreview,
        bool DndEnabled,
        TimeOnly? DndStart,
        TimeOnly? DndEnd,
        short DndDays,
        string? TimeZone,
        bool DigestEnabled,
        Guid[] PriorityContactUserIds,
        ChannelOverrideDto[] ChannelOverrides);
}
