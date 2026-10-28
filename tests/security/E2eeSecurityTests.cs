using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-064 — device rows stay inside the tenant.</summary>
[Collection(SecurityCollection.Name)]
public sealed class E2eeSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Runtime_role_cannot_read_another_tenants_device()
    {
        var foreign = new TenantId(Guid.NewGuid());
        var deviceId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.E2eeDevices.Add(new E2eeDevice
            {
                Id = deviceId,
                TenantId = foreign,
                UserId = new UserId(Guid.NewGuid()),
                PublicJwk = """{"kty":"EC","crv":"P-256","x":"abc","y":"def"}""",
                Fingerprint = "ab",
                CreatedAt = DateTimeOffset.UtcNow
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
            count.CommandText = """SELECT COUNT(*) FROM messaging.e2ee_devices WHERE "Id" = @id""";
            count.Parameters.AddWithValue("id", deviceId);
            var seen = (long)(await count.ExecuteScalarAsync())!;
            seen.Should().Be(0);
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var row = await db.E2eeDevices.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == deviceId);
            if (row is not null)
            {
                db.E2eeDevices.Remove(row);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Disabled_flag_hides_capabilities()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Messaging:E2ee:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var response = await client.GetAsync("/api/v1/e2ee/capabilities");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(E2eeErrors.Disabled);
    }
}
