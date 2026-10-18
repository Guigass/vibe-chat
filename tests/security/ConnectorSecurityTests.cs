using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class ConnectorSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_and_other_tenant_cannot_read_connector_secrets()
    {
        using var demo = Client("demo");
        const string token = "connector-token-secret-99";
        var created = await demo.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/connectors", new
        {
            idempotencyKey = $"sec-{Guid.NewGuid():N}"[..20],
            provider = "github",
            destinationKind = "channel",
            destinationId = SeedData.DemoChannelId.Value,
            scopes = new[] { "issues:read", "issues:write" },
            token,
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var listed = await demo.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/connectors");
        listed.Should().NotContain(token);

        using var bob = Client("bob");
        var denied = await bob.GetAsync($"/api/v1/workspaces/{WorkspaceId}/connectors");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain(token);

        var foreign = await demo.GetAsync($"/api/v1/workspaces/{Guid.NewGuid()}/connectors");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreign.Content.ReadAsStringAsync()).Should().NotContain(token);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
