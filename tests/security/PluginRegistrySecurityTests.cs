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

/// <summary>B-137 — registry admin stays inside the workspace and FORCE RLS hides other tenants.</summary>
[Collection(SecurityCollection.Name)]
public sealed class PluginRegistrySecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_and_foreign_workspace_cannot_read_the_registry()
    {
        using var bob = Client("bob");
        var denied = await bob.GetAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugin-registry");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("publisher").And.NotContain("vc_int_");

        var installDenied = await bob.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugin-registry/install",
            new { index = "{}", pluginId = "x", version = "1.0.0", idempotencyKey = "registry-security-key" });
        installDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreignWorkspaceId = await SeedForeignWorkspaceAsync();
        using var admin = Client("demo");
        var foreign = await admin.GetAsync($"/api/v1/admin/workspaces/{foreignWorkspaceId}/plugin-registry");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreign.Content.ReadAsStringAsync()).Should().NotContain(foreignWorkspaceId.ToString()).And.NotContain("vc_int_");
    }

    [Fact]
    public async Task Runtime_role_cannot_read_another_tenants_registry_row()
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
                Name = "Foreign registry",
                Slug = "reg-" + foreignWorkspace.ToString("N")[..12],
                CreatedAt = now
            });
            db.PluginRegistryInstalls.Add(new PluginRegistryInstall
            {
                Id = foreignId,
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                PluginId = "foreign-registry",
                Version = "1.0.0",
                Payload = "{}",
                Signature = "rsapss-sha256=aa",
                Publisher = "foreign-publisher",
                Source = "mirror",
                CatalogId = "mirror",
                KeyId = "lab",
                Checksum = "sha256=" + new string('a', 64),
                ArtifactRef = "mirror://lab/foreign-registry/1.0.0",
                Provenance = "test",
                Risk = "low",
                Capabilities = ["messages.send"],
                GrantedCapabilities = ["messages.send"],
                DependsOn = [],
                Status = PluginRegistryRules.ActiveStatus,
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
            count.CommandText = """SELECT count(*)::int FROM integrations.plugin_registry_installs WHERE "Id" = @id""";
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
            hidden.CommandText = """SELECT count(*)::int FROM integrations.plugin_registry_installs WHERE "Id" = @id""";
            hidden.Parameters.AddWithValue("id", foreignId);
            ((int)(await hidden.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }

        await using var cleanup = factory.CreateMigratorDbContext();
        var row = await cleanup.PluginRegistryInstalls.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == foreignId);
        if (row is not null) cleanup.PluginRegistryInstalls.Remove(row);
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
            Name = "Foreign registry gate",
            Slug = "gate-" + workspaceId.Value.ToString("N")[..12],
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
