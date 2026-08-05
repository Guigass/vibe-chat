using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;
using Xunit;

namespace VibeChat.SecurityTests;

/// <summary>SEC-RLS-RUNTIME — proves FORCE RLS with the real app runtime role.</summary>
[Collection(SecurityCollection.Name)]
public sealed class RlsRuntimeEnforcementTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Runtime_role_is_not_privileged_and_does_not_own_messages()
    {
        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT current_user,
                   EXISTS (
                     SELECT 1 FROM pg_roles
                     WHERE rolname = current_user AND (rolsuper OR rolbypassrls)
                   ),
                   EXISTS (
                     SELECT 1
                     FROM pg_class c
                     JOIN pg_namespace n ON n.oid = c.relnamespace
                     WHERE n.nspname = 'messaging' AND c.relname = 'messages'
                       AND c.relowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)
                   ),
                   EXISTS (
                     SELECT 1
                     FROM pg_class c
                     JOIN pg_namespace n ON n.oid = c.relnamespace
                     WHERE n.nspname = 'messaging' AND c.relname = 'messages' AND c.relforcerowsecurity
                   ),
                   EXISTS (
                     SELECT 1
                     FROM pg_auth_members m
                     JOIN pg_roles r ON r.oid = m.roleid
                     WHERE m.member = (SELECT oid FROM pg_roles WHERE rolname = current_user)
                       AND r.rolbypassrls
                   )
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("vibechat_app");
        reader.GetBoolean(1).Should().BeFalse("runtime must not be superuser/BYPASSRLS");
        reader.GetBoolean(2).Should().BeFalse("runtime must not own messaging.messages");
        reader.GetBoolean(3).Should().BeTrue("FORCE ROW LEVEL SECURITY must be enabled");
        reader.GetBoolean(4).Should().BeFalse("runtime must not inherit BYPASSRLS via role membership");
    }

    [Fact]
    public async Task RlsSession_set_local_clears_after_commit()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetTenant(SeedData.DemoTenantId);
        tenant.SetUser(SeedData.AliceUserId);
        tenant.SetJobRole("outbox");

        await RlsSession.EnsureAppliedAsync(db, tenant, CancellationToken.None);

        var connection = db.Database.GetDbConnection();
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            check.CommandText =
                """
                SELECT current_setting('app.tenant_id', true),
                       current_setting('app.job_role', true)
                """;
            await using var reader = await check.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be(SeedData.DemoTenantId.Value.ToString());
            reader.GetString(1).Should().Be("outbox");
        }

        await RlsSession.CommitAsync(db);

        await using (var after = connection.CreateCommand())
        {
            after.CommandText =
                """
                SELECT coalesce(current_setting('app.tenant_id', true), ''),
                       coalesce(current_setting('app.job_role', true), '')
                """;
            await using var reader = await after.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().BeEmpty("SET LOCAL must not persist app.tenant_id after COMMIT");
            reader.GetString(1).Should().BeEmpty("SET LOCAL must not persist app.job_role after COMMIT");
        }
    }

    [Fact]
    public async Task Runtime_without_tenant_context_cannot_read_or_write_messages()
    {
        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();

        await using (var clear = conn.CreateCommand())
        {
            clear.CommandText = "SELECT set_config('app.tenant_id', '', false), set_config('app.user_id', '', false), set_config('app.job_role', '', false)";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var select = conn.CreateCommand())
        {
            select.CommandText = """SELECT count(*)::int FROM messaging.messages""";
            var count = (int)(await select.ExecuteScalarAsync() ?? -1);
            count.Should().Be(0, "fail-closed SELECT without app.tenant_id");
        }

        var insert = async () =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO messaging.messages
                  ("Id", "TenantId", "ConversationId", "AuthorId", "Sequence", "Body", "CreatedAt")
                VALUES
                  (@id, @tenant, @channel, @author, 999001, 'rls-denied', NOW())
                """;
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenant", SeedData.DemoTenantId.Value);
            cmd.Parameters.AddWithValue("channel", SeedData.DemoChannelId.Value);
            cmd.Parameters.AddWithValue("author", SeedData.AliceUserId.Value);
            await cmd.ExecuteNonQueryAsync();
        };

        await insert.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Runtime_with_tenant_a_cannot_read_tenant_b_rows()
    {
        var foreignTenant = TenantId.New();
        var foreignChannel = ChannelId.New();
        var foreignMessage = MessageId.New();

        await using (var db = factory.CreateMigratorDbContext())
        {
            db.Workspaces.Add(new VibeChat.Tenancy.Workspace
            {
                Id = new WorkspaceId(foreignTenant.Value),
                TenantId = foreignTenant,
                Name = "RLS Foreign",
                Slug = $"rls-foreign-{foreignTenant.Value:N}"[..32],
                AiEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.Channels.Add(new VibeChat.Conversations.Channel
            {
                Id = foreignChannel,
                TenantId = foreignTenant,
                WorkspaceId = new WorkspaceId(foreignTenant.Value),
                Name = "rls",
                Type = VibeChat.Conversations.ChannelType.Public,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = SeedData.DemoUserId
            });
            db.Messages.Add(new Message
            {
                Id = foreignMessage,
                TenantId = foreignTenant,
                ConversationId = foreignChannel,
                AuthorId = SeedData.DemoUserId,
                Sequence = 1,
                Body = "secret-other-tenant",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using (var set = conn.CreateCommand())
        {
            set.CommandText = "SELECT set_config('app.tenant_id', @t, false)";
            set.Parameters.AddWithValue("t", SeedData.DemoTenantId.Value.ToString());
            await set.ExecuteNonQueryAsync();
        }

        await using (var select = conn.CreateCommand())
        {
            select.CommandText = """SELECT count(*)::int FROM messaging.messages WHERE "Id" = @id""";
            select.Parameters.AddWithValue("id", foreignMessage.Value);
            var count = (int)(await select.ExecuteScalarAsync() ?? -1);
            count.Should().Be(0);
        }

        var crossInsert = async () =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO messaging.messages
                  ("Id", "TenantId", "ConversationId", "AuthorId", "Sequence", "Body", "CreatedAt")
                VALUES
                  (@id, @tenant, @channel, @author, 999002, 'spoof-tenant', NOW())
                """;
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenant", foreignTenant.Value);
            cmd.Parameters.AddWithValue("channel", foreignChannel.Value);
            cmd.Parameters.AddWithValue("author", SeedData.AliceUserId.Value);
            await cmd.ExecuteNonQueryAsync();
        };

        await crossInsert.Should().ThrowAsync<PostgresException>("WITH CHECK must reject TenantId != app.tenant_id");
    }
}
