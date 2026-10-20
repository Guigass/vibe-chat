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

/// <summary>B-133 — policy packs stay off the admin role and cannot cross tenants or carry secrets.</summary>
[Collection(SecurityCollection.Name)]
public sealed class PolicyPackSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Workspace_admin_without_a_grant_cannot_apply_a_pack()
    {
        using var demo = Client("demo");
        var response = await SendAsync(demo, ApplyPath(), new { packId = PolicyPackIds.SmallTeam, version = 1, confirm = true });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        RolePermissionCatalog.For(Role.Admin).Should().NotContain(Permissions.Compliance.Policy);
    }

    [Fact]
    public async Task Forbidden_override_malicious_import_and_foreign_rows_fail_closed()
    {
        using var demo = Client("demo");
        await GrantAsync(demo);
        using var alice = Client("alice");

        var weakened = await SendAsync(alice, ApplyPath(), new
        {
            packId = PolicyPackIds.EnterpriseBaseline,
            version = 1,
            confirm = true,
            overrides = new Dictionary<string, bool> { ["guests.enabled"] = true }
        });
        weakened.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await weakened.Content.ReadAsStringAsync()).Should().Contain(PolicyPackErrors.OverrideForbidden);

        var imported = await SendAsync(alice, ImportPath(), new
        {
            schema = PolicyPackSchema.Name,
            packId = PolicyPackIds.SmallTeam,
            version = 1,
            confirm = true,
            tenantId = WorkspaceId,
            secret = "super-secret-value",
            script = "eval(atob('a'))"
        });
        imported.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await imported.Content.ReadAsStringAsync()).Should().Contain(PolicyPackErrors.ImportRejected);

        var foreignId = Guid.NewGuid();
        var foreignWorkspace = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Workspaces.Add(new Workspace
            {
                Id = new WorkspaceId(foreignWorkspace),
                TenantId = new TenantId(foreignWorkspace),
                Name = "Foreign pack",
                Slug = $"pack-{foreignWorkspace:N}"[..32],
                CreatedAt = now
            });
            db.PolicyPackAssignments.Add(new PolicyPackAssignment
            {
                Id = foreignId,
                TenantId = new TenantId(foreignWorkspace),
                WorkspaceId = new WorkspaceId(foreignWorkspace),
                PackId = "foreign-pack-should-stay-hidden",
                PackVersion = 1,
                OverridesJson = "{}",
                EffectiveJson = """{"packId":"foreign-pack-should-stay-hidden"}""",
                IdempotencyKey = foreignId.ToString("N"),
                RequestHash = "abc",
                AppliedBy = new UserId(foreignWorkspace),
                AppliedAt = now
            });
            await db.SaveChangesAsync();
        }

        var current = await alice.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/policy-packs");
        current.Should().NotContain("foreign-pack-should-stay-hidden");

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using (var clear = conn.CreateCommand())
        {
            clear.CommandText = "SELECT set_config('app.tenant_id', '', false)";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var count = conn.CreateCommand())
        {
            count.CommandText = """SELECT count(*)::int FROM compliance.policy_assignments WHERE "Id" = @id""";
            count.Parameters.AddWithValue("id", foreignId);
            ((int)(await count.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.PolicyPackAssignments.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == foreignId);
            if (row is not null)
            {
                db.PolicyPackAssignments.Remove(row);
            }

            var workspace = await db.Workspaces.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == new WorkspaceId(foreignWorkspace));
            if (workspace is not null)
            {
                db.Workspaces.Remove(workspace);
            }

            await db.SaveChangesAsync();
        }
    }

    private async Task GrantAsync(HttpClient demo)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/compliance/grants")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { userId = SeedData.AliceUserId.Value, capability = "policy" }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "policy-sec-" + Guid.NewGuid().ToString("N"));
        var response = await demo.SendAsync(request);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static string ApplyPath() => $"/api/v1/workspaces/{WorkspaceId}/policy-packs/apply";

    private static string ImportPath() => $"/api/v1/workspaces/{WorkspaceId}/policy-packs/import";

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
