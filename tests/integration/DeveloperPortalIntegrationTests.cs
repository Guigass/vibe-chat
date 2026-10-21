using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-135 — portal credentials, synthetic explorer and contract catalog.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class DeveloperPortalIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Secret_is_shown_once_revoke_is_immediate_and_openapi_matches_runtime()
    {
        using var admin = Client("demo");
        var key = $"k-{Guid.NewGuid():N}";
        var created = await CreateAsync(admin, "Portal Bot", [ChannelId], allowDms: false, key);
        created.Token.Should().StartWith("vc_int_");
        created.Idempotent.Should().BeFalse();

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/developer/credentials")
        {
            Content = JsonContent.Create(new { name = "Portal Bot", channelIds = new[] { ChannelId }, allowDms = false })
        };
        replayRequest.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        var replay = await admin.SendAsync(replayRequest);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayBody = await replay.Content.ReadAsStringAsync();
        replayBody.Should().NotContain("vc_int_").And.Contain("\"idempotent\":true");
        replay.Headers.CacheControl?.NoStore.Should().BeTrue();

        var listed = await admin.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/developer/credentials");
        listed.Should().NotContain("vc_int_").And.Contain(created.Token[^4..]);

        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        var sent = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "portal-live", idempotencyKey = $"live-{Guid.NewGuid():N}" });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var revoked = await admin.PostAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/credentials/{created.Id}/revoke",
            null);
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
        (await revoked.Content.ReadAsStringAsync()).Should().NotContain(created.Token);

        var after = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "portal-revoked", idempotencyKey = $"rev-{Guid.NewGuid():N}" });
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var openapi = await admin.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/developer/openapi");
        var paths = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet();
        foreach (var path in DeveloperPortalCatalog.RuntimePaths)
        {
            openapi.Should().Contain(path);
            paths.Should().Contain(path);
        }

        var kit = await admin.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/developer/contract-kit");
        kit.Should().Contain("messaging.message.created").And.NotContain("vc_int_");
    }

    [Fact]
    public async Task Explorer_and_webhook_inspector_stay_synthetic_inside_scope()
    {
        using var admin = Client("demo");
        var created = await CreateAsync(admin, "Sandbox Bot", [ChannelId], allowDms: false, $"k-{Guid.NewGuid():N}");
        await using var db = factory.CreateMigratorDbContext();
        var before = await db.Messages.IgnoreQueryFilters().CountAsync(x => x.ConversationId == new ChannelId(ChannelId));

        var allowed = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/explore",
            new { credentialId = created.Id, operation = "messages.send", channelId = ChannelId });
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        var allowedBody = await allowed.Content.ReadAsStringAsync();
        allowedBody.Should().Contain("\"synthetic\":true").And.Contain("\"delivered\":false").And.NotContain(created.Token);

        var other = Guid.NewGuid();
        var denied = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/explore",
            new { credentialId = created.Id, operation = "messages.send", channelId = other });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().Contain(DeveloperPortalErrors.ScopeDenied).And.NotContain(created.Token);

        var dm = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/explore",
            new { credentialId = created.Id, operation = "messages.dm" });
        dm.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var inspected = await admin.PostAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/webhook-inspector",
            null);
        inspected.StatusCode.Should().Be(HttpStatusCode.OK);
        var inspector = await inspected.Content.ReadAsStringAsync();
        inspector.Should().Contain("\"delivered\":false").And.Contain("messaging.message.created").And.NotContain("vc_int_");

        var after = await db.Messages.IgnoreQueryFilters().CountAsync(x => x.ConversationId == new ChannelId(ChannelId));
        after.Should().Be(before);

        var usage = await admin.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/developer/usage");
        usage.Should().Contain(created.Id.ToString()).And.NotContain(created.Token);
    }

    [Fact]
    public async Task Kill_switch_hides_the_portal()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Integrations:DeveloperPortal:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var response = await client.GetAsync($"/api/v1/workspaces/{WorkspaceId}/developer/catalog");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(DeveloperPortalErrors.Disabled);
    }

    private async Task<CreatedCredential> CreateAsync(HttpClient admin, string name, Guid[] channelIds, bool allowDms, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/developer/credentials")
        {
            Content = JsonContent.Create(new { name, channelIds, allowDms })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        var response = await admin.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<CreatedCredential>(Json))!;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record CreatedCredential(Guid Id, string Token, bool Idempotent);
}
