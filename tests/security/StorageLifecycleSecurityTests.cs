using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-145 — storage ledger stays inside the tenant and the CDN hides the key.</summary>
[Collection(SecurityCollection.Name)]
public sealed class StorageLifecycleSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_cannot_set_limits_and_foreign_bytes_stay_hidden()
    {
        using var alice = Client("alice");
        var denied = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/storage/limits",
            new { kind = StorageObjectKinds.Export, softLimit = 1, hardLimit = 2 });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreignWorkspace = Guid.NewGuid();
        var foreignObject = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.StorageLedgers.Add(new StorageLedger
            {
                Id = Guid.NewGuid(),
                TenantId = new(foreignWorkspace),
                WorkspaceId = new(foreignWorkspace),
                Kind = StorageObjectKinds.Export,
                SoftLimit = 1,
                HardLimit = 2,
                UsedBytes = 2
            });
            db.StorageObjects.Add(new StorageObjectRecord
            {
                Id = foreignObject,
                TenantId = new(foreignWorkspace),
                WorkspaceId = new(foreignWorkspace),
                Kind = StorageObjectKinds.Export,
                State = StorageObjectStates.Hot,
                Bytes = 2,
                Referenced = true,
                Clean = true,
                StorageKey = "tenants/foreign-secret-key",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var demo = Client("demo");
        var usage = await demo.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/storage");
        usage.Should().NotContain("tenants/foreign-secret-key").And.NotContain(foreignObject.ToString());

        var cdn = await demo.GetAsync($"/api/v1/workspaces/{WorkspaceId}/storage/objects/{foreignObject}/cdn");
        cdn.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT c.relforcerowsecurity
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'files' AND c.relname = 'storage_objects'
            """;
        var forced = (bool)(await cmd.ExecuteScalarAsync())!;
        forced.Should().BeTrue();
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
