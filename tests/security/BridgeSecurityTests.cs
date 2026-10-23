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

/// <summary>B-138 — bridge admin stays in the workspace and FORCE RLS hides other tenants.</summary>
[Collection(SecurityCollection.Name)]
public sealed class BridgeSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_and_foreign_workspace_cannot_open_a_bridge()
    {
        using var bob = Client("bob");
        var denied = await bob.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/bridges", new
        {
            channelId = SeedData.DemoChannelId.Value,
            network = "labnet",
            remoteRoom = "room-1",
            destinationHost = "fake.bridge.local",
            capabilities = new[] { "message" },
            remoteCapabilities = new[] { "message" },
            idempotencyKey = "bridge-security-" + Guid.NewGuid().ToString("N")
        });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("vc_int_").And.NotContain("fake.bridge.local");

        var foreignWorkspaceId = await SeedForeignWorkspaceAsync();
        using var admin = Client("demo");
        var foreign = await admin.GetAsync($"/api/v1/admin/workspaces/{foreignWorkspaceId}/bridges/{Guid.NewGuid()}/mapping");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreign.Content.ReadAsStringAsync()).Should().NotContain("remote-subject");
    }

    [Fact]
    public async Task Runtime_role_cannot_read_another_tenants_bridge()
    {
        var foreignId = Guid.NewGuid();
        var foreignWorkspace = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var workspaceId = new WorkspaceId(foreignWorkspace);
            var tenantId = new TenantId(foreignWorkspace);
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                TenantId = tenantId,
                Name = "Foreign bridge",
                Slug = "brg-" + foreignWorkspace.ToString("N")[..12],
                CreatedAt = now
            });
            db.Bridges.Add(new Bridge
            {
                Id = foreignId,
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                ChannelId = new ChannelId(channelId),
                BotId = Guid.NewGuid(),
                Network = "foreign",
                RemoteRoom = "room-9",
                DestinationHost = "fake.bridge.local",
                Status = BridgeRules.StatusPending,
                Capabilities = ["message"],
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
            count.CommandText = """SELECT count(*)::int FROM integrations.bridges WHERE "Id" = @id""";
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
            hidden.CommandText = """SELECT count(*)::int FROM integrations.bridges WHERE "Id" = @id""";
            hidden.Parameters.AddWithValue("id", foreignId);
            ((int)(await hidden.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }

        await using var cleanup = factory.CreateMigratorDbContext();
        var row = await cleanup.Bridges.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == foreignId);
        if (row is not null) cleanup.Bridges.Remove(row);
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
            Name = "Foreign bridge gate",
            Slug = "bgate-" + workspaceId.Value.ToString("N")[..10],
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
