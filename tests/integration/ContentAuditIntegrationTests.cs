using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ContentAuditIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Default_snapshots_deleted_body_and_keeps_it_after_the_row_is_purged()
    {
        using var demo = Client("demo");
        (await demo.GetAsync("/health")).EnsureSuccessStatusCode();
        await ResetAsync();
        try
        {
            var settings = await demo.GetAsync($"/api/v1/admin/settings?workspaceId={SeedData.DemoWorkspaceId.Value}");
            settings.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ReadBool(settings, "contentAudit", "enabled")).Should().BeTrue();

            var channelId = await CreateChannelAsync(demo);
            var messageId = Guid.NewGuid();
            var body = $"audit-snapshot-{messageId:N}";
            await SendAsync(demo, channelId, messageId, body);
            (await demo.DeleteAsync($"/api/v1/channels/{channelId}/messages/{messageId}"))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            var admin = await demo.GetAsync($"/api/v1/admin/conversations/{channelId}/messages?limit=100");
            admin.StatusCode.Should().Be(HttpStatusCode.OK);
            var row = (await admin.Content.ReadFromJsonAsync<AdminMessages>(Json))!
                .Items.Single(x => x.Id == messageId);
            row.Body.Should().Be(body);
            row.BodyRedacted.Should().BeFalse();

            await using var db = factory.CreateMigratorDbContext();
            var audit = await db.AuditEvents.IgnoreQueryFilters()
                .SingleAsync(x => x.Action == AuditActions.MessageDelete && x.EntityId == messageId.ToString());
            using (var metadata = JsonDocument.Parse(audit.MetadataJson))
            {
                metadata.RootElement.GetProperty("body").GetString().Should().Be(body);
                metadata.RootElement.GetProperty("authorId").GetGuid().Should().Be(SeedData.DemoUserId.Value);
            }

            var message = await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.Id == new MessageId(messageId));
            db.Messages.Remove(message);
            await db.SaveChangesAsync();

            var surviving = await db.AuditEvents.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == audit.Id);
            using var still = JsonDocument.Parse(surviving.MetadataJson);
            still.RootElement.GetProperty("body").GetString().Should().Be(body);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Confidential_mode_hides_privileged_body_and_gates_e2ee()
    {
        using var demo = Client("demo");
        using var bob = Client("bob");
        (await demo.GetAsync("/health")).EnsureSuccessStatusCode();
        await ResetAsync();
        var messageId = Guid.NewGuid();
        var body = $"confidential-{messageId:N}";
        try
        {
            var channelId = await CreateChannelAsync(demo);
            await SendAsync(demo, channelId, messageId, body);
            var blocked = await demo.PostAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
                new { name = $"e2ee-{messageId:N}", type = "ConfidentialE2EE" });
            blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadError(blocked)).Should().Be(ContentAuditRules.BlocksE2ee);

            var missingConfirm = await PutAudit(demo, false, false);
            missingConfirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadError(missingConfirm)).Should().Be(ContentAuditRules.ConfirmationRequired);

            (await PutAudit(demo, false, true)).StatusCode.Should().Be(HttpStatusCode.OK);

            await using (var unchanged = factory.CreateMigratorDbContext())
            {
                var stored = await unchanged.Messages.IgnoreQueryFilters().SingleAsync(x => x.Id == new MessageId(messageId));
                stored.Body.Should().Be(body);
            }

            var member = await demo.GetFromJsonAsync<ChannelMessages>(
                $"/api/v1/channels/{channelId}/messages?after=0&limit=100", Json);
            member!.Messages.Single(x => x.Id == messageId).Body.Should().Be(body);

            var admin = await demo.GetFromJsonAsync<AdminMessages>(
                $"/api/v1/admin/conversations/{channelId}/messages?limit=100", Json);
            var redacted = admin!.Items.Single(x => x.Id == messageId);
            redacted.Body.Should().BeEmpty();
            redacted.BodyRedacted.Should().BeTrue();
            redacted.Sequence.Should().BeGreaterThan(0);

            (await demo.PutAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.BobUserId.Value}/role",
                new { role = "Auditor" })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await bob.GetAsync($"/api/v1/admin/settings?workspaceId={SeedData.DemoWorkspaceId.Value}"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await PutAudit(bob, true, false)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var export = await demo.GetAsync($"/api/v1/admin/workspaces/{SeedData.DemoWorkspaceId.Value}/export");
            export.StatusCode.Should().Be(HttpStatusCode.OK);
            await using var zipStream = new MemoryStream(await export.Content.ReadAsByteArrayAsync());
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
            await using var messagesStream = zip.GetEntry("messages.json")!.Open();
            using var messages = await JsonDocument.ParseAsync(messagesStream);
            var exported = messages.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == messageId);
            exported.GetProperty("body").GetString().Should().BeEmpty();
            exported.GetProperty("bodyRedacted").GetBoolean().Should().BeTrue();

            (await demo.DeleteAsync($"/api/v1/channels/{channelId}/messages/{messageId}"))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            await using var db = factory.CreateMigratorDbContext();
            var audit = await db.AuditEvents.IgnoreQueryFilters()
                .SingleAsync(x => x.Action == AuditActions.MessageDelete && x.EntityId == messageId.ToString());
            audit.MetadataJson.Should().NotContain(body);
            using var metadata = JsonDocument.Parse(audit.MetadataJson);
            metadata.RootElement.TryGetProperty("body", out _).Should().BeFalse();
            metadata.RootElement.GetProperty("channelId").GetGuid().Should().Be(channelId);

            var unavailable = await demo.PostAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
                new { name = $"e2ee-off-{messageId:N}", type = "ConfidentialE2EE" });
            unavailable.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadError(unavailable)).Should().Be(ContentAuditRules.E2eeUnavailable);

            (await PutAudit(demo, true, false)).StatusCode.Should().Be(HttpStatusCode.OK);
            var restored = await demo.GetFromJsonAsync<AdminMessages>(
                $"/api/v1/admin/conversations/{channelId}/messages?limit=100", Json);
            restored!.Items.Single(x => x.Id == messageId).Body.Should().Be(body);
        }
        finally
        {
            using var restore = Client("demo");
            await restore.PutAsJsonAsync(
                $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.BobUserId.Value}/role",
                new { role = "Member" });
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Active_hold_blocks_disable_and_another_tenant_is_untouched()
    {
        using var demo = Client("demo");
        (await demo.GetAsync("/health")).EnsureSuccessStatusCode();
        await ResetAsync();
        var holdId = Guid.NewGuid();
        var foreignTenant = new TenantId(Guid.NewGuid());
        try
        {
            await using (var db = factory.CreateMigratorDbContext())
            {
                db.LegalHolds.Add(new LegalHold
                {
                    Id = holdId,
                    TenantId = SeedData.DemoTenantId,
                    WorkspaceId = SeedData.DemoWorkspaceId,
                    ScopeKind = LegalHoldScope.Workspace,
                    Reason = "preservar corpo",
                    Status = LegalHoldStatus.Active,
                    RequestedBy = SeedData.AliceUserId,
                    ApprovedBy = SeedData.BobUserId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ActivatedAt = DateTimeOffset.UtcNow,
                    IdempotencyKey = holdId.ToString("N")
                });
                db.ContentAuditSettings.Add(new ContentAuditSettings
                {
                    TenantId = foreignTenant,
                    Enabled = true,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var blocked = await PutAudit(demo, false, true);
            blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadError(blocked)).Should().Be(ContentAuditRules.BlockedByLegalHold);

            await using (var db = factory.CreateMigratorDbContext())
            {
                (await db.ContentAuditSettings.IgnoreQueryFilters().AnyAsync(x => x.TenantId == SeedData.DemoTenantId))
                    .Should().BeFalse();
                var audit = await db.AuditEvents.IgnoreQueryFilters()
                    .Where(x => x.TenantId == SeedData.DemoTenantId && x.Action == AuditActions.SettingsContentAuditChange)
                    .OrderByDescending(x => x.OccurredAt)
                    .FirstAsync();
                audit.MetadataJson.Should().Contain(ContentAuditRules.BlockedByLegalHold);
                audit.MetadataJson.Should().NotContain("body");
                (await db.ContentAuditSettings.IgnoreQueryFilters().SingleAsync(x => x.TenantId == foreignTenant))
                    .Enabled.Should().BeTrue();

                db.LegalHolds.Remove(await db.LegalHolds.IgnoreQueryFilters().SingleAsync(x => x.Id == holdId));
                await db.SaveChangesAsync();
            }

            (await PutAudit(demo, false, true)).StatusCode.Should().Be(HttpStatusCode.OK);
            await using var after = factory.CreateMigratorDbContext();
            (await after.ContentAuditSettings.IgnoreQueryFilters().SingleAsync(x => x.TenantId == SeedData.DemoTenantId))
                .Enabled.Should().BeFalse();
            (await after.ContentAuditSettings.IgnoreQueryFilters().SingleAsync(x => x.TenantId == foreignTenant))
                .Enabled.Should().BeTrue();
            after.ContentAuditSettings.Remove(
                await after.ContentAuditSettings.IgnoreQueryFilters().SingleAsync(x => x.TenantId == foreignTenant));
            await after.SaveChangesAsync();
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var hold = await db.LegalHolds.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == holdId);
            if (hold is not null)
            {
                db.LegalHolds.Remove(hold);
            }

            var foreign = await db.ContentAuditSettings.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == foreignTenant);
            if (foreign is not null)
            {
                db.ContentAuditSettings.Remove(foreign);
            }

            await db.SaveChangesAsync();
            await ResetAsync();
        }
    }

    private async Task ResetAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.ContentAuditSettings.IgnoreQueryFilters()
            .Where(x => x.TenantId == SeedData.DemoTenantId)
            .ToListAsync();
        db.ContentAuditSettings.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static Task<HttpResponseMessage> PutAudit(HttpClient client, bool enabled, bool confirmDisable) =>
        client.PutAsJsonAsync("/api/v1/admin/settings", new
        {
            workspaceId = SeedData.DemoWorkspaceId.Value,
            contentAudit = new { enabled, confirmDisable }
        });

    private static async Task<Guid> CreateChannelAsync(HttpClient client)
    {
        var name = $"audit-{Guid.NewGuid():N}"[..24];
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name, type = "Public" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task SendAsync(HttpClient client, Guid channelId, Guid messageId, string body)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    private static async Task<string?> ReadError(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("error").GetString();
    }

    private static async Task<bool> ReadBool(HttpResponseMessage response, string objectName, string property)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty(objectName).GetProperty(property).GetBoolean();
    }

    private sealed record ChannelMessages(ChannelMessage[] Messages);
    private sealed record ChannelMessage(Guid Id, string Body);
    private sealed record AdminMessages(AdminMessage[] Items);
    private sealed record AdminMessage(Guid Id, string Body, bool BodyRedacted, long Sequence);
}
