using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class AnnouncementIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Publish_ack_is_idempotent_and_edit_close_are_audited()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");

        var channelId = await CreateAnnouncementChannelAsync(demo);
        var denied = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages",
            new SendMessageRequest(Guid.NewGuid(), $"ack-deny-{Guid.NewGuid():N}", "nope", null, null, RequiresAcknowledgement: true));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deadline = DateTimeOffset.UtcNow.AddHours(2);
        var messageId = Guid.NewGuid();
        var published = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages",
            new SendMessageRequest(
                messageId,
                $"ack-pub-{messageId:N}",
                "Leiam até sexta",
                null,
                null,
                RequiresAcknowledgement: true,
                AcknowledgeBy: deadline));
        published.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await published.Content.ReadFromJsonAsync<MessageDto>(JsonOptions);
        created!.Announcement.Should().NotBeNull();
        created.Announcement!.RequiresAcknowledgement.Should().BeTrue();
        created.Announcement.AcknowledgementCount.Should().Be(0);

        var pending = await alice.GetFromJsonAsync<PendingDto[]>(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/announcements/pending",
            JsonOptions);
        pending.Should().Contain(x => x.MessageId == messageId);

        var unread = await alice.GetFromJsonAsync<UnreadDto[]>(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/unread",
            JsonOptions);
        unread.Should().Contain(x => x.ChannelId == channelId && x.PendingAnnouncementCount >= 1);

        var first = await alice.PostAsync($"/api/v1/channels/{channelId}/messages/{messageId}/acknowledgements", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var acked = await first.Content.ReadFromJsonAsync<AnnouncementDto>(JsonOptions);
        acked!.AcknowledgementCount.Should().Be(1);
        acked.AcknowledgedByMe.Should().BeTrue();

        var repeat = await alice.PostAsync($"/api/v1/channels/{channelId}/messages/{messageId}/acknowledgements", null);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await repeat.Content.ReadFromJsonAsync<AnnouncementDto>(JsonOptions);
        again!.AcknowledgementCount.Should().Be(1);

        var memberReport = await alice.GetAsync($"/api/v1/channels/{channelId}/messages/{messageId}/acknowledgements");
        memberReport.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var report = await demo.GetFromJsonAsync<ReportDto>(
            $"/api/v1/channels/{channelId}/messages/{messageId}/acknowledgements",
            JsonOptions);
        report!.Count.Should().Be(1);
        report.Items.Should().ContainSingle();

        var otherChannel = await CreateAnnouncementChannelAsync(demo);
        var crossed = await demo.GetAsync($"/api/v1/channels/{otherChannel}/messages/{messageId}/acknowledgements");
        crossed.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var edited = await demo.PutAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages/{messageId}",
            new EditMessageRequest("Leiam até sexta, atualizado"));
        edited.StatusCode.Should().Be(HttpStatusCode.OK);

        var closed = await demo.PostAsync($"/api/v1/channels/{channelId}/messages/{messageId}/acknowledgements/close", null);
        closed.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterClose = await alice.PostAsync($"/api/v1/channels/{channelId}/messages/{messageId}/acknowledgements", null);
        afterClose.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = factory.CreateMigratorDbContext();
        var actions = await db.AuditEvents.AsNoTracking()
            .Where(x => x.EntityId == messageId.ToString() || x.EntityId == channelId.ToString())
            .Select(x => x.Action)
            .ToListAsync();
        actions.Should().Contain(AuditActions.AnnouncementCreated);
        actions.Should().Contain(AuditActions.AnnouncementPublished);
        actions.Should().Contain(AuditActions.AnnouncementEdited);
        actions.Should().Contain(AuditActions.AnnouncementClosed);

        var events = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.Payload.Contains(messageId.ToString()))
            .Select(x => x.Type)
            .ToListAsync();
        events.Should().Contain(AnnouncementEvents.Published);
        events.Should().Contain(AnnouncementEvents.Acknowledged);
    }

    private static async Task<Guid> CreateAnnouncementChannelAsync(HttpClient demo)
    {
        var name = $"anuncio-{Guid.NewGuid():N}"[..24];
        var created = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name, type = "Announcement" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var channel = await created.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions);
        return channel!.Id;
    }

    private sealed record ChannelDto(Guid Id);
    private sealed record MessageDto(Guid Id, AnnouncementDto? Announcement);
    private sealed record AnnouncementDto(bool RequiresAcknowledgement, int AcknowledgementCount, bool AcknowledgedByMe);
    private sealed record PendingDto(Guid MessageId, Guid ChannelId);
    private sealed record UnreadDto(Guid ChannelId, int PendingAnnouncementCount);
    private sealed record ReportDto(int Count, ReportItemDto[] Items);
    private sealed record ReportItemDto(Guid UserId, string DisplayName);
}
