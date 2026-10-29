using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Infrastructure;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-147 — live rows stay inside the tenant and the flag fails closed.</summary>
[Collection(SecurityCollection.Name)]
public sealed class LiveSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Runtime_role_cannot_read_another_tenants_session()
    {
        var foreign = new TenantId(Guid.NewGuid());
        var sessionId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.LiveSessions.Add(new LiveSession
            {
                Id = sessionId,
                TenantId = foreign,
                WorkspaceId = new WorkspaceId(foreign.Value),
                ChannelId = new ChannelId(Guid.NewGuid()),
                State = LiveRules.StateActive,
                CreatedBy = new UserId(Guid.NewGuid()),
                IdempotencyKey = "sec-" + sessionId.ToString("N"),
                RequestHash = "start",
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
            count.CommandText = """SELECT COUNT(*) FROM conversations.live_sessions WHERE "Id" = @id""";
            count.Parameters.AddWithValue("id", sessionId);
            var seen = (long)(await count.ExecuteScalarAsync())!;
            seen.Should().Be(0);
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var row = await db.LiveSessions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == sessionId);
            if (row is not null)
            {
                db.LiveSessions.Remove(row);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Runtime_role_cannot_read_another_tenants_recording()
    {
        var foreign = new TenantId(Guid.NewGuid());
        var recordingId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.LiveRecordings.Add(new LiveRecording
            {
                Id = recordingId,
                TenantId = foreign,
                WorkspaceId = new WorkspaceId(foreign.Value),
                ChannelId = new ChannelId(Guid.NewGuid()),
                SessionId = Guid.NewGuid(),
                State = LiveRules.RecordingConsent,
                StartedBy = new UserId(Guid.NewGuid()),
                IdempotencyKey = "sec-" + recordingId.ToString("N"),
                RequestHash = LiveRules.RecordHash,
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
            count.CommandText = """SELECT COUNT(*) FROM conversations.live_recordings WHERE "Id" = @id""";
            count.Parameters.AddWithValue("id", recordingId);
            var seen = (long)(await count.ExecuteScalarAsync())!;
            seen.Should().Be(0);
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var row = await db.LiveRecordings.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == recordingId);
            if (row is not null)
            {
                db.LiveRecordings.Remove(row);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Disabled_flag_hides_live_routes()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:Live:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var response = await client.GetAsync($"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/live/health");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(LiveErrors.Disabled);
    }
}
