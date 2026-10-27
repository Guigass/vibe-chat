using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-065 — federation admin stays in the workspace and FORCE RLS hides other tenants.</summary>
[Collection(SecurityCollection.Name)]
public sealed class FederationSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_and_foreign_workspace_cannot_open_a_trust()
    {
        using var bob = Client("bob");
        var denied = await bob.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts", new
        {
            domain = "peer.example",
            remoteInstanceId = "remote-bob",
            capabilities = new[] { "message" },
            remoteCapabilities = new[] { "message" },
            remoteSchemaVersion = 1,
            idempotencyKey = "fed-security-" + Guid.NewGuid().ToString("N")
        });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("vc_int_").And.NotContain("peer.example");

        var foreignWorkspaceId = await SeedForeignWorkspaceAsync();
        using var admin = Client("demo");
        var foreign = await admin.PostAsJsonAsync($"/api/v1/admin/workspaces/{foreignWorkspaceId}/federation/trusts", new
        {
            domain = "peer.example",
            remoteInstanceId = "remote-foreign",
            capabilities = new[] { "message" },
            remoteCapabilities = new[] { "message" },
            remoteSchemaVersion = 1,
            idempotencyKey = "fed-foreign-" + Guid.NewGuid().ToString("N")
        });
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Runtime_role_cannot_read_another_tenants_trust()
    {
        var foreignId = Guid.NewGuid();
        var foreignWorkspace = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var workspaceId = new WorkspaceId(foreignWorkspace);
            var tenantId = new TenantId(foreignWorkspace);
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                TenantId = tenantId,
                Name = "Foreign federation",
                Slug = "fgate-" + foreignWorkspace.ToString("N")[..10],
                CreatedAt = now
            });
            db.FederationTrusts.Add(new FederationTrust
            {
                Id = foreignId,
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                BotId = Guid.NewGuid(),
                Domain = "peer.example",
                RemoteInstanceId = "foreign-instance",
                Status = FederationRules.StatusPending,
                Capabilities = ["message"],
                CurrentKeyId = FederationRules.InitialKeyId,
                CurrentTokenId = Guid.NewGuid(),
                DeleteRemoteGuaranteed = false,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using (var clear = conn.CreateCommand())
        {
            clear.CommandText = "SELECT set_config('app.tenant_id', '', false)";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var count = conn.CreateCommand())
        {
            count.CommandText = """SELECT count(*)::int FROM integrations.federation_trusts WHERE "Id" = @id""";
            count.Parameters.AddWithValue("id", foreignId);
            ((int)(await count.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }

        await using (var scoped = conn.CreateCommand())
        {
            scoped.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
            scoped.Parameters.AddWithValue("tenant", SeedData.DemoTenantId.Value.ToString());
            await scoped.ExecuteNonQueryAsync();
        }

        await using (var hidden = conn.CreateCommand())
        {
            hidden.CommandText = """SELECT count(*)::int FROM integrations.federation_trusts WHERE "Id" = @id""";
            hidden.Parameters.AddWithValue("id", foreignId);
            ((int)(await hidden.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }

        await using var cleanup = factory.CreateMigratorDbContext();
        var row = await cleanup.FederationTrusts.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == foreignId);
        if (row is not null) cleanup.FederationTrusts.Remove(row);
        var workspace = await cleanup.Workspaces.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == new WorkspaceId(foreignWorkspace));
        if (workspace is not null) cleanup.Workspaces.Remove(workspace);
        await cleanup.SaveChangesAsync();
    }

    private async Task<Guid> SeedForeignWorkspaceAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = new WorkspaceId(Guid.NewGuid());
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = new TenantId(workspaceId.Value),
            Name = "Foreign federation gate",
            Slug = "fg-" + workspaceId.Value.ToString("N")[..12],
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        return workspaceId.Value;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
