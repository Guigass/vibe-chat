using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class BrandingSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Brand_stays_in_the_tenant_and_public_host_ignores_query_tenant()
    {
        var foreignId = Guid.NewGuid();
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.BrandSettings.Add(new BrandSettings
            {
                TenantId = new TenantId(foreignId),
                DisplayName = "Foreign Brand",
                AssetVersion = 1,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var demo = Client("demo");
        var saved = await Send(demo, HttpMethod.Put, "/api/v1/admin/branding", new
        {
            displayName = "Acme",
            brandColor = "#0f766e",
            accentColor = "#0d9488"
        }, "brand-sec-1");
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var body = await saved.Content.ReadFromJsonAsync<BrandDto>(Json);
        body!.DisplayName.Should().Be("Acme");
        body.ProductName.Should().Be(BrandingRules.ProductName);
        body.Fallback.Should().BeFalse();

        using var alice = Client("alice");
        var denied = await alice.PutAsJsonAsync("/api/v1/admin/branding", new { displayName = "Nope", brandColor = "#0f766e" });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var seen = await alice.GetFromJsonAsync<BrandDto>("/api/v1/branding", Json);
        seen!.DisplayName.Should().Be("Acme");
        seen.ProductName.Should().Be("VibeChat");

        var leaked = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/public/branding?tenantId={foreignId}");
        leaked.Headers.Host = "unknown.test";
        var hidden = await alice.SendAsync(leaked);
        hidden.StatusCode.Should().Be(HttpStatusCode.OK);
        var official = await hidden.Content.ReadFromJsonAsync<BrandDto>(Json);
        official!.DisplayName.Should().BeNull();
        official.Fallback.Should().BeTrue();
        official.ProductName.Should().Be("VibeChat");

        var mapped = await alice.GetFromJsonAsync<BrandDto>("/api/v1/public/branding?tenantId=" + foreignId, Json);
        mapped!.DisplayName.Should().Be("Acme");

        await using var check = factory.CreateMigratorDbContext();
        var foreign = await check.BrandSettings.IgnoreQueryFilters().SingleAsync(x => x.TenantId == new TenantId(foreignId));
        foreign.DisplayName.Should().Be("Foreign Brand");

        (await Send(demo, HttpMethod.Put, "/api/v1/admin/branding", new
        {
            displayName = (string?)null,
            brandColor = (string?)null,
            accentColor = (string?)null
        }, "brand-sec-clear")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Invalid_color_and_file_are_rejected_and_missing_asset_falls_back()
    {
        using var demo = Client("demo");
        var weak = await Send(demo, HttpMethod.Put, "/api/v1/admin/branding", new
        {
            displayName = "Acme",
            brandColor = "#ffffff",
            accentColor = "#0f766e"
        }, "brand-sec-weak");
        weak.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await weak.Content.ReadAsStringAsync()).Should().Contain(BrandingRules.ContrastFailed);

        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent("<svg><script>alert(1)</script></svg>"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/svg+xml");
        upload.Add(file, "file", "logo.svg");
        var rejected = await Send(demo, HttpMethod.Post, "/api/v1/admin/branding/assets/logo-light", upload, "brand-sec-svg");
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var png = new MultipartFormDataContent();
        var image = new ByteArrayContent(Png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        png.Add(image, "file", "logo.png");
        var ok = await Send(demo, HttpMethod.Post, "/api/v1/admin/branding/assets/logo-light", png, "brand-sec-png");
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var saved = await ok.Content.ReadFromJsonAsync<BrandDto>(Json);
        var bytes = await demo.GetByteArrayAsync(saved!.LogoLightUrl);
        bytes.Should().Equal(Png);

        (await Send(demo, HttpMethod.Delete, "/api/v1/admin/branding/assets/logo-light", null, "brand-sec-del")).EnsureSuccessStatusCode();
        (await demo.GetAsync("/api/v1/branding/assets/logo-light")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var after = await demo.GetFromJsonAsync<BrandDto>("/api/v1/admin/branding", Json);
        after!.LogoLightUrl.Should().BeNull();
        after.ProductName.Should().Be("VibeChat");
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, HttpContent? content, string key)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, string key) =>
        Send(client, method, path, JsonContent.Create(body), key);

    private sealed class BrandDto
    {
        public string? DisplayName { get; set; }
        public string? LogoLightUrl { get; set; }
        public string? ProductName { get; set; }
        public bool Fallback { get; set; }
    }
}
