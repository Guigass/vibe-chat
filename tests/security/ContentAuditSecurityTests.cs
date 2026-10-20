using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-169 — content audit is tenant-scoped and not writable by a member.</summary>
[Collection(SecurityCollection.Name)]
public sealed class ContentAuditSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Member_cannot_change_content_audit_and_rls_hides_the_other_tenant()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var denied = await alice.PutAsJsonAsync("/api/v1/admin/settings", new
        {
            workspaceId = SeedData.DemoWorkspaceId.Value,
            contentAudit = new { enabled = false, confirmDisable = true }
        });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreign = new TenantId(Guid.NewGuid());
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.ContentAuditSettings.Add(new ContentAuditSettings
            {
                TenantId = foreign,
                Enabled = false,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
            await conn.OpenAsync();
            await using (var set = conn.CreateCommand())
            {
                set.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
                set.Parameters.AddWithValue("tenant", SeedData.DemoTenantId.Value.ToString());
                await set.ExecuteNonQueryAsync();
            }

            await using var count = conn.CreateCommand();
            count.CommandText = """SELECT COUNT(*) FROM audit.content_audit_settings WHERE "TenantId" = @id""";
            count.Parameters.AddWithValue("id", foreign.Value);
            var seen = (long)(await count.ExecuteScalarAsync())!;
            seen.Should().Be(0);
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var row = await db.ContentAuditSettings.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == foreign);
            if (row is not null)
            {
                db.ContentAuditSettings.Remove(row);
                await db.SaveChangesAsync();
            }
        }
    }
}
