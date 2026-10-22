using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-111 — history scope, member authZ and hostile attachments.</summary>
[Collection(SecurityCollection.Name)]
public sealed class PluginGovernedSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Member_cannot_grant_history_and_a_token_cannot_see_another_channel()
    {
        using var admin = Client("demo");
        var created = await InstallAsync(admin);
        created.Token.Should().NotBeNullOrWhiteSpace();
        using var bob = Client("bob");
        var denied = await bob.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{created.Id}/history-grants",
            new
            {
                channelId = ChannelId,
                windowStart = DateTimeOffset.UtcNow.AddMinutes(-1),
                windowEnd = DateTimeOffset.UtcNow.AddMinutes(1)
            });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var bot = BotClient(created.Token!);
        var foreign = await bot.GetAsync(HistoryUrl(Guid.NewGuid()));
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().Contain(PluginPlatformErrors.HistoryGrantNotFound);

        var outside = await bot.GetAsync(HistoryUrl(ChannelId));
        outside.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var script = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/attachments",
            new { fileName = "run.js", contentType = "application/javascript", sizeBytes = 8, idempotencyKey = $"sec-{Guid.NewGuid():N}" });
        script.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await script.Content.ReadAsStringAsync()).Should().Contain(PluginPlatformErrors.AttachmentRejected);

        var metadata = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new
            {
                manifest = new
                {
                    schema = PluginManifestRules.SchemaV1,
                    id = "metadata-action",
                    name = "Metadata Action",
                    version = "1.0.0",
                    capabilities = new[] { PluginManifestRules.CapabilityMessagesInteract },
                    actions = new[] { new { name = "ping", description = "Ping" } },
                    callback = new { url = "https://169.254.169.254/latest" }
                },
                channelIds = new[] { ChannelId },
                allowDms = false
            });
        metadata.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await metadata.Content.ReadAsStringAsync()).Should().Contain(PluginPlatformErrors.EgressBlocked);

        await admin.DeleteAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{created.Id}");
    }

    private static string HistoryUrl(Guid channelId)
    {
        var start = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
        var end = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));
        return $"/api/v1/integrations/v1/channels/{channelId}/history?from={start}&to={end}&idempotencyKey=sec-{Guid.NewGuid():N}";
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private HttpClient BotClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<InstalledDto> InstallAsync(HttpClient admin)
    {
        var slug = "h" + Guid.NewGuid().ToString("N")[..8];
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new
            {
                manifest = new
                {
                    schema = PluginManifestRules.SchemaV1,
                    id = slug,
                    name = "History Gate",
                    version = "1.0.0",
                    capabilities = new[] { PluginManifestRules.CapabilityHistoryRead, PluginManifestRules.CapabilityAttachmentsSend }
                },
                channelIds = new[] { ChannelId },
                allowDms = false
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InstalledDto>(Json))!;
    }

    private sealed record InstalledDto(Guid Id, string PluginId, string? Token);
}
