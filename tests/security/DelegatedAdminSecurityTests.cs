using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-134 — delegated admin cannot cross tenants or raise itself.</summary>
[Collection(SecurityCollection.Name)]
public sealed class DelegatedAdminSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_cannot_raise_a_quota_and_foreign_rows_stay_hidden()
    {
        using var alice = Client("alice");
        var denied = await alice.PutAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/quotas/{DelegatedResources.Users}", new { softLimit = 1, hardLimit = 2 });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().Contain(DelegatedAdminErrors.ScopeExceeded);

        var foreignId = Guid.NewGuid();
        var foreignWorkspace = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Workspaces.Add(new Workspace
            {
                Id = new WorkspaceId(foreignWorkspace),
                TenantId = new TenantId(foreignWorkspace),
                Name = "Foreign delegation",
                Slug = $"del-{foreignWorkspace:N}"[..32],
                CreatedAt = now
            });
            db.ScopedRoleAssignments.Add(new ScopedRoleAssignment
            {
                Id = foreignId,
                TenantId = new TenantId(foreignWorkspace),
                WorkspaceId = new WorkspaceId(foreignWorkspace),
                GranteeId = new UserId(foreignWorkspace),
                Scope = DelegatedScopes.Tenant,
                Reason = "foreign-delegation-hidden",
                GrantedBy = new UserId(foreignWorkspace),
                GrantedAt = now,
                IdempotencyKey = foreignId.ToString("N"),
                RequestHash = "abc",
                ResultJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        using var demo = Client("demo");
        var list = await demo.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/delegated-admin/assignments");
        list.Should().NotContain("foreign-delegation-hidden").And.NotContain(foreignId.ToString());

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using (var clear = conn.CreateCommand())
        {
            clear.CommandText = "SELECT set_config('app.tenant_id', '', false)";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var count = conn.CreateCommand())
        {
            count.CommandText = """SELECT count(*)::int FROM compliance.scoped_role_assignments WHERE "Id" = @id""";
            count.Parameters.AddWithValue("id", foreignId);
            ((int)(await count.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.ScopedRoleAssignments.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == foreignId);
            if (row is not null)
            {
                db.ScopedRoleAssignments.Remove(row);
            }

            var workspace = await db.Workspaces.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == new WorkspaceId(foreignWorkspace));
            if (workspace is not null)
            {
                db.Workspaces.Remove(workspace);
            }

            await db.SaveChangesAsync();
        }
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
