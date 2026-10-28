using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-064 — confidential channels store ciphertext, hide history, and rotate recipients.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class E2eeIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string PublicJwk = """
        {"kty":"EC","crv":"P-256","x":"f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU","y":"x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0"}
        """;

    [Fact]
    public async Task Flag_off_keeps_the_channel_unavailable()
    {
        using var demo = Client(factory, "demo");
        await ResetAuditAsync();
        (await PutAudit(demo, false, true)).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            var blocked = await demo.PostAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
                new { name = "e2ee-off-" + Guid.NewGuid().ToString("N")[..8], type = "ConfidentialE2EE" });
            blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadError(blocked)).Should().Be(ContentAuditRules.E2eeUnavailable);
        }
        finally
        {
            await ResetAuditAsync();
        }
    }

    [Fact]
    public async Task Ciphertext_round_trip_hides_history_and_drops_removed_recipients()
    {
        using var enabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Messaging:E2ee:Enabled", "true"));
        using var demo = Client(enabled, "demo");
        using var bob = Client(enabled, "bob");
        await ResetAuditAsync();
        (await PutAudit(demo, false, true)).StatusCode.Should().Be(HttpStatusCode.OK);
        var secret = "secret-phrase-" + Guid.NewGuid().ToString("N");
        try
        {
            (await PutAudit(demo, true, false)).EnsureSuccessStatusCode();
            var audited = await demo.PostAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
                new { name = "e2ee-audit-" + Guid.NewGuid().ToString("N")[..8], type = "ConfidentialE2EE" });
            audited.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadError(audited)).Should().Be(ContentAuditRules.BlocksE2ee);
            (await PutAudit(demo, false, true)).EnsureSuccessStatusCode();

            var created = await demo.PostAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
                new { name = "e2ee-" + Guid.NewGuid().ToString("N")[..8], type = "ConfidentialE2EE" });
            created.EnsureSuccessStatusCode();
            var channelId = (await created.Content.ReadFromJsonAsync<ChannelBody>(Json))!.Id;
            var deviceId = Guid.NewGuid();
            (await demo.PostAsJsonAsync("/api/v1/e2ee/devices", new { deviceId, publicJwk = PublicJwk }))
                .EnsureSuccessStatusCode();

            var plain = await demo.PostAsJsonAsync(
                $"/api/v1/channels/{channelId}/messages",
                new { messageId = Guid.NewGuid(), idempotencyKey = "plain-" + Guid.NewGuid().ToString("N"), body = secret });
            plain.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadError(plain)).Should().Be(E2eeErrors.EnvelopeRequired);

            var state = await demo.GetFromJsonAsync<ChannelE2ee>($"/api/v1/channels/{channelId}/e2ee", Json);
            state!.Generation.Should().BeGreaterThan(0);
            state.Devices.Select(x => x.DeviceId).Should().Contain(deviceId);
            var envelope = Envelope(state.Generation, deviceId);
            envelope.Should().NotContain(secret);
            var messageId = Guid.NewGuid();
            var sent = await demo.PostAsJsonAsync(
                $"/api/v1/channels/{channelId}/messages",
                new { messageId, idempotencyKey = "seal-" + messageId.ToString("N"), body = envelope });
            sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

            await using (var db = factory.CreateMigratorDbContext())
            {
                var stored = await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.Id == new MessageId(messageId));
                stored.Body.Should().Be(envelope);
                stored.Body.Should().NotContain(secret);
                var outbox = await db.OutboxMessages.IgnoreQueryFilters()
                    .SingleAsync(x => x.Type == "MessageCreatedEvent" && x.Payload.Contains(messageId.ToString()));
                outbox.Payload.Should().NotContain(secret);
                outbox.Payload.Should().Contain("\"e2ee\":true");
                var audit = await db.AuditEvents.IgnoreQueryFilters()
                    .Where(x => x.Action == AuditActions.MessageSend && x.EntityId == messageId.ToString())
                    .SingleAsync();
                audit.MetadataJson.Should().NotContain(secret);
            }

            (await demo.PostAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/{channelId}/members",
                new { userId = SeedData.BobUserId.Value })).EnsureSuccessStatusCode();
            var bobPage = await bob.GetFromJsonAsync<MessagePage>(
                $"/api/v1/channels/{channelId}/messages?after=0&limit=100", Json);
            bobPage!.Messages.Should().NotContain(x => x.Id == messageId);

            var bobDevice = Guid.NewGuid();
            (await bob.PostAsJsonAsync("/api/v1/e2ee/devices", new { deviceId = bobDevice, publicJwk = PublicJwk }))
                .EnsureSuccessStatusCode();
            (await demo.DeleteAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels/{channelId}/members/{SeedData.BobUserId.Value}"))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            var afterRemoval = await demo.GetFromJsonAsync<ChannelE2ee>($"/api/v1/channels/{channelId}/e2ee", Json);
            afterRemoval!.Devices.Select(x => x.DeviceId).Should().NotContain(bobDevice);
            var future = Envelope(afterRemoval.Generation, deviceId);
            future.Should().NotContain(bobDevice.ToString());
            var futureId = Guid.NewGuid();
            (await demo.PostAsJsonAsync(
                $"/api/v1/channels/{channelId}/messages",
                new { messageId = futureId, idempotencyKey = "future-" + futureId.ToString("N"), body = future }))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
            (await bob.GetAsync($"/api/v1/channels/{channelId}/messages?after=0&limit=20"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await ResetAuditAsync();
        }
    }

    private async Task ResetAuditAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.ContentAuditSettings.IgnoreQueryFilters()
            .Where(x => x.TenantId == SeedData.DemoTenantId)
            .ToListAsync();
        db.ContentAuditSettings.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private static HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static Task<HttpResponseMessage> PutAudit(HttpClient client, bool enabled, bool confirmDisable) =>
        client.PutAsJsonAsync("/api/v1/admin/settings", new
        {
            workspaceId = SeedData.DemoWorkspaceId.Value,
            contentAudit = new { enabled, confirmDisable }
        });

    private static async Task<string?> ReadError(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("error").GetString();
    }

    private static string Envelope(int epoch, Guid device) =>
        $$"""{"v":1,"epoch":{{epoch}},"recipients":[{"kid":"{{device}}","enc":"AQIDBA","ct":"BAUGBw"}]}""";

    private sealed record ChannelBody(Guid Id);
    private sealed record ChannelE2ee(int Generation, E2eeDeviceRow[] Devices);
    private sealed record E2eeDeviceRow(Guid DeviceId);
    private sealed record MessagePage(MessageRow[] Messages);
    private sealed record MessageRow(Guid Id, string Body);
}
