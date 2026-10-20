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

/// <summary>B-132 — SIEM export stays off the admin role and cannot cross tenants or private networks.</summary>
[Collection(SecurityCollection.Name)]
public sealed class SiemSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Workspace_admin_without_a_grant_cannot_create_a_destination()
    {
        using var demo = Client("demo");
        var response = await SendAsync(demo, Destinations(), new
        {
            name = "denied",
            url = "https://siem.example.test/denied",
            transport = "webhook-json"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        RolePermissionCatalog.For(Role.Admin).Should().NotContain(Permissions.Security.AuditExport);
    }

    [Fact]
    public async Task Private_and_rebound_destinations_are_rejected_and_foreign_rows_stay_hidden()
    {
        SiemSandbox.Shared.Reset();
        using var demo = Client("demo");
        await GrantAsync(demo);
        using var alice = Client("alice");
        foreach (var url in new[] { "http://127.0.0.1/siem", "https://169.254.169.254/latest", "https://rebind.example.test/hook", "https://metadata.google.internal/latest" })
        {
            var blocked = await SendAsync(alice, Destinations(), new { name = "blocked", url, transport = "webhook-json" });
            blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await blocked.Content.ReadAsStringAsync()).Should().Contain(SiemErrors.EgressBlocked);
        }

        SiemSandbox.Shared.Calls.Should().BeEmpty();

        var foreignId = Guid.NewGuid();
        var foreignWorkspace = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Workspaces.Add(new Workspace
            {
                Id = new WorkspaceId(foreignWorkspace),
                TenantId = new TenantId(foreignWorkspace),
                Name = "Foreign siem",
                Slug = $"siem-{foreignWorkspace:N}"[..32],
                CreatedAt = now
            });
            db.SiemDestinations.Add(new SiemDestination
            {
                Id = foreignId,
                TenantId = new TenantId(foreignWorkspace),
                WorkspaceId = new WorkspaceId(foreignWorkspace),
                Name = "foreign-siem-destination",
                Transport = SiemTransports.WebhookJson,
                Url = "https://siem.example.test/foreign",
                CategoriesJson = "[]",
                MinSeverity = SiemSeverities.Info,
                Status = SiemStatuses.Revoked,
                Circuit = SiemCircuits.Closed,
                CheckpointAt = now,
                IdempotencyKey = foreignId.ToString("N"),
                CreatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var listed = await alice.GetStringAsync(Destinations());
        listed.Should().NotContain("foreign-siem-destination");

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using (var clear = conn.CreateCommand())
        {
            clear.CommandText = "SELECT set_config('app.tenant_id', '', false)";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var count = conn.CreateCommand())
        {
            count.CommandText = """SELECT count(*)::int FROM compliance.siem_destinations WHERE "Id" = @id""";
            count.Parameters.AddWithValue("id", foreignId);
            ((int)(await count.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.SiemDestinations.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == foreignId);
            if (row is not null)
            {
                db.SiemDestinations.Remove(row);
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
                JsonSerializer.Serialize(new { userId = SeedData.AliceUserId.Value, capability = "audit.export" }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
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

    private static string Destinations() => $"/api/v1/workspaces/{WorkspaceId}/siem/destinations";

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
