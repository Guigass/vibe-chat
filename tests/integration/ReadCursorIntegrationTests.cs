using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ReadCursorIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid DemoChannelId = SeedData.DemoChannelId.Value;
    private static readonly Guid DemoWorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Read_cursor_is_monotonic_and_unread_summary_reflects_it()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", $"read-cursor-{messageId:N}", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var sent = await send.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        sent.Should().NotBeNull();
        var seq = sent!.Sequence;

        var firstPut = await alice.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/read-cursor",
            new { lastReadSequence = seq - 1 });
        firstPut.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondPut = await alice.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/read-cursor",
            new { lastReadSequence = seq - 2 });
        secondPut.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await secondPut.Content.ReadFromJsonAsync<ReadCursorDto>(JsonOptions);
        secondBody!.LastReadSequence.Should().Be(seq - 1);

        var unread = await alice.GetFromJsonAsync<UnreadCountDto>(
            $"/api/v1/channels/{DemoChannelId}/unread-count",
            JsonOptions);
        unread.Should().NotBeNull();
        unread!.UnreadCount.Should().BeGreaterThanOrEqualTo(1);

        var batch = await alice.GetFromJsonAsync<ChannelUnreadSummaryDto[]>(
            $"/api/v1/workspaces/{DemoWorkspaceId}/channels/unread",
            JsonOptions);
        batch.Should().NotBeNull();
        batch!.Should().Contain(x => x.ChannelId == DemoChannelId && x.UnreadCount >= 1);

        var advance = await alice.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/read-cursor",
            new { lastReadSequence = seq });
        advance.StatusCode.Should().Be(HttpStatusCode.OK);

        var cleared = await alice.GetFromJsonAsync<UnreadCountDto>(
            $"/api/v1/channels/{DemoChannelId}/unread-count",
            JsonOptions);
        cleared!.UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task Read_cursor_allows_retrograde_when_requested()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var messageId = Guid.NewGuid();
        var send = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", $"mark-unread-{messageId:N}", null, null));
        send.EnsureSuccessStatusCode();
        var sent = await send.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        var seq = sent!.Sequence;

        await alice.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/read-cursor",
            new { lastReadSequence = seq });

        var retro = await alice.PutAsJsonAsync(
            $"/api/v1/channels/{DemoChannelId}/read-cursor",
            new { lastReadSequence = seq - 1, allowRetrograde = true });
        retro.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await retro.Content.ReadFromJsonAsync<ReadCursorDto>(JsonOptions);
        body!.LastReadSequence.Should().Be(seq - 1);

        var unread = await alice.GetFromJsonAsync<UnreadCountDto>(
            $"/api/v1/channels/{DemoChannelId}/unread-count",
            JsonOptions);
        unread!.UnreadCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Read_cursor_cross_tenant_returns_forbidden()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        await using var db = factory.CreateMigratorDbContext();
        var foreignChannel = await db.Channels.IgnoreQueryFilters()
            .Where(x => x.TenantId != SeedData.DemoTenantId)
            .Select(x => x.Id.Value)
            .FirstOrDefaultAsync();

        if (foreignChannel == Guid.Empty)
        {
            return;
        }

        var response = await alice.PutAsJsonAsync(
            $"/api/v1/channels/{foreignChannel}/read-cursor",
            new { lastReadSequence = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record SendMessageRequest(
        Guid MessageId,
        string IdempotencyKey,
        string Body,
        Guid? ReplyToMessageId,
        Guid? ThreadId);
    private sealed record MessageDto(Guid Id, long Sequence);
    private sealed record ReadCursorDto(long LastReadSequence);
    private sealed record UnreadCountDto(int UnreadCount, int MentionCount);
    private sealed record ChannelUnreadSummaryDto(Guid ChannelId, int UnreadCount, int MentionCount, long LastReadSeq);
}
