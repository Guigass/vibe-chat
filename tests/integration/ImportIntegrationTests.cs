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
public sealed class ImportIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_cannot_import_and_dry_run_does_not_write_domain_state()
    {
        using var alice = Client("alice");
        var denied = await PostImportAsync(alice, Document("dry-run"));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var before = factory.CreateMigratorDbContext();
        var channelsBefore = await before.Channels.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId);
        var messagesBefore = await before.Messages.IgnoreQueryFilters().CountAsync(x => x.TenantId == SeedData.DemoTenantId);

        using var demo = Client("demo");
        var created = await PostImportAsync(demo, Document("dry-run"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var raw = await created.Content.ReadAsStringAsync();
        raw.Should().NotContain("raiz importada");
        raw.Should().NotContain("99999999-9999-9999-9999-999999999999");
        var job = JsonSerializer.Deserialize<ImportJobDto>(raw, Json);
        job!.Report.Messages.Should().Be(1);

        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/plan", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var after = factory.CreateMigratorDbContext();
        (await after.Channels.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId)).Should().Be(channelsBefore);
        (await after.Messages.IgnoreQueryFilters().CountAsync(x => x.TenantId == SeedData.DemoTenantId)).Should().Be(messagesBefore);
        var row = await after.ImportJobs.IgnoreQueryFilters().SingleAsync(x => x.Id == job.Id);
        row.TenantId.Should().Be(SeedData.DemoTenantId);
    }

    [Fact]
    public async Task Publish_preserves_author_time_and_thread_without_membership_and_retry_is_idempotent()
    {
        var name = $"imp{Guid.NewGuid():N}"[..16];
        using var demo = Client("demo");
        var created = await PostImportAsync(demo, Document(name, withThread: true, withFile: true));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var job = await created.Content.ReadFromJsonAsync<ImportJobDto>(Json);
        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job!.Id}/plan", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/execute", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/execute", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/publish", null);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<ImportJobDto>(Json))!.Idempotent.Should().BeTrue();

        await using var db = factory.CreateMigratorDbContext();
        var channel = await db.Channels.IgnoreQueryFilters().SingleAsync(x => x.WorkspaceId == SeedData.DemoWorkspaceId && x.Name == name);
        var root = await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.ConversationId == channel.Id && x.Body == "raiz importada");
        root.CreatedAt.Should().Be(DateTimeOffset.Parse("2020-01-02T03:04:05Z"));
        var author = await db.UserProfiles.SingleAsync(x => x.Id == root.AuthorId);
        author.Subject.Should().StartWith("import:");
        (await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(x => x.UserId == author.Id)).Should().BeFalse();
        (await db.MessageThreads.IgnoreQueryFilters().AnyAsync(x => x.ParentMessageId == root.Id)).Should().BeTrue();
        (await db.Messages.IgnoreQueryFilters().CountAsync(x => x.Body == "resposta importada")).Should().Be(1);
        (await db.Attachments.IgnoreQueryFilters().CountAsync(x => x.FileName == "note.txt" && x.MessageId == root.Id)).Should().Be(1);
        (await db.Attachments.IgnoreQueryFilters().AnyAsync(x => x.FileName == "evil.exe")).Should().BeFalse();

        var report = await demo.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/report");
        report.Should().NotContain("raiz importada");
        report.Should().NotContain("payloadBase64");

        db.ChangeTracker.Clear();
        var unconfirmed = await demo.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/rollback", new { confirm = false });
        unconfirmed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var removed = await demo.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/rollback", new { confirm = true });
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await db.Messages.IgnoreQueryFilters().AnyAsync(x => x.Id == root.Id)).Should().BeFalse();
        (await db.Channels.IgnoreQueryFilters().AnyAsync(x => x.Id == channel.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Foreign_tenant_job_is_invisible_and_pause_blocks_publish()
    {
        var foreignId = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.ImportJobs.Add(new ImportJobRecord
            {
                Id = foreignId,
                TenantId = new TenantId(foreignTenant),
                WorkspaceId = SeedData.DemoWorkspaceId,
                CreatedBy = SeedData.DemoUserId,
                Adapter = "vibechat",
                Status = ImportStatus.Validated,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                DocumentHash = "abc",
                CanonicalJson = "{}",
                ReportJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var demo = Client("demo");
        (await demo.GetAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{foreignId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

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

        await using (var count = conn.CreateCommand())
        {
            count.Transaction = tx;
            count.CommandText = """SELECT COUNT(*) FROM import.jobs WHERE "Id" = @id""";
            count.Parameters.AddWithValue("id", foreignId);
            ((long)(await count.ExecuteScalarAsync() ?? 0L)).Should().Be(0);
        }

        var created = await PostImportAsync(demo, Document($"pause{Guid.NewGuid():N}"[..12]));
        var job = await created.Content.ReadFromJsonAsync<ImportJobDto>(Json);
        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job!.Id}/plan", null)).EnsureSuccessStatusCode();
        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/pause", null)).EnsureSuccessStatusCode();
        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/publish", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/resume", null)).EnsureSuccessStatusCode();
        (await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/imports/{job.Id}/rollback", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Same_idempotency_key_replays_and_a_different_body_conflicts()
    {
        using var demo = Client("demo");
        var key = Guid.NewGuid().ToString("N");
        var first = await PostImportAsync(demo, Document("idem"), key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var replay = await PostImportAsync(demo, Document("idem"), key);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<ImportJobDto>(Json))!.Idempotent.Should().BeTrue();
        var conflict = await PostImportAsync(demo, Document("other-body"), key);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static async Task<HttpResponseMessage> PostImportAsync(HttpClient client, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/imports")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static object Document(string channel, bool withThread = false, bool withFile = false)
    {
        var messages = withThread
            ? new object[]
            {
                new { externalId = "M1", channelExternalId = "C1", authorExternalId = "U1", body = "raiz importada", createdAt = "2020-01-02T03:04:05Z" },
                new { externalId = "M2", channelExternalId = "C1", threadExternalId = "T1", authorExternalId = "U1", body = "resposta importada", createdAt = "2020-01-02T03:05:05Z" }
            }
            : new object[]
            {
                new { externalId = "M1", channelExternalId = "C1", authorExternalId = "U1", body = "raiz importada", createdAt = "2020-01-02T03:04:05Z" }
            };
        object[]? threads = withThread
            ? [new { externalId = "T1", channelExternalId = "C1", rootMessageExternalId = "M1" }]
            : null;
        object[]? attachments = withFile
            ?
            [
                new { externalId = "A1", messageExternalId = "M1", fileName = "note.txt", contentType = "text/plain", byteLength = 2, payloadBase64 = "aGk=" },
                new { externalId = "A2", messageExternalId = "M1", fileName = "evil.exe", contentType = "application/x-msdownload", byteLength = 4, payloadBase64 = "AAAA" }
            ]
            : null;
        return new
        {
            adapter = "vibechat",
            document = new
            {
                format = "vibechat.import.v1",
                tenantId = "99999999-9999-9999-9999-999999999999",
                principals = new[] { new { externalId = "U1", displayName = "Ada Import", role = "Member" } },
                spaces = new[] { new { externalId = "S1", name = $"Imp {channel}" } },
                channels = new[] { new { externalId = "C1", spaceExternalId = "S1", name = channel, kind = "public" } },
                threads,
                messages,
                attachments
            }
        };
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class ImportJobDto
    {
        public Guid Id { get; set; }
        public bool Idempotent { get; set; }
        public ImportReport Report { get; set; } = new();
    }
}
