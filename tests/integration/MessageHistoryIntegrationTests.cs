using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-114 — versão a cada edição, move idempotente e tombstone sem vazar o destino.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class MessageHistoryIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Edit_records_each_previous_body_and_export_includes_it()
    {
        using var alice = Client("alice");
        using var demo = Client("demo");
        var messageId = Guid.NewGuid();
        var original = $"history-v0-{messageId:N}";
        var edited = $"history-v1-{messageId:N}";

        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new { messageId, idempotencyKey = $"idem-{messageId:N}", body = original });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var first = await alice.PutAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}",
            new { body = edited });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await alice.PutAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}",
            new { body = $"history-v2-{messageId:N}" });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await alice.GetFromJsonAsync<HistoryDto>(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}/history",
            Json);
        history!.Versions.Select(v => v.Body).Should().Equal(original, edited);
        history.CurrentBody.Should().Be($"history-v2-{messageId:N}");

        var deleted = await alice.DeleteAsync($"/api/v1/channels/{ChannelId}/messages/{messageId}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using (var db = factory.CreateMigratorDbContext())
        {
            (await db.MessageVersions.IgnoreQueryFilters().CountAsync(x => x.MessageId == new MessageId(messageId)))
                .Should().Be(2);
        }

        var export = await demo.GetAsync($"/api/v1/admin/workspaces/{WorkspaceId}/export");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var zipStream = new MemoryStream(await export.Content.ReadAsByteArrayAsync());
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        await using var entry = zip.GetEntry("message-versions.json")!.Open();
        using var doc = await JsonDocument.ParseAsync(entry);
        doc.RootElement.EnumerateArray()
            .Where(row => row.GetProperty("messageId").GetGuid() == messageId)
            .Select(row => row.GetProperty("body").GetString())
            .Should().Equal(original, edited);
    }

    [Fact]
    public async Task Move_is_idempotent_and_hides_destination_from_non_members()
    {
        using var demo = Client("demo");
        using var bob = Client("bob");
        using var alice = Client("alice");
        var messageId = Guid.NewGuid();
        var body = $"move-me-{messageId:N}";
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new { messageId, idempotencyKey = $"idem-move-{messageId:N}", body });
        sent.EnsureSuccessStatusCode();
        var created = await sent.Content.ReadFromJsonAsync<SentDto>(Json);

        var forbidden = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}/move",
            new { idempotencyKey = $"nope-{messageId:N}", targetChannelId = Guid.NewGuid() });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var privateId = await CreateChannelAsync(demo, "Private");
        var key = $"move-{messageId:N}";
        var moved = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}/move",
            new { idempotencyKey = key, targetChannelId = privateId });
        moved.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await moved.Content.ReadFromJsonAsync<MoveDto>(Json);
        result!.DestinationChannelId.Should().Be(privateId);
        result.Tombstone.Should().BeTrue();
        result.DestinationSequence.Should().BeGreaterThan(0);

        var replay = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}/move",
            new { idempotencyKey = key, targetChannelId = privateId });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<MoveDto>(Json))!.DestinationMessageId
            .Should().Be(result.DestinationMessageId);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var source = await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.Id == new MessageId(messageId));
            source.Sequence.Should().Be(created!.Sequence);
            source.Body.Should().Be(MessageHistoryPolicies.TombstoneBody);
            (await db.Messages.IgnoreQueryFilters().CountAsync(x => x.MovedFromMessageId == source.Id)).Should().Be(1);
        }

        var bobView = await bob.GetFromJsonAsync<LinksDto>(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}/move",
            Json);
        bobView!.Destination.Accessible.Should().BeFalse();
        bobView.Destination.MessageId.Should().BeNull();
        bobView.Destination.ChannelId.Should().BeNull();

        var demoView = await demo.GetFromJsonAsync<LinksDto>(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}/move",
            Json);
        demoView!.Destination.Accessible.Should().BeTrue();
        demoView.Destination.MessageId.Should().Be(result.DestinationMessageId);

        var list = await bob.GetFromJsonAsync<PageDto>(
            $"/api/v1/channels/{ChannelId}/messages?after={created!.Sequence - 1}&limit=20",
            Json);
        var row = list!.Messages.Single(m => m.Id == messageId);
        row.Body.Should().Be(MessageHistoryPolicies.TombstoneBody);
        row.MovedToAccessible.Should().BeFalse();
        row.MovedToMessageId.Should().BeNull();

        var cross = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}/move",
            new { idempotencyKey = $"cross-{messageId:N}", targetChannelId = Guid.NewGuid() });
        cross.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Flag_off_does_not_version_edits()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Messaging:History:Enabled", "false"));
        using var alice = disabled.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var messageId = Guid.NewGuid();
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new { messageId, idempotencyKey = $"off-{messageId:N}", body = "antes" });
        sent.EnsureSuccessStatusCode();
        var edit = await alice.PutAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages/{messageId}",
            new { body = "depois" });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await alice.GetAsync($"/api/v1/channels/{ChannelId}/messages/{messageId}/history");
        history.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var db = factory.CreateMigratorDbContext();
        (await db.MessageVersions.IgnoreQueryFilters().AnyAsync(x => x.MessageId == new MessageId(messageId)))
            .Should().BeFalse();
    }

    private async Task<Guid> CreateChannelAsync(HttpClient client, string type)
    {
        var create = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = $"hist-{Guid.NewGuid():N}"[..28], type });
        create.EnsureSuccessStatusCode();
        var channel = await create.Content.ReadFromJsonAsync<ChannelDto>(Json);
        return channel!.Id;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record HistoryDto(VersionDto[] Versions, string CurrentBody);
    private sealed record VersionDto(int Version, string Body);
    private sealed record SentDto(long Sequence);
    private sealed record MoveDto(Guid DestinationMessageId, Guid DestinationChannelId, long DestinationSequence, bool Tombstone);
    private sealed record LinksDto(LinkDto Destination);
    private sealed record LinkDto(bool Accessible, Guid? ChannelId, Guid? MessageId);
    private sealed record PageDto(RowDto[] Messages);
    private sealed record RowDto(Guid Id, string Body, bool MovedToAccessible, Guid? MovedToMessageId);
    private sealed record ChannelDto(Guid Id);
}
