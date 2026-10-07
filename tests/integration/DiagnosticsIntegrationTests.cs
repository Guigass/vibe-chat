using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class DiagnosticsIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid WelcomeId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task Member_is_forbidden_feature_off_is_skipped_and_workspace_evidence_is_masked()
    {
        using var alice = Client("alice");
        (await alice.GetAsync(Preflight)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var demo = Client("demo");
        var response = await demo.GetAsync(Preflight);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("smtp-test-password42");
        json.Should().NotContain("sk-test-secret-key99");
        json.Should().NotContain("Bem-vindo ao VibeChat Demo.");
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("audience").GetString().Should().Be("workspace");
        var checks = doc.RootElement.GetProperty("checks").EnumerateArray().ToList();
        checks.Should().Contain(check => check.GetProperty("code").GetString() == "email.configured" && check.GetProperty("status").GetString() == "Skipped");
        checks.Should().OnlyContain(check => check.GetProperty("evidence").GetProperty("endpoint").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Auditor_can_read_preflight_but_cannot_bundle_or_repair()
    {
        await WithRoleAsync(SeedData.AliceUserId, Role.Auditor, async () =>
        {
            using var alice = Client("alice");
            (await alice.GetAsync(Preflight)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostAsync(alice, $"{Root}/diagnostics/bundles", new { windowMinutes = 60 }, Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await PostAsync(alice, $"{Root}/diagnostics/repairs", new { action = "search.reindex", dryRun = true }, Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task Bundle_is_sanitized_idempotent_limited_and_invisible_across_tenants()
    {
        using var demo = Client("demo");
        var email = await PostAsync(demo, $"{Root}/diagnostics/probes/email", new { }, null);
        email.StatusCode.Should().Be(HttpStatusCode.OK);
        (await email.Content.ReadAsStringAsync()).Should().Contain("Skipped");

        var storage = await PostAsync(demo, $"{Root}/diagnostics/probes/storage", new { }, null);
        storage.StatusCode.Should().Be(HttpStatusCode.OK);
        var storageJson = await storage.Content.ReadAsStringAsync();
        storageJson.Should().Contain("\"cleaned\":true");

        (await PostAsync(demo, $"{Root}/diagnostics/probes/dns", new { }, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var key = Guid.NewGuid().ToString("N");
        var created = await PostAsync(demo, $"{Root}/diagnostics/bundles", new { windowMinutes = 60 }, key);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var bundle = await created.Content.ReadFromJsonAsync<BundleDto>(Json);
        var replay = await PostAsync(demo, $"{Root}/diagnostics/bundles", new { windowMinutes = 60 }, key);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = await replay.Content.ReadFromJsonAsync<BundleDto>(Json);
        replayed!.Id.Should().Be(bundle!.Id);
        replayed.Idempotent.Should().BeTrue();
        (await PostAsync(demo, $"{Root}/diagnostics/bundles", new { windowMinutes = 15 }, key)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var manifest = await demo.GetStringAsync($"{Root}/diagnostics/bundles/{bundle.Id}");
        manifest.Should().Contain(SupportBundleSchema.Format);
        manifest.Should().Contain(bundle.Checksum);
        manifest.Should().NotContain("smtp-test-password42");
        manifest.Should().NotContain("alice@vibechat.local");
        manifest.Should().NotContain("Bem-vindo ao VibeChat Demo.");
        manifest.Should().NotContain("Password");
        SupportBundleComposer.TryValidate(manifest, out var error).Should().BeTrue(error);

        (await demo.GetAsync($"{Root}/diagnostics/bundles/{bundle.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await demo.GetAsync($"{Root}/diagnostics/bundles/{bundle.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await demo.GetAsync($"{Root}/diagnostics/bundles/{bundle.Id}")).StatusCode.Should().Be(HttpStatusCode.Gone);

        var foreignId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.SupportBundles.Add(new SupportBundleRecord
            {
                Id = foreignId,
                TenantId = new TenantId(Guid.NewGuid()),
                WorkspaceId = SeedData.DemoWorkspaceId,
                RequestedBy = SeedData.DemoUserId,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                RequestHash = "abc",
                Status = "ready",
                ManifestJson = "{}",
                Checksum = "sha256:abc",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                CreatedAt = DateTimeOffset.UtcNow,
                CorrelationId = "none"
            });
            await db.SaveChangesAsync();
        }

        (await demo.GetAsync($"{Root}/diagnostics/bundles/{foreignId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var set = conn.CreateCommand())
        {
            set.Transaction = tx;
            set.CommandText = "SELECT set_config('app.tenant_id', @tenant, true)";
            set.Parameters.AddWithValue("tenant", SeedData.DemoTenantId.Value.ToString());
            await set.ExecuteNonQueryAsync();
        }

        await using var count = conn.CreateCommand();
        count.Transaction = tx;
        count.CommandText = """SELECT COUNT(*) FROM support.bundles WHERE "Id" = @id""";
        count.Parameters.AddWithValue("id", foreignId);
        ((long)(await count.ExecuteScalarAsync() ?? 0L)).Should().Be(0);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.SupportBundles.IgnoreQueryFilters().SingleAsync(x => x.Id == bundle.Id);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            row.DownloadCount = 0;
            await db.SaveChangesAsync();
        }

        (await demo.GetAsync($"{Root}/diagnostics/bundles/{bundle.Id}")).StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Dry_run_does_not_write_and_repair_rejects_an_arbitrary_target()
    {
        using var demo = Client("demo");
        (await PostAsync(demo, $"{Root}/diagnostics/repairs", new { action = "shell", target = "messages", dryRun = true }, Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(demo, $"{Root}/diagnostics/repairs", new { action = "search.reindex", target = "messages", dryRun = true }, Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(demo, $"{Root}/diagnostics/repairs", new { action = "search.reindex", dryRun = false, confirm = false }, Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var before = await MessageXminAsync();
        var dry = await PostAsync(demo, $"{Root}/diagnostics/repairs", new { action = "search.reindex", dryRun = true }, Guid.NewGuid().ToString("N"));
        dry.StatusCode.Should().Be(HttpStatusCode.OK);
        var dryJob = await dry.Content.ReadFromJsonAsync<RepairDto>(Json);
        dryJob!.Written.Should().Be(0);
        dryJob.Status.Should().Be(RepairStatus.Completed);
        (await MessageXminAsync()).Should().Be(before);

        var running = await PostAsync(demo, $"{Root}/diagnostics/repairs", new { action = "search.reindex", dryRun = false, confirm = true }, Guid.NewGuid().ToString("N"));
        running.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var job = await running.Content.ReadFromJsonAsync<RepairDto>(Json);
        (await demo.PostAsync($"{Root}/diagnostics/repairs/{job!.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await demo.PostAsync($"{Root}/diagnostics/repairs/{job.Id}/apply", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MessageXminAsync()).Should().Be(before);

        var second = await PostAsync(demo, $"{Root}/diagnostics/repairs", new { action = "search.reindex", dryRun = false, confirm = true }, Guid.NewGuid().ToString("N"));
        var applyJob = await second.Content.ReadFromJsonAsync<RepairDto>(Json);
        var applied = await demo.PostAsync($"{Root}/diagnostics/repairs/{applyJob!.Id}/apply", null);
        applied.StatusCode.Should().Be(HttpStatusCode.OK);
        (await applied.Content.ReadFromJsonAsync<RepairDto>(Json))!.Written.Should().BeGreaterThan(0);
        (await MessageXminAsync()).Should().NotBe(before);

        await using var db = factory.CreateMigratorDbContext();
        (await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.Id == new MessageId(WelcomeId))).Body.Should().Be("Bem-vindo ao VibeChat Demo.");
    }

    private async Task WithRoleAsync(UserId userId, Role role, Func<Task> action)
    {
        await using var db = factory.CreateMigratorDbContext();
        var member = await db.WorkspaceMembers.IgnoreQueryFilters().SingleAsync(x => x.UserId == userId && x.WorkspaceId == SeedData.DemoWorkspaceId);
        var previous = member.Role;
        member.Role = role;
        await db.SaveChangesAsync();
        try
        {
            await action();
        }
        finally
        {
            db.ChangeTracker.Clear();
            var again = await db.WorkspaceMembers.IgnoreQueryFilters().SingleAsync(x => x.UserId == userId && x.WorkspaceId == SeedData.DemoWorkspaceId);
            again.Role = previous;
            await db.SaveChangesAsync();
        }
    }

    private async Task<string> MessageXminAsync()
    {
        await using var conn = new NpgsqlConnection(factory.MigratorDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT xmin::text FROM messaging.messages WHERE "Id" = @id""";
        cmd.Parameters.AddWithValue("id", WelcomeId);
        return (string)(await cmd.ExecuteScalarAsync() ?? "");
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return client.SendAsync(request);
    }

    private static string Root => $"/api/v1/workspaces/{WorkspaceId}";
    private static string Preflight => $"{Root}/diagnostics";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class BundleDto
    {
        public Guid Id { get; set; }
        public string Checksum { get; set; } = "";
        public bool Idempotent { get; set; }
    }

    private sealed class RepairDto
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public int Written { get; set; }
    }
}
