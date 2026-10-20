using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-130 — DLP policy stays off the admin role, and labels do not cross tenants.</summary>
[Collection(SecurityCollection.Name)]
public sealed class DlpSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Workspace_admin_without_a_grant_cannot_change_policy()
    {
        using var demo = Client("demo");
        (await PutAsync(demo, new { simulation = false })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        RolePermissionCatalog.For(Role.Admin).Should().NotContain(Permissions.Compliance.Dlp);
    }

    [Fact]
    public async Task Cross_tenant_label_is_invisible()
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
                Name = "Foreign dlp",
                Slug = $"dlp-{foreignWorkspace:N}"[..32],
                CreatedAt = now
            });
            db.DataLabels.Add(new DataLabel
            {
                Id = foreignId,
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                Key = "segredo",
                Rank = 9,
                CreatedBy = SeedData.DemoUserId,
                CreatedAt = now,
                IdempotencyKey = foreignId.ToString("N")
            });
            await db.SaveChangesAsync();
        }

        using var demo = Client("demo");
        await GrantAsync(demo, SeedData.AliceUserId.Value);
        using var alice = Client("alice");
        var listed = await alice.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/dlp/labels");
        listed.Should().NotContain("segredo");

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using (var clear = conn.CreateCommand())
        {
            clear.CommandText = "SELECT set_config('app.tenant_id', '', false)";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var count = conn.CreateCommand())
        {
            count.CommandText = """SELECT count(*)::int FROM compliance.data_labels WHERE "Id" = @id""";
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
            hidden.CommandText = """SELECT count(*)::int FROM compliance.data_labels WHERE "Id" = @id""";
            hidden.Parameters.AddWithValue("id", foreignId);
            ((int)(await hidden.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }

        await using (var db = factory.CreateMigratorDbContext())
        {
            var label = await db.DataLabels.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == foreignId);
            if (label is not null)
            {
                db.DataLabels.Remove(label);
            }

            var workspace = await db.Workspaces.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == new WorkspaceId(foreignWorkspace));
            if (workspace is not null)
            {
                db.Workspaces.Remove(workspace);
            }

            await db.SaveChangesAsync();
        }
    }

    private async Task GrantAsync(HttpClient demo, Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/compliance/grants")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { userId, capability = "dlp" }), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await demo.SendAsync(request);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/workspaces/{WorkspaceId}/dlp/policy")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        return await client.SendAsync(request);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
