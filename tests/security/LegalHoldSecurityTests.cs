using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-129 — compliance capabilities stay off the admin role, and holds do not cross tenants.</summary>
[Collection(SecurityCollection.Name)]
public sealed class LegalHoldSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Workspace_admin_without_a_grant_cannot_open_a_hold()
    {
        using var demo = Client("demo");
        using var bob = Client("bob");
        (await PostAsync(demo, new { scopeKind = "workspace", reason = "investigacao autorizada", confirmed = true, acknowledged = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync(bob, new { scopeKind = "workspace", reason = "investigacao autorizada", confirmed = true, acknowledged = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cross_tenant_hold_is_invisible()
    {
        using var demo = Client("demo");
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
                Name = "Foreign hold",
                Slug = $"hold-{foreignWorkspace:N}"[..32],
                CreatedAt = now
            });
            db.LegalHolds.Add(new LegalHold
            {
                Id = foreignId,
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                ScopeKind = LegalHoldScope.Workspace,
                Reason = "segredo de outro tenant",
                Status = LegalHoldStatus.Active,
                RequestedBy = SeedData.DemoUserId,
                CreatedAt = now,
                IdempotencyKey = foreignId.ToString("N")
            });
            await db.SaveChangesAsync();
        }

        await GrantAsync(demo, SeedData.AliceUserId.Value);
        using var alice = Client("alice");
        var listed = await alice.GetFromJsonAsync<HoldList>($"/api/v1/workspaces/{WorkspaceId}/compliance/holds", Json);
        listed!.Holds.Select(x => x.Id).Should().NotContain(foreignId);

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using (var clear = conn.CreateCommand())
        {
            clear.CommandText = "SELECT set_config('app.tenant_id', '', false)";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var count = conn.CreateCommand())
        {
            count.CommandText = """SELECT count(*)::int FROM compliance.legal_holds WHERE "Id" = @id""";
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
            hidden.CommandText = """SELECT count(*)::int FROM compliance.legal_holds WHERE "Id" = @id""";
            hidden.Parameters.AddWithValue("id", foreignId);
            ((int)(await hidden.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }
    }

    private async Task GrantAsync(HttpClient demo, Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/compliance/grants")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { userId, capability = "hold" }), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await demo.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/compliance/holds")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record HoldList(HoldItem[] Holds);
    private sealed record HoldItem(Guid Id);
}
