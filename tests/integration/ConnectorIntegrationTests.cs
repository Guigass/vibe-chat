using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ConnectorIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Inbound_is_idempotent_revoke_stops_calls_and_upstream_failure_skips_outbox()
    {
        var sandbox = ConnectorSandbox.Shared;
        sandbox.Reset();
        using var demo = Client("demo");
        var catalog = await demo.GetFromJsonAsync<CatalogItem[]>($"/api/v1/workspaces/{WorkspaceId}/connectors/catalog", JsonOptions);
        catalog!.Single(item => item.Id == "github").Scopes.Should().Equal("issues:read", "issues:write");

        const string token = "connector-token-test-0001";
        var title = $"Build{Guid.NewGuid():N}"[..12];
        var created = await CreateAsync(demo, "github", "channel", ChannelId, ["issues:read", "issues:write"], token, null, null);
        var body = await demo.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/connectors");
        body.Should().Contain("issues:read").And.Contain("issues:write").And.NotContain(token);
        created.SecretMask.Should().NotBeNullOrWhiteSpace().And.NotContain(token);

        var payload = "{\"tenantId\":\"00000000-0000-0000-0000-000000000099\",\"issue\":{\"id\":42,\"title\":\"" + title + "\"}}";
        var first = await InboundAsync(created.InboundPath, payload, token);
        var second = await InboundAsync(created.InboundPath, payload, token);
        second.EffectId.Should().Be(first.EffectId);
        second.Duplicate.Should().BeTrue();

        await using var db = factory.CreateMigratorDbContext();
        var summary = $"github 42: {title}";
        (await db.Messages.IgnoreQueryFilters().CountAsync(row => row.Body == summary)).Should().Be(1);
        var effect = await db.ConnectorEffects.IgnoreQueryFilters().SingleAsync(row => row.Id == first.EffectId);
        effect.TenantId.Should().Be(SeedData.DemoTenantId);

        var delivered = await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/connectors/{created.Id}/deliveries", null);
        delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());
        (await delivered.Content.ReadFromJsonAsync<DeliveryDto>(JsonOptions))!.Ok.Should().BeTrue();

        var rotated = await PostOk<InstallationDto>(demo, $"/api/v1/workspaces/{WorkspaceId}/connectors/{created.Id}/rotate", new
        {
            token = "connector-token-test-0002",
        });
        rotated.SecretVersion.Should().BeGreaterThan(created.SecretVersion);
        var stale = await InboundRawAsync(created.InboundPath, """{"issue":{"id":43,"title":"Nope"}}""", token);
        stale.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await PostOk<InstallationDto>(demo, $"/api/v1/workspaces/{WorkspaceId}/connectors/{created.Id}/revoke", new { });
        var callsAfterRevoke = sandbox.Calls.Count;
        var revokedInbound = await InboundRawAsync(created.InboundPath, payload, "connector-token-test-0002");
        revokedInbound.StatusCode.Should().Be(HttpStatusCode.Gone);
        var revokedDelivery = await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/connectors/{created.Id}/deliveries", null);
        (await revokedDelivery.Content.ReadAsStringAsync()).Should().Contain("ConnectorRevoked");
        sandbox.Calls.Count.Should().Be(callsAfterRevoke);

        var metadata = await CreateRawAsync(demo, "calendar", "channel", ChannelId, ["calendar:read", "calendar:write"], token, null, "https://169.254.169.254/dav");
        metadata.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await metadata.Content.ReadAsStringAsync()).Should().Contain("EgressBlocked");
        var rebind = await CreateRawAsync(demo, "calendar", "channel", ChannelId, ["calendar:read", "calendar:write"], token, null, "https://rebind.example/dav");
        rebind.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rebind.Content.ReadAsStringAsync()).Should().Contain("DnsRebinding");

        var oauth = await CreateAsync(demo, "github", "channel", ChannelId, ["issues:read", "issues:write"], null, "github-client-1", null);
        var start = await PostOk<OauthStartDto>(demo, $"/api/v1/workspaces/{WorkspaceId}/connectors/{oauth.Id}/oauth/start", new { });
        start.Scopes.Should().Equal("issues:read", "issues:write");
        start.AuthorizeUrl.Should().Contain("issues:read").And.Contain("issues:write");
        var state = Uri.UnescapeDataString(new Uri(start.AuthorizeUrl).Query.TrimStart('?')
            .Split('&')
            .Select(part => part.Split('=', 2))
            .First(part => part[0] == "state")[1]);
        var callbackResponse = await demo.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/connectors/{oauth.Id}/oauth/callback", new
        {
            code = "sandbox-code-01",
            state,
        });
        var callbackBody = await callbackResponse.Content.ReadAsStringAsync();
        callbackResponse.StatusCode.Should().Be(HttpStatusCode.OK, callbackBody);
        callbackBody.Should().NotContain(ConnectorSandbox.AccessToken);
        callbackBody.Should().Contain("••••");

        sandbox.Mode = "redirect-metadata";
        var redirected = await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/connectors/{oauth.Id}/deliveries", null);
        (await redirected.Content.ReadAsStringAsync()).Should().Contain("EgressBlocked");
        sandbox.Calls.Should().NotContain(uri => uri.Host == "169.254.169.254");

        sandbox.Reset();
        sandbox.Mode = "fail";
        var failing = await CreateAsync(demo, "github", "channel", ChannelId, ["issues:read", "issues:write"], token, null, null);
        var before = await db.OutboxMessages.IgnoreQueryFilters().CountAsync();
        var failed = await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/connectors/{failing.Id}/deliveries", null);
        failed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await failed.Content.ReadAsStringAsync()).Should().Contain("UpstreamFailed");
        var backedOff = await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/connectors/{failing.Id}/deliveries", null);
        (await backedOff.Content.ReadAsStringAsync()).Should().Contain("Backoff");
        sandbox.Calls.Count.Should().Be(1);
        (await db.OutboxMessages.IgnoreQueryFilters().CountAsync()).Should().Be(before);

        await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/connectors/{failing.Id}/reconnect", null);
        await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/connectors/{failing.Id}/reconnect", null);
        var open = await demo.PostAsync($"/api/v1/workspaces/{WorkspaceId}/connectors/{failing.Id}/reconnect", null);
        (await open.Content.ReadAsStringAsync()).Should().Contain("CircuitOpen");
        sandbox.Calls.Count.Should().Be(3);

        var messageId = Guid.NewGuid();
        var sent = await demo.PostAsJsonAsync($"/api/v1/channels/{ChannelId}/messages", new
        {
            messageId,
            idempotencyKey = $"idem-{messageId:N}",
            body = $"after-connector-{messageId:N}",
        });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync());
        (await db.OutboxMessages.IgnoreQueryFilters().CountAsync()).Should().BeGreaterThan(before);
        (await db.Messages.IgnoreQueryFilters().AnyAsync(row => row.Id == new VibeChat.SharedKernel.MessageId(messageId))).Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_flag_does_not_write()
    {
        using var disabledHost = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Connectors:Enabled", "false"));
        using var disabled = disabledHost.CreateClient();
        disabled.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var off = await disabled.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/connectors", new
        {
            idempotencyKey = $"off-{Guid.NewGuid():N}"[..20],
            provider = "github",
            destinationKind = "channel",
            destinationId = ChannelId,
            scopes = new[] { "issues:read", "issues:write" },
            token = "connector-token-test-0001",
        });
        off.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await off.Content.ReadAsStringAsync()).Should().Contain("ConnectorDisabled");
        await using var db = factory.CreateMigratorDbContext();
        (await db.ConnectorInstallations.IgnoreQueryFilters().CountAsync(row => row.IdempotencyKey.StartsWith("off-"))).Should().Be(0);
    }

    private async Task<InstallationDto> CreateAsync(
        HttpClient client,
        string provider,
        string kind,
        Guid destinationId,
        string[] scopes,
        string? token,
        string? clientId,
        string? baseUrl)
    {
        var response = await CreateRawAsync(client, provider, kind, destinationId, scopes, token, clientId, baseUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InstallationDto>(JsonOptions))!;
    }

    private static Task<HttpResponseMessage> CreateRawAsync(
        HttpClient client,
        string provider,
        string kind,
        Guid destinationId,
        string[] scopes,
        string? token,
        string? clientId,
        string? baseUrl) =>
        client.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/connectors", new
        {
            idempotencyKey = $"cx-{Guid.NewGuid():N}"[..24],
            provider,
            destinationKind = kind,
            destinationId,
            scopes,
            token,
            clientId,
            baseUrl,
        });

    private async Task<InboundDto> InboundAsync(string path, string payload, string token)
    {
        var response = await InboundRawAsync(path, payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InboundDto>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> InboundRawAsync(string path, string payload, string token)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(
            ConnectorPolicies.SignatureHeader,
            ConnectorPolicies.ComputeSignature(token, payload));
        return await client.SendAsync(request);
    }

    private async Task<T> PostOk<T>(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record CatalogItem(string Id, string[] Scopes, bool Oauth);
    private sealed record InstallationDto(Guid Id, string? SecretMask, int SecretVersion, string InboundPath, string[] Scopes);
    private sealed record InboundDto(Guid EffectId, bool Duplicate);
    private sealed record DeliveryDto(bool Ok, string? Error);
    private sealed record OauthStartDto(string AuthorizeUrl, string[] Scopes);
}
