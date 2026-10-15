using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class DigestIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Catch_up_without_ai_is_idempotent_and_does_not_mark_read()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var secret = $"digest-secret-{Guid.NewGuid():N}";
        var channelId = await CreatePrivateAsync(alice);
        (await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{channelId}/members",
            new { userId = SeedData.BobUserId.Value })).StatusCode.Should().Be(HttpStatusCode.OK);
        var messageId = Guid.NewGuid();
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages",
            new { messageId, idempotencyKey = $"idem-{messageId:N}", body = secret });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await using var db = factory.CreateMigratorDbContext();
        var before = await db.ReadCursors.AsNoTracking()
            .Where(row => row.UserId == SeedData.BobUserId && row.ChannelId == new VibeChat.SharedKernel.ChannelId(channelId))
            .Select(row => (long?)row.LastReadSequence)
            .FirstOrDefaultAsync();

        var idempotency = $"catchup-{Guid.NewGuid():N}";
        var first = await CatchUpAsync(bob, idempotency);
        first.AiStatus.Should().Be("off");
        first.Sections.SelectMany(section => section.Items).Select(item => item.SourceId).Should().Contain(messageId);
        var raw = JsonSerializer.Serialize(first);
        raw.Should().NotContain(secret);
        raw.Should().NotContain("body");

        var second = await CatchUpAsync(bob, idempotency);
        second.Id.Should().Be(first.Id);
        second.DeliveryCount.Should().Be(first.DeliveryCount);

        var after = await db.ReadCursors.AsNoTracking()
            .Where(row => row.UserId == SeedData.BobUserId && row.ChannelId == new VibeChat.SharedKernel.ChannelId(channelId))
            .Select(row => (long?)row.LastReadSequence)
            .FirstOrDefaultAsync();
        after.Should().Be(before);

        var audit = await db.AuditEvents.AsNoTracking()
            .Where(row => row.Action == "digest.generated" && row.EntityId == first.Id.ToString("D"))
            .Select(row => row.MetadataJson)
            .FirstAsync();
        audit.Should().NotContain(secret);
        audit.Should().Contain("unread");
    }

    [Fact]
    public async Task Dnd_defers_external_delivery_without_a_second_send()
    {
        using var bob = Client("bob");
        var prefs = await bob.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new
            {
                level = "MentionsAndDms",
                hidePreview = true,
                dndEnabled = true,
                dndStart = "00:00:00",
                dndEnd = "23:59:00",
                dndDays = 0,
                timeZone = "UTC",
                digestEnabled = true,
                priorityContactUserIds = Array.Empty<Guid>(),
            });
        prefs.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await bob.PutAsJsonAsync(
            "/api/v1/notifications/digests/settings",
            new
            {
                enabled = true,
                cadence = "daily",
                localTime = "00:00",
                weekday = 1,
                timeZone = "UTC",
                delivery = "email",
                includeAi = false,
                channelIds = Array.Empty<Guid>(),
            });
        settings.StatusCode.Should().Be(HttpStatusCode.OK);

        var idempotency = $"dnd-{Guid.NewGuid():N}";
        var first = await CatchUpAsync(bob, idempotency);
        first.ExternalDeferred.Should().BeTrue();
        first.DeliveryCount.Should().Be(0);
        var second = await CatchUpAsync(bob, idempotency);
        second.Id.Should().Be(first.Id);
        second.DeliveryCount.Should().Be(0);
    }

    private async Task<DigestDto> CatchUpAsync(HttpClient client, string idempotency)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/digests/catch-up");
        request.Headers.Add("Idempotency-Key", idempotency);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DigestDto>(JsonOptions))!;
    }

    private static async Task<Guid> CreatePrivateAsync(HttpClient client)
    {
        var name = $"dg-{Guid.NewGuid():N}"[..18];
        var created = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name, type = "Private" });
        created.EnsureSuccessStatusCode();
        var channel = (await created.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;
        return channel.Id;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record IdDto(Guid Id);

    private sealed record DigestDto(
        Guid Id,
        string Status,
        string AiStatus,
        string? Summary,
        string Delivery,
        int DeliveryCount,
        bool ExternalDeferred,
        DigestSectionDto[] Sections);

    private sealed record DigestSectionDto(string Kind, DigestItemDto[] Items);

    private sealed record DigestItemDto(Guid SourceId, Guid ChannelId, string ChannelName, Guid? MessageId, long? Sequence);
}
