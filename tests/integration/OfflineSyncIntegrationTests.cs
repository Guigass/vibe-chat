using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class OfflineSyncIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid Device = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Flag_off_keeps_send_and_hides_sync()
    {
        await using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:OfflineWrite:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var sync = await client.GetAsync($"/api/v1/sync?deviceId={Device}");
        sync.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await sync.Content.ReadAsStringAsync()).Should().Contain("OfflineWriteDisabled");

        var messageId = Guid.NewGuid();
        var sent = await client.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId, idempotencyKey = $"off-{messageId:N}", body = $"off-{messageId:N}" });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Sync_returns_a_message_then_a_tombstone_without_the_body()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var messageId = Guid.NewGuid();
        var body = $"offline-{messageId:N}";
        var sent = await client.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId, idempotencyKey = $"sync-{messageId:N}", body });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var first = await client.GetFromJsonAsync<SyncDto>($"/api/v1/sync?deviceId={Device}", Json);
        first.Should().NotBeNull();
        first!.ProtocolVersion.Should().Be(1);
        first.Cursor.Should().NotBeNullOrWhiteSpace();
        first.Changes.Should().Contain(item => item.Id == messageId && item.Body == body && item.Seq > 0);

        var removed = await client.DeleteAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await client.GetFromJsonAsync<SyncDto>(
            $"/api/v1/sync?deviceId={Device}&cursor={Uri.EscapeDataString(first.Cursor)}",
            Json);
        second!.Tombstones.Should().Contain(item => item.Id == messageId && item.Body == null && item.Reason == "deleted");
        second.Changes.Should().NotContain(item => item.Id == messageId);
        var raw = JsonSerializer.Serialize(second);
        raw.Should().NotContain(body);
    }

    [Fact]
    public async Task Expired_cursor_rebuilds_and_keeps_the_recent_message()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var messageId = Guid.NewGuid();
        var body = $"rebuild-{messageId:N}";
        var sent = await client.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId, idempotencyKey = $"rebuild-{messageId:N}", body });
        sent.EnsureSuccessStatusCode();

        var expired = VibeChat.Messaging.OfflineSyncCursor.Issue(
            SeedData.DemoTenantId.Value,
            SeedData.AliceUserId.Value,
            Device,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Guid.Empty,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "vibechat-lab-offline-cursor");
        var page = await client.GetFromJsonAsync<SyncDto>(
            $"/api/v1/sync?deviceId={Device}&cursor={Uri.EscapeDataString(expired)}",
            Json);
        page!.Rebuild!.Reason.Should().Be("CursorExpired");
        page.Changes.Should().Contain(item => item.Id == messageId && item.Body == body);
        page.Cursor.Should().NotBe(expired);
    }

    [Fact]
    public async Task Stale_edit_conflicts_and_reaction_retry_does_not_toggle_off()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var messageId = Guid.NewGuid();
        var sent = await client.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId, idempotencyKey = $"edit-{messageId:N}", body = "original" });
        sent.EnsureSuccessStatusCode();
        var accepted = await sent.Content.ReadFromJsonAsync<AcceptedDto>(Json);

        var edited = await client.PutAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}",
            new { body = "server", expectedVersion = 0L });
        edited.StatusCode.Should().Be(HttpStatusCode.OK);

        var conflict = await client.PutAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}",
            new { body = "stale", expectedVersion = 0L });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await conflict.Content.ReadFromJsonAsync<ConflictDto>(Json);
        payload!.Error.Should().Be("MessageVersionConflict");
        payload.CurrentBody.Should().Be("server");

        var listed = await client.GetFromJsonAsync<HistoryDto>(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages?after={accepted!.Sequence - 1}",
            Json);
        listed!.Messages.Should().Contain(message => message.Id == messageId && message.Body == "server");

        var first = await client.PutAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}/reactions",
            new { emoji = "👍", intent = "add" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await client.PutAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}/reactions",
            new { emoji = "👍", intent = "add" });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var reaction = await again.Content.ReadFromJsonAsync<ReactionDto>(Json);
        reaction!.Added.Should().BeTrue();
        reaction.Reactions.Should().Contain(item => item.Emoji == "👍" && item.Count == 1 && item.Me);
    }

    [Fact]
    public async Task Repeated_send_keeps_one_sequence()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var messageId = Guid.NewGuid();
        var key = $"idem-{messageId:N}";
        var body = new { messageId, idempotencyKey = key, body = "once" };
        var first = await client.PostAsJsonAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages", body);
        var second = await client.PostAsJsonAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages", body);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var left = await first.Content.ReadFromJsonAsync<AcceptedDto>(Json);
        var right = await second.Content.ReadFromJsonAsync<AcceptedDto>(Json);
        right!.Sequence.Should().Be(left!.Sequence);
        right.Id.Should().Be(left.Id);
    }

    private sealed record SyncDto(
        int ProtocolVersion,
        string Cursor,
        SyncItem[] Changes,
        SyncItem[] Tombstones,
        bool HasMore,
        RebuildDto? Rebuild);

    private sealed record SyncItem(Guid Id, long Seq, string? Body, string? Reason);

    private sealed record RebuildDto(string Reason);

    private sealed record ConflictDto(string Error, long CurrentVersion, string CurrentBody);

    private sealed record HistoryDto(HistoryMessage[] Messages);

    private sealed record HistoryMessage(Guid Id, string Body);

    private sealed record ReactionDto(bool Added, ReactionItem[] Reactions);

    private sealed record ReactionItem(string Emoji, int Count, bool Me);

    private sealed record AcceptedDto(Guid Id, long Sequence);
}
