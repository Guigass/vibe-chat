using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-110 — members cannot manage plugins; a plugin id does not cross tenants.</summary>
[Collection(SecurityCollection.Name)]
public sealed class IntegrationPluginSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Member_cannot_install_or_list_plugins()
    {
        using var bob = Client("bob");
        var denied = await bob.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new { builtinId = PluginManifestRules.BuiltinIncomingMessagesId, channelIds = new[] { ChannelId }, allowDms = false });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var list = await bob.GetAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Plugin_installed_in_another_workspace_is_not_addressable_here()
    {
        var foreignWorkspaceId = await SeedForeignWorkspaceAsync();
        using var admin = Client("demo");
        var createdResponse = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{foreignWorkspaceId}/plugins",
            new { builtinId = PluginManifestRules.BuiltinIncomingMessagesId, channelIds = Array.Empty<Guid>(), allowDms = false });
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadFromJsonAsync<InstalledDto>(Json);

        var home = await admin.GetStringAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins");
        home.Should().NotContain(created!.Id.ToString());

        var patch = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{created.Id}",
            new { enabled = false });
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.TryAddWithoutValidation("X-VibeChat-Integration-Token", created.Token);
        var send = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "cross", idempotencyKey = $"px-{Guid.NewGuid():N}" });
        send.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private async Task<Guid> SeedForeignWorkspaceAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = VibeChat.SharedKernel.WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = new TenantId(workspaceId.Value),
            Name = "Foreign plugins",
            Slug = $"foreign-plugins-{workspaceId.Value:N}",
            AiEnabled = false,
            CreatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = new TenantId(workspaceId.Value),
            WorkspaceId = workspaceId,
            UserId = SeedData.DemoUserId,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });
        await db.SaveChangesAsync();
        return workspaceId.Value;
    }

    private sealed record InstalledDto(Guid Id, string Token);
}
