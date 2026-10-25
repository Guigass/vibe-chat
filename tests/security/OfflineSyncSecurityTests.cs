using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-143 — a cursor from another principal cannot read that principal's sync window.</summary>
[Collection(SecurityCollection.Name)]
public sealed class OfflineSyncSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Foreign_cursor_does_not_return_the_other_tenant_body()
    {
        var foreignBody = $"foreign-offline-{Guid.NewGuid():N}";
        var foreignTenant = TenantId.New();
        var foreignChannel = ChannelId.New();
        var foreignMessage = MessageId.New();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.Workspaces.Add(new Workspace
            {
                Id = new WorkspaceId(foreignTenant.Value),
                TenantId = foreignTenant,
                Name = "Offline Foreign",
                Slug = $"off-{foreignTenant.Value:N}"[..32],
                AiEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Channels.Add(new VibeChat.Conversations.Channel
            {
                Id = foreignChannel,
                TenantId = foreignTenant,
                WorkspaceId = new WorkspaceId(foreignTenant.Value),
                Name = "offline",
                Type = VibeChat.Conversations.ChannelType.Public,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = SeedData.DemoUserId,
            });
            db.Messages.Add(new Message
            {
                Id = foreignMessage,
                TenantId = foreignTenant,
                ConversationId = foreignChannel,
                Sequence = 1,
                AuthorId = SeedData.DemoUserId,
                Body = foreignBody,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var device = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var stolen = OfflineSyncCursor.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            device,
            DateTimeOffset.UnixEpoch,
            Guid.Empty,
            DateTimeOffset.UtcNow,
            "vibechat-lab-offline-cursor");
        var response = await alice.GetAsync($"/api/v1/sync?deviceId={device}&cursor={Uri.EscapeDataString(stolen)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(foreignBody);
        raw.Should().NotContain(foreignMessage.Value.ToString());
        var page = JsonSerializer.Deserialize<SyncDto>(raw, Json);
        page!.Rebuild!.Reason.Should().Be("FullResyncRequired");
    }

    private sealed record SyncDto(RebuildDto? Rebuild);

    private sealed record RebuildDto(string Reason);
}
