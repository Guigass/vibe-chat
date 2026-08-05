using FluentAssertions;
using Npgsql;
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
                   )
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("vibechat_app");
        reader.GetBoolean(1).Should().BeFalse("runtime must not be superuser/BYPASSRLS");
        reader.GetBoolean(2).Should().BeFalse("runtime must not own messaging.messages");
        reader.GetBoolean(3).Should().BeTrue("FORCE ROW LEVEL SECURITY must be enabled");
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
