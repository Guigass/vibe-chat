using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class BrandingIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Save_is_idempotent_and_rollback_restores_the_previous_brand()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var baseline = await Send(demo, new { displayName = "Base " + suffix, brandColor = "#0f766e", accentColor = "#0d9488" }, "brand-int-base-" + suffix);
        baseline.StatusCode.Should().Be(HttpStatusCode.OK, await baseline.Content.ReadAsStringAsync());

        var first = await Send(demo, new { displayName = "Acme " + suffix, brandColor = "#0d9488", accentColor = "#0f766e" }, "brand-int-1-" + suffix);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var saved = await first.Content.ReadFromJsonAsync<BrandDto>(Json);
        saved!.DisplayName.Should().Be("Acme " + suffix);
        saved.AssetVersion.Should().BeGreaterThan(0);
        saved.HasRollback.Should().BeTrue();
        saved.ProductName.Should().Be(BrandingRules.ProductName);

        var replay = await Send(demo, new { displayName = "Acme " + suffix, brandColor = "#0d9488", accentColor = "#0f766e" }, "brand-int-1-" + suffix);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<BrandDto>(Json))!.AssetVersion.Should().Be(saved.AssetVersion);

        var conflict = await Send(demo, new { displayName = "Other " + suffix, brandColor = "#0f766e", accentColor = "#0d9488" }, "brand-int-1-" + suffix);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadAsStringAsync()).Should().Contain(BrandingRules.IdempotencyConflict);

        var rolled = await SendEmpty(demo, HttpMethod.Post, "/api/v1/admin/branding/rollback", "brand-int-rollback-" + suffix);
        rolled.StatusCode.Should().Be(HttpStatusCode.OK, await rolled.Content.ReadAsStringAsync());
        var restored = await rolled.Content.ReadFromJsonAsync<BrandDto>(Json);
        restored!.DisplayName.Should().Be("Base " + suffix);
        restored.BrandColor.Should().Be("#0f766e");
        restored.ProductName.Should().Be("VibeChat");

        (await Send(demo, new { displayName = (string?)null, brandColor = (string?)null, accentColor = (string?)null }, "brand-int-clear-" + suffix))
            .EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/branding")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendEmpty(HttpClient client, HttpMethod method, string path, string key)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private sealed class BrandDto
    {
        public string? DisplayName { get; set; }
        public string? BrandColor { get; set; }
        public int AssetVersion { get; set; }
        public bool HasRollback { get; set; }
        public string? ProductName { get; set; }
    }
}
