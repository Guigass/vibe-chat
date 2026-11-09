using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Messaging;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class CanvasIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Disabled_flag_does_not_open_a_document()
    {
        using var host = CanvasHost(enabled: false);
        using var alice = Client(host, "alice");
        var page = await CreatePageAsync(alice, "off");
        var response = await alice.GetAsync(Canvas(page.Id));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CanvasRealtimeDisabled");
        await using var db = factory.CreateMigratorDbContext();
        (await db.CanvasDocuments.IgnoreQueryFilters().AnyAsync(row => row.PageId == page.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Two_clients_converge_snapshot_restore_and_export()
    {
        using var host = CanvasHost(enabled: true);
        using var alice = Client(host, "alice");
        using var demo = Client(host, "demo");
        var page = await CreatePageAsync(alice, "Olá");
        var state = await alice.GetFromJsonAsync<StateDto>(Canvas(page.Id), JsonOptions);
        state!.Body.Should().Be("Olá");
        state.Epoch.Should().Be(1);

        var origin = Convert.FromBase64String(state.StateBase64);
        var left = Convert.ToBase64String(CanvasCrdt.Insert(origin, 1001, (uint)"Olá".Length, " Alice"));
        var right = Convert.ToBase64String(CanvasCrdt.Insert(origin, 1002, 0, "Bob "));
        var first = await PostUpdateAsync(alice, page.Id, state.Epoch, left, "left");
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var replay = await PostUpdateAsync(alice, page.Id, state.Epoch, left, "left");
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<WriteDto>(JsonOptions))!.Replayed.Should().BeTrue();

        var second = await PostUpdateAsync(demo, page.Id, state.Epoch, right, "right");
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var merged = (await second.Content.ReadFromJsonAsync<WriteDto>(JsonOptions))!;
        merged.Body.Should().Contain("Alice").And.Contain("Bob").And.Contain("Olá");

        var stale = await PostUpdateAsync(alice, page.Id, epoch: 99, left, "stale");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Contain("CanvasEpochMismatch");

        var snapshot = await demo.PostAsync($"{Canvas(page.Id)}/snapshots", null);
        snapshot.StatusCode.Should().Be(HttpStatusCode.OK, await snapshot.Content.ReadAsStringAsync());
        var saved = (await snapshot.Content.ReadFromJsonAsync<SnapshotDto>(JsonOptions))!;

        await using (var db = factory.CreateMigratorDbContext())
        {
            var document = await db.CanvasDocuments.IgnoreQueryFilters().SingleAsync(row => row.PageId == page.Id);
            document.CompactedThroughSeq.Should().BeGreaterThan(0);
        }

        var resync = await alice.GetAsync($"{Canvas(page.Id)}/updates?afterSeq=0&epoch={merged.Epoch}");
        resync.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resync.Content.ReadAsStringAsync()).Should().Contain("CanvasResync");

        var restored = await demo.PostAsJsonAsync($"{Canvas(page.Id)}/restore", new { snapshotId = saved.Id });
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        var after = (await restored.Content.ReadFromJsonAsync<WriteDto>(JsonOptions))!;
        after.Epoch.Should().BeGreaterThan(merged.Epoch);
        after.Body.Should().Be(merged.Body);

        var oldEpoch = await PostUpdateAsync(alice, page.Id, merged.Epoch, left, "after-restore");
        oldEpoch.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var exported = await alice.GetFromJsonAsync<ExportDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}/export", JsonOptions);
        exported!.Canvas.Should().NotBeNull();
        exported.Canvas!.SnapshotIds.Should().Contain(saved.Id);
        exported.Page.Body.Should().Be(merged.Body);
        exported.Page.References.Should().BeEmpty();

        var title = await alice.PatchAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}",
            new { version = exported.Page.Version, title = "Título novo", body = "não pode apagar o canvas" });
        title.StatusCode.Should().Be(HttpStatusCode.OK, await title.Content.ReadAsStringAsync());
        var titled = (await title.Content.ReadFromJsonAsync<PageDto>(JsonOptions))!;
        titled.Title.Should().Be("Título novo");
        titled.Body.Should().Be(merged.Body);
    }

    [Fact]
    public async Task Unsafe_update_is_rejected_and_retention_respects_hold()
    {
        using var host = CanvasHost(enabled: true);
        using var alice = Client(host, "alice");
        using var demo = Client(host, "demo");
        var page = await CreatePageAsync(alice, "limpo");
        var state = await alice.GetFromJsonAsync<StateDto>(Canvas(page.Id), JsonOptions);
        var origin = Convert.FromBase64String(state!.StateBase64);
        var bad = Convert.ToBase64String(CanvasCrdt.Insert(origin, 1003, (uint)"limpo".Length, "<script>"));
        var rejected = await PostUpdateAsync(alice, page.Id, state.Epoch, bad, "bad");
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("UnsafeKnowledgeBody");

        var kept = await CreatePageAsync(demo, "preso");
        await demo.GetAsync(Canvas(kept.Id));
        var deleted = await demo.DeleteAsync($"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{kept.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = factory.CreateMigratorDbContext();
        db.LegalHolds.Add(new LegalHold
        {
            Id = Guid.NewGuid(),
            TenantId = SeedData.DemoTenantId,
            WorkspaceId = SeedData.DemoWorkspaceId,
            ScopeKind = LegalHoldScope.Workspace,
            Reason = "preserva canvas",
            Status = LegalHoldStatus.Active,
            RequestedBy = SeedData.DemoUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = $"hold-{Guid.NewGuid():N}",
        });
        await db.SaveChangesAsync();

        var removed = await KnowledgeRetention.PurgeDeletedAsync(
            db, SeedData.DemoTenantId, DateTimeOffset.UtcNow.AddMinutes(1), 50, DateTimeOffset.UtcNow, CancellationToken.None);
        removed.Should().BeGreaterThan(0);
        await db.SaveChangesAsync();
        (await db.CanvasDocuments.IgnoreQueryFilters().AnyAsync(row => row.PageId == kept.Id)).Should().BeTrue();

        var hold = await db.LegalHolds.IgnoreQueryFilters()
            .Where(row => row.TenantId == SeedData.DemoTenantId && row.Reason == "preserva canvas")
            .SingleAsync();
        hold.Status = LegalHoldStatus.Released;
        await db.SaveChangesAsync();
        var stillHeld = await db.LegalHolds.IgnoreQueryFilters().AnyAsync(row =>
            row.TenantId == SeedData.DemoTenantId
            && row.WorkspaceId == SeedData.DemoWorkspaceId
            && row.Status == LegalHoldStatus.Active
            && (row.ExpiresAt == null || row.ExpiresAt > DateTimeOffset.UtcNow));
        if (!stillHeld)
        {
            db.ChangeTracker.Clear();
            await KnowledgeRetention.PurgeDeletedAsync(
                db, SeedData.DemoTenantId, DateTimeOffset.UtcNow.AddMinutes(1), 50, DateTimeOffset.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
            (await db.CanvasDocuments.IgnoreQueryFilters().AnyAsync(row => row.PageId == kept.Id)).Should().BeFalse();
        }
    }

    private WebApplicationFactory<Program> CanvasHost(bool enabled) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("Features:CanvasRealtime:Enabled", enabled ? "true" : "false"));

    private static HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static string Canvas(Guid pageId) =>
        $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{pageId}/canvas";

    private static async Task<HttpResponseMessage> PostUpdateAsync(
        HttpClient client,
        Guid pageId,
        int epoch,
        string updateBase64,
        string key) =>
        await client.PostAsJsonAsync($"{Canvas(pageId)}/updates", new
        {
            wireVersion = CanvasPolicies.WireVersion,
            idempotencyKey = key,
            epoch,
            updateBase64,
        });

    private static async Task<PageDto> CreatePageAsync(HttpClient client, string body)
    {
        var collection = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/collections",
            new { idempotencyKey = $"c-{Guid.NewGuid():N}", title = $"cv-{Guid.NewGuid():N}"[..16], scope = "workspace" });
        collection.EnsureSuccessStatusCode();
        var collectionId = (await collection.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;
        var page = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages",
            new { idempotencyKey = $"p-{Guid.NewGuid():N}", collectionId, title = "Canvas", body });
        page.StatusCode.Should().Be(HttpStatusCode.OK, await page.Content.ReadAsStringAsync());
        return (await page.Content.ReadFromJsonAsync<PageDto>(JsonOptions))!;
    }

    private sealed record IdDto(Guid Id);

    private sealed record PageDto(Guid Id, string Title, string Body, int Version, IReadOnlyList<object> References);

    private sealed record StateDto(int Epoch, string StateBase64, string Body);

    private sealed record WriteDto(int Epoch, long Seq, string Body, bool Replayed);

    private sealed record SnapshotDto(Guid Id);

    private sealed record ExportDto(PageDto Page, CanvasExportDto? Canvas);

    private sealed record CanvasExportDto(IReadOnlyList<Guid> SnapshotIds);
}
