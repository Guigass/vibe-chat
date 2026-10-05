using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-114 — versão de outro tenant não vaza e o move cross-tenant não grava.</summary>
[Collection(SecurityCollection.Name)]
public sealed class MessageHistorySecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Cross_tenant_version_is_invisible_to_the_app_role()
    {
        var foreignId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var tenantId = new TenantId(Guid.NewGuid());
            db.MessageVersions.Add(new MessageVersion
            {
                Id = foreignId,
                TenantId = tenantId,
                MessageId = new MessageId(messageId),
                VersionNumber = 1,
                Body = "segredo-de-outro-tenant",
                ActorUserId = new UserId(Guid.NewGuid()),
                CreatedAt = DateTimeOffset.UtcNow
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
            count.CommandText = """SELECT count(*)::int FROM messaging.message_versions WHERE "Id" = @id""";
            count.Parameters.AddWithValue("id", foreignId);
            ((int)(await count.ExecuteScalarAsync() ?? -1)).Should().Be(0);
        }

        await using (var scoped = conn.CreateCommand())
        {
            scoped.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
            scoped.Parameters.AddWithValue("tenant", SeedData.DemoTenantId.Value.ToString());
            await scoped.ExecuteNonQueryAsync();
        }

        await using var hidden = conn.CreateCommand();
        hidden.CommandText = """SELECT count(*)::int FROM messaging.message_versions WHERE "Id" = @id""";
        hidden.Parameters.AddWithValue("id", foreignId);
        ((int)(await hidden.ExecuteScalarAsync() ?? -1)).Should().Be(0);
    }

    [Fact]
    public async Task Move_to_unknown_channel_does_not_create_a_message()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var messageId = Guid.NewGuid();
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId, idempotencyKey = $"sec-{messageId:N}", body = $"sec-{messageId:N}" });
        sent.EnsureSuccessStatusCode();

        var foreignChannel = Guid.NewGuid();
        var move = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}/move",
            new { idempotencyKey = $"sec-move-{messageId:N}", targetChannelId = foreignChannel });
        move.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = factory.CreateMigratorDbContext();
        var source = await db.Messages.IgnoreQueryFilters().SingleAsync(x => x.Id == new MessageId(messageId));
        source.MovedToMessageId.Should().BeNull();
        source.Body.Should().Be($"sec-{messageId:N}");
        (await db.MessageMoves.IgnoreQueryFilters().AnyAsync(x => x.SourceMessageId == source.Id)).Should().BeFalse();
    }
}
