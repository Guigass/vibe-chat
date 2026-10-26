using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-172 — backup rows, secrets and restores stay inside the tenant.</summary>
[Collection(SecurityCollection.Name)]
public sealed class ChatBackupSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_cannot_manage_backup_and_foreign_rows_stay_hidden()
    {
        using var alice = Client("alice");
        var denied = await alice.GetAsync($"/api/v1/workspaces/{WorkspaceId}/chat-backup");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreign = Guid.NewGuid();
        var foreignMessage = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.ChatBackupDestinations.Add(new ChatBackupDestination
            {
                Id = Guid.NewGuid(),
                TenantId = new TenantId(foreign),
                WorkspaceId = new WorkspaceId(foreign),
                Name = "foreign-backup",
                Kind = ChatBackupKinds.S3,
                Host = "secret.internal",
                Port = 9000,
                RemotePath = "hidden",
                Username = "foreign",
                Bucket = "foreign",
                SecretMask = "zzzz",
                IdempotencyKey = foreign.ToString("N"),
                RequestHash = "abc",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.Messages.Add(new Message
            {
                Id = new MessageId(foreignMessage),
                TenantId = new TenantId(foreign),
                ConversationId = new ChannelId(Guid.NewGuid()),
                Sequence = 1,
                AuthorId = SeedData.DemoUserId,
                Body = "foreign-secret-body",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var demo = Client("demo");
        var list = await demo.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/chat-backup");
        list.Should().NotContain("secret.internal").And.NotContain("foreign-backup").And.NotContain("foreign-secret-body");

        var zip = ChatBackupPackage.Compose(
            Guid.NewGuid(),
            WorkspaceId,
            DateTimeOffset.UtcNow,
            false,
            true,
            new Dictionary<string, byte[]>
            {
                [ChatBackupPackage.MessagesEntry] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[]
                {
                    new ChatBackupMessageEntry(foreignMessage, SeedData.DemoChannelId.Value, 1, SeedData.DemoUserId.Value, "foreign-secret-body", false, null, null, DateTimeOffset.UtcNow, null, null, null)
                }, ChatBackupPackage.Json)
            });
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/chat-backup/import/execute?confirm=true")
        {
            Content = new ByteArrayContent(zip)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Headers.Add("Idempotency-Key", "sec-" + Guid.NewGuid().ToString("N"));
        var imported = await demo.SendAsync(request);
        imported.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await imported.Content.ReadAsStringAsync()).Should().Contain(ChatBackupErrors.CrossTenant);

        await using var conn = new NpgsqlConnection(factory.RuntimeDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT c.relforcerowsecurity
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'administration' AND c.relname = 'chat_backup_destinations'
            """;
        var forced = (bool)(await cmd.ExecuteScalarAsync())!;
        forced.Should().BeTrue();
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
