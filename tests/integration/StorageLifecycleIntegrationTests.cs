using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-145 — quota, hold, GC, CDN, reconciliation and restore.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class StorageLifecycleIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Flag_off_hides_storage_routes()
    {
        using var client = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:StorageLifecycle:Enabled", "false")).CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var response = await client.GetAsync(UsagePath());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(StorageLifecycleErrors.Disabled);
    }

    [Fact]
    public async Task Reservation_hold_gc_cdn_reconcile_and_restore_follow_the_ledger()
    {
        using var demo = Client("demo");
        using var alice = Client("alice");
        try
        {
            (await SendAsync(demo, LimitsPath(), new { kind = StorageObjectKinds.Attachment, softLimit = 80, hardLimit = 100 }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var left = SendAsync(demo, ReservePath(), new { kind = StorageObjectKinds.Attachment, bytes = 60 });
            var right = SendAsync(demo, ReservePath(), new { kind = StorageObjectKinds.Attachment, bytes = 60 });
            var races = await Task.WhenAll(left, right);
            races.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
            races.Count(x => x.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
            (await races.First(x => x.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync())
                .Should().Contain(StorageLifecycleErrors.HardLimit);

            var usage = await demo.GetFromJsonAsync<JsonElement>(UsagePath());
            usage.GetProperty("kinds")[0].GetProperty("reservedBytes").GetInt64().Should().BeLessThanOrEqualTo(100);

            await ClearObjectsAsync();
            (await SendAsync(demo, LimitsPath(), new { kind = StorageObjectKinds.Attachment, softLimit = 1, hardLimit = 1 }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var blocked = await alice.PostAsJsonAsync(
                $"/api/v1/channels/{SeedData.DemoChannelId.Value}/attachments",
                new { fileName = "note.txt", contentType = "text/plain", sizeBytes = 20 });
            blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await blocked.Content.ReadAsStringAsync()).Should().Contain(StorageLifecycleErrors.HardLimit);

            (await SendAsync(demo, LimitsPath(), new { kind = StorageObjectKinds.Attachment, softLimit = 40, hardLimit = 1000 }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            await ClearObjectsAsync();

            var referenced = await RegisterAsync(demo, referenced: true, clean: true, bytes: 50, key: "tenants/secret-object-key");
            var orphan = await RegisterAsync(demo, referenced: false, clean: false, bytes: 9, key: "tenants/orphan-object-key");

            var held = Guid.NewGuid();
            await using (var db = factory.CreateMigratorDbContext())
            {
                db.LegalHolds.Add(new LegalHold
                {
                    Id = held,
                    TenantId = SeedData.DemoTenantId,
                    WorkspaceId = SeedData.DemoWorkspaceId,
                    ScopeKind = LegalHoldScope.Workspace,
                    Reason = "b145-hold",
                    Status = LegalHoldStatus.Active,
                    RequestedBy = SeedData.DemoUserId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    IdempotencyKey = held.ToString("N")
                });
                await db.SaveChangesAsync();
            }

            var purgeHeld = await SendAsync(demo, PurgePath(orphan), new { });
            purgeHeld.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await purgeHeld.Content.ReadAsStringAsync()).Should().Contain(StorageLifecycleErrors.Hold);

            var gcHeld = await ReadGcAsync(demo);
            gcHeld.GetProperty("deleted").GetArrayLength().Should().Be(0);

            await using (var db = factory.CreateMigratorDbContext())
            {
                var row = await db.LegalHolds.IgnoreQueryFilters().SingleAsync(x => x.Id == held);
                db.LegalHolds.Remove(row);
                await db.SaveChangesAsync();
            }

            var gcDry = await ReadGcAsync(demo);
            gcDry.GetProperty("candidates").EnumerateArray().Select(x => x.GetGuid()).Should().Contain(orphan);
            gcDry.GetProperty("candidates").EnumerateArray().Select(x => x.GetGuid()).Should().NotContain(referenced);
            gcDry.GetProperty("deleted").GetArrayLength().Should().Be(0);

            var gc = await SendAsync(demo, GcPath(), new { dryRun = false });
            gc.StatusCode.Should().Be(HttpStatusCode.OK);
            (await gc.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deleted").EnumerateArray()
                .Select(x => x.GetGuid()).Should().Contain(orphan).And.NotContain(referenced);

            await using (var db = factory.CreateMigratorDbContext())
            {
                var ledger = await db.StorageLedgers.IgnoreQueryFilters()
                    .SingleAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId && x.Kind == StorageObjectKinds.Attachment);
                ledger.UsedBytes = 1;
                await db.SaveChangesAsync();
            }

            var reconcile = await SendAsync(demo, ReconcilePath(), new { });
            reconcile.StatusCode.Should().Be(HttpStatusCode.OK);
            var corrected = await reconcile.Content.ReadFromJsonAsync<JsonElement>();
            corrected.GetProperty("corrected").EnumerateArray().Should().Contain(row =>
                row.GetProperty("kind").GetString() == StorageObjectKinds.Attachment
                && row.GetProperty("usedBytes").GetInt64() == 50);

            var cdn = await alice.GetFromJsonAsync<JsonElement>(CdnPath(referenced));
            var url = cdn.GetProperty("url").GetString();
            url.Should().NotBeNullOrWhiteSpace();
            url.Should().NotContain("tenants/secret-object-key");
            url!.Length.Should().BeGreaterThan(40);
            cdn.GetProperty("expiresAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);

            var redeem = await alice.GetAsync(url);
            redeem.StatusCode.Should().Be(HttpStatusCode.OK);
            (await redeem.Content.ReadAsStringAsync()).Should().NotContain("tenants/secret-object-key");

            var expired = StorageLifecycleRules.IssueCdnToken(
                SeedData.DemoTenantId.Value,
                referenced,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                "vibechat-lab-cdn-key");
            var stale = await alice.GetAsync($"/api/v1/storage/cdn?token={Uri.EscapeDataString(expired)}");
            stale.StatusCode.Should().Be(HttpStatusCode.Gone);

            (await SendAsync(demo, ArchivePath(referenced), new { })).StatusCode.Should().Be(HttpStatusCode.OK);
            await using (var db = factory.CreateMigratorDbContext())
            {
                var row = await db.StorageObjects.IgnoreQueryFilters().SingleAsync(x => x.Id == referenced);
                row.Referenced = false;
                row.PurgeAfter = DateTimeOffset.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
            }

            (await SendAsync(demo, PurgePath(referenced), new { })).StatusCode.Should().Be(HttpStatusCode.OK);
            var purged = await SendAsync(demo, PurgePath(referenced), new { });
            purged.StatusCode.Should().Be(HttpStatusCode.OK);

            await using (var db = factory.CreateMigratorDbContext())
            {
                db.StorageObjects.Add(new StorageObjectRecord
                {
                    Id = referenced,
                    TenantId = SeedData.DemoTenantId,
                    WorkspaceId = SeedData.DemoWorkspaceId,
                    Kind = StorageObjectKinds.Attachment,
                    State = StorageObjectStates.Hot,
                    Bytes = 50,
                    Version = 1,
                    Referenced = true,
                    Clean = true,
                    StorageKey = "tenants/secret-object-key",
                    AuthorId = SeedData.DemoUserId.Value,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var restore = await SendAsync(demo, RestorePath(), new { objectId = referenced });
            restore.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await restore.Content.ReadAsStringAsync()).Should().Contain(StorageLifecycleErrors.AlreadyPurged);
            await using (var db = factory.CreateMigratorDbContext())
            {
                (await db.StorageObjects.IgnoreQueryFilters().AnyAsync(x => x.Id == referenced)).Should().BeFalse();
                (await db.StorageTombstones.IgnoreQueryFilters().AnyAsync(x => x.ObjectId == referenced)).Should().BeTrue();
            }
        }
        finally
        {
            await ClearAsync();
        }
    }

    private async Task<Guid> RegisterAsync(HttpClient client, bool referenced, bool clean, long bytes, string key)
    {
        var response = await SendAsync(client, ObjectsPath(), new
        {
            kind = StorageObjectKinds.Attachment,
            bytes,
            referenced,
            clean,
            storageKey = key
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("objectId").GetGuid();
    }

    private async Task<JsonElement> ReadGcAsync(HttpClient client)
    {
        var response = await SendAsync(client, GcPath(), new { dryRun = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private async Task ClearObjectsAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.StorageObjects.IgnoreQueryFilters()
            .Where(x => x.TenantId == SeedData.DemoTenantId)
            .ToListAsync();
        db.StorageObjects.RemoveRange(rows);
        var ledgers = await db.StorageLedgers.IgnoreQueryFilters()
            .Where(x => x.TenantId == SeedData.DemoTenantId && x.Kind == StorageObjectKinds.Attachment)
            .ToListAsync();
        foreach (var ledger in ledgers)
        {
            ledger.ReservedBytes = 0;
            ledger.UsedBytes = 0;
        }

        await db.SaveChangesAsync();
    }

    private async Task ClearAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        db.StorageObjects.RemoveRange(await db.StorageObjects.IgnoreQueryFilters().Where(x => x.TenantId == SeedData.DemoTenantId).ToListAsync());
        db.StorageTombstones.RemoveRange(await db.StorageTombstones.IgnoreQueryFilters().Where(x => x.TenantId == SeedData.DemoTenantId).ToListAsync());
        db.StorageJobs.RemoveRange(await db.StorageJobs.IgnoreQueryFilters().Where(x => x.TenantId == SeedData.DemoTenantId).ToListAsync());
        db.StorageLedgers.RemoveRange(await db.StorageLedgers.IgnoreQueryFilters().Where(x => x.TenantId == SeedData.DemoTenantId).ToListAsync());
        var holds = await db.LegalHolds.IgnoreQueryFilters().Where(x => x.Reason == "b145-hold").ToListAsync();
        db.LegalHolds.RemoveRange(holds);
        await db.SaveChangesAsync();
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static string UsagePath() => $"/api/v1/workspaces/{WorkspaceId}/storage";

    private static string LimitsPath() => $"/api/v1/workspaces/{WorkspaceId}/storage/limits";

    private static string ReservePath() => $"/api/v1/workspaces/{WorkspaceId}/storage/reservations";

    private static string ObjectsPath() => $"/api/v1/workspaces/{WorkspaceId}/storage/objects";

    private static string ReconcilePath() => $"/api/v1/workspaces/{WorkspaceId}/storage/reconcile";

    private static string GcPath() => $"/api/v1/workspaces/{WorkspaceId}/storage/gc";

    private static string RestorePath() => $"/api/v1/workspaces/{WorkspaceId}/storage/restore";

    private static string ArchivePath(Guid objectId) => $"/api/v1/workspaces/{WorkspaceId}/storage/objects/{objectId}/archive";

    private static string PurgePath(Guid objectId) => $"/api/v1/workspaces/{WorkspaceId}/storage/objects/{objectId}/purge";

    private static string CdnPath(Guid objectId) => $"/api/v1/workspaces/{WorkspaceId}/storage/objects/{objectId}/cdn";
}
