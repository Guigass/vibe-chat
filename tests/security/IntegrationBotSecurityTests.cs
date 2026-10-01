using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-109 — integration token cannot cross tenants, leak, or be used by a member.</summary>
[Collection(SecurityCollection.Name)]
public sealed class IntegrationBotSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Member_cannot_create_bot_and_get_never_returns_the_secret()
    {
        using var bob = Client("bob");
        var denied = await bob.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/bots",
            new { name = "Nope", channelIds = new[] { ChannelId }, allowDms = false });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = Client("demo");
        var created = await CreateBotAsync(admin);
        var list = await admin.GetStringAsync($"/api/v1/admin/workspaces/{WorkspaceId}/bots");
        list.Should().NotContain("vc_int_");
        list.Should().Contain(created.TokenLast4);
    }

    [Fact]
    public async Task Token_from_another_tenant_cannot_post_here()
    {
        var foreignWorkspaceId = await SeedForeignWorkspaceAsync();
        using var admin = Client("demo");
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{foreignWorkspaceId}/bots",
            new { name = "Foreign Bot", channelIds = Array.Empty<Guid>(), allowDms = false });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedBot>(Json);

        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.TryAddWithoutValidation("X-VibeChat-Integration-Token", created!.Token);
        var send = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "cross", idempotencyKey = $"x-{Guid.NewGuid():N}" });
        send.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Header_token_is_accepted_and_unknown_token_is_unauthorized()
    {
        using var admin = Client("demo");
        var created = await CreateBotAsync(admin);
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        var ok = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "header", idempotencyKey = $"h-{Guid.NewGuid():N}" });
        ok.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var stranger = factory.CreateClient();
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "vc_int_" + new string('a', 64));
        var denied = await stranger.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "no", idempotencyKey = $"u-{Guid.NewGuid():N}" });
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private async Task<CreatedBot> CreateBotAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/bots",
            new { name = "Sec Bot", channelIds = new[] { ChannelId }, allowDms = false });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedBot>(Json))!;
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
            Name = "Foreign bots",
            Slug = $"foreign-bots-{workspaceId.Value:N}",
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

    private sealed record CreatedBot(Guid Id, string Token, string TokenLast4);
}
