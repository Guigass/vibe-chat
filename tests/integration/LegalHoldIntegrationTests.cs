using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class LegalHoldIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Hold_blocks_purge_until_release_and_export_keeps_checksum()
    {
        using var demo = Client("demo");
        using var alice = Client("alice");
        using var bob = Client("bob");
        var channel = await CreateChannelAsync(demo);
        var heldId = await SendAndDeleteAsync(alice, channel);
        var freeId = await SendAndDeleteAsync(alice, SeedData.DemoChannelId.Value);

        await using (var db = factory.CreateMigratorDbContext())
        {
            foreach (var id in new[] { heldId, freeId })
            {
                var message = await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.Id == new MessageId(id));
                message.DeletedAt = DateTimeOffset.UtcNow.AddDays(-40);
            }

            await db.SaveChangesAsync();
        }

        var retention = await demo.PutAsJsonAsync(
            "/api/v1/admin/settings",
            new { workspaceId = WorkspaceId, retention = new { enabled = true, retentionDays = 30 } });
        retention.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            (await PostAsync(demo, Holds(), new { scopeKind = "channel", scopeId = channel, reason = "matter held", confirmed = true, acknowledged = true }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            await GrantAsync(demo, SeedData.AliceUserId.Value, "hold");
            await GrantAsync(demo, SeedData.BobUserId.Value, "hold");
            await GrantAsync(demo, SeedData.AliceUserId.Value, "discovery");

            (await PostAsync(alice, Holds(), new { scopeKind = "channel", scopeId = channel, reason = "curto", confirmed = true, acknowledged = true }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var requested = await PostAsync(
                alice,
                Holds(),
                new { scopeKind = "channel", scopeId = channel, reason = "investigacao autorizada", confirmed = true, acknowledged = true });
            requested.StatusCode.Should().Be(HttpStatusCode.Created);
            var hold = (await requested.Content.ReadFromJsonAsync<HoldDto>(Json))!;
            hold.Status.Should().Be(LegalHoldStatus.Requested);

            (await PostAsync(alice, $"{Holds()}/{hold.Id}/approve", new { confirmed = true, acknowledged = true }))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);

            var approved = await PostAsync(bob, $"{Holds()}/{hold.Id}/approve", new { confirmed = true, acknowledged = true });
            approved.StatusCode.Should().Be(HttpStatusCode.OK);

            var processor = factory.Services.GetRequiredService<MessageRetentionPurgeProcessor>();
            await processor.ProcessBatchAsync(CancellationToken.None);

            await using (var db = factory.CreateMigratorDbContext())
            {
                (await db.Messages.IgnoreQueryFilters().AnyAsync(x => x.Id == new MessageId(heldId))).Should().BeTrue();
                (await db.Messages.IgnoreQueryFilters().AnyAsync(x => x.Id == new MessageId(freeId))).Should().BeFalse();
                (await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.TenantId == SeedData.DemoTenantId && x.Action == AuditActions.MessagePreserve))
                    .Should().BeTrue();
            }

            var release = await PostAsync(
                bob,
                $"{Holds()}/{hold.Id}/release",
                new { reason = "investigacao encerrada", confirmed = true, acknowledged = true });
            release.StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostAsync(alice, $"{Holds()}/{hold.Id}/release/approve", new { confirmed = true, acknowledged = true }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            await using (var db = factory.CreateMigratorDbContext())
            {
                (await db.Messages.IgnoreQueryFilters().AnyAsync(x => x.Id == new MessageId(heldId))).Should().BeTrue();
            }

            (await PostAsync(bob, Cases(), new { title = "caso", reason = "busca autorizada", channelIds = new[] { channel } }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var created = await PostAsync(
                alice,
                Cases(),
                new { title = "caso interno", reason = "busca autorizada", channelIds = new[] { channel } });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var discoveryCase = (await created.Content.ReadFromJsonAsync<CaseDto>(Json))!;
            var exported = await PostAsync(
                alice,
                $"{Cases()}/{discoveryCase.Id}/exports",
                new { confirmed = true, acknowledged = true });
            exported.StatusCode.Should().Be(HttpStatusCode.Created);
            var export = (await exported.Content.ReadFromJsonAsync<ExportDto>(Json))!;
            export.Progress.Should().Be(100);
            export.ItemCount.Should().BeGreaterThan(0);
            export.Checksum.Should().Be(Sha(export.ManifestJson));
            export.ManifestJson.Should().Contain(heldId.ToString());

            await processor.ProcessBatchAsync(CancellationToken.None);
            await using (var db = factory.CreateMigratorDbContext())
            {
                (await db.Messages.IgnoreQueryFilters().AnyAsync(x => x.Id == new MessageId(heldId))).Should().BeFalse();
            }

            var reread = await alice.GetFromJsonAsync<ExportDto>(
                $"{Cases()}/{discoveryCase.Id}/exports/{export.Id}",
                Json);
            reread!.ManifestJson.Should().Be(export.ManifestJson);

            await using (var db = factory.CreateMigratorDbContext())
            {
                var act = async () => await db.Database.ExecuteSqlRawAsync(
                    """UPDATE compliance.custody_events SET "Reason" = 'tamper' WHERE "WorkspaceId" = {0}""",
                    WorkspaceId);
                await act.Should().ThrowAsync<Exception>();
            }
        }
        finally
        {
            await demo.PutAsJsonAsync(
                "/api/v1/admin/settings",
                new { workspaceId = WorkspaceId, retention = new { enabled = false, retentionDays = 30 } });
        }
    }

    private static async Task GrantAsync(HttpClient demo, Guid userId, string capability)
    {
        var response = await PostAsync(
            demo,
            $"/api/v1/workspaces/{WorkspaceId}/compliance/grants",
            new { userId, capability });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<Guid> CreateChannelAsync(HttpClient demo)
    {
        var response = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = $"hold-{Guid.NewGuid():N}"[..20], type = "Public" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var channel = await response.Content.ReadFromJsonAsync<IdDto>(Json);
        return channel!.Id;
    }

    private static async Task<Guid> SendAndDeleteAsync(HttpClient client, Guid channelId)
    {
        var messageId = Guid.NewGuid();
        var send = await client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", $"hold-{messageId:N}", null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var delete = await client.DeleteAsync($"/api/v1/channels/{channelId}/messages/{messageId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return messageId;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static string Holds() => $"/api/v1/workspaces/{WorkspaceId}/compliance/holds";
    private static string Cases() => $"/api/v1/workspaces/{WorkspaceId}/compliance/cases";

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record HoldDto(Guid Id, string Status);
    private sealed record CaseDto(Guid Id);
    private sealed record ExportDto(Guid Id, int Progress, string Checksum, string ManifestJson, int ItemCount);
    private sealed record IdDto(Guid Id);
}
