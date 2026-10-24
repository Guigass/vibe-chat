using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using VibeChat.Identity;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class DesktopIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid ChannelId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Disabled_flag_hides_desktop_and_leaves_chat()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Desktop:Enabled", "false"));
        using var demo = Client(disabled, "demo");

        var hidden = await demo.GetAsync("/api/v1/desktop/shell");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().Contain(DesktopErrors.Disabled);

        (await demo.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Shell_link_update_register_preferences_and_logout()
    {
        using var demo = Client(factory, "demo");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var shellResponse = await demo.GetAsync("/api/v1/desktop/shell");
        shellResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var shellBody = await shellResponse.Content.ReadAsStringAsync();
        var shell = JsonSerializer.Deserialize<ShellDto>(shellBody, Json);
        shell!.UiFork.Should().BeFalse();
        shell.ApiBase.Should().Be("/api/v1");
        shell.Updates.Should().Be("feed");
        shell.SignedBuilds.Should().BeTrue();
        shell.Signing.Should().Be("ready");
        shell.BrowserFallback.Should().BeTrue();
        shell.LaunchAtLoginDefault.Should().BeFalse();
        shellBody.Should().NotContain(DesktopTestKeys.PublicKey);
        shellBody.Should().NotContain("operator/desktop-signing");

        var link = await demo.PostAsJsonAsync("/api/v1/desktop/deep-links/validate", new
        {
            url = $"vibechat://chat.example/v1/channel/{ChannelId:D}"
        });
        link.StatusCode.Should().Be(HttpStatusCode.OK, await link.Content.ReadAsStringAsync());
        (await link.Content.ReadFromJsonAsync<LinkDto>(Json))!.Kind.Should().Be("channel");

        var attack = await demo.PostAsJsonAsync("/api/v1/desktop/deep-links/validate", new
        {
            url = "vibechat://evil.example/v1/channel/" + ChannelId.ToString("D")
        });
        attack.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await attack.Content.ReadAsStringAsync()).Should().Contain(DesktopErrors.DeepLinkRejected);

        var update = await SendJson(demo, HttpMethod.Post, "/api/v1/desktop/updates/verify", SignedUpdate("updates.example"));
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        (await update.Content.ReadFromJsonAsync<UpdateDto>(Json))!.Accepted.Should().BeTrue();

        var forged = await SendJson(demo, HttpMethod.Post, "/api/v1/desktop/updates/verify", SignedUpdate("updates.example", corrupt: true));
        forged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await forged.Content.ReadAsStringAsync()).Should().Contain(DesktopErrors.UpdateRejected);

        var leaked = await SendJson(demo, HttpMethod.Post, "/api/v1/desktop/devices",
            """{"platform":"linux-x64","appVersion":"1.0.0","access_token":"should-not-persist"}""",
            "desktop-token-" + suffix);
        leaked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await leaked.Content.ReadAsStringAsync()).Should().Contain(DesktopErrors.TokenRejected);

        var created = await SendJson(demo, HttpMethod.Post, "/api/v1/desktop/devices",
            """{"platform":"linux-x64","appVersion":"1.2.3"}""",
            "desktop-reg-" + suffix);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var device = await created.Content.ReadFromJsonAsync<DeviceDto>(Json);
        device!.Platform.Should().Be("linux-x64");
        device.LaunchAtLogin.Should().BeFalse();
        device.WipeSecureStorage.Should().BeFalse();
        (await created.Content.ReadAsStringAsync()).Should().NotContain("access_token");

        var replay = await SendJson(demo, HttpMethod.Post, "/api/v1/desktop/devices",
            """{"platform":"linux-x64","appVersion":"1.2.3"}""",
            "desktop-reg-" + suffix);
        (await replay.Content.ReadFromJsonAsync<DeviceDto>(Json))!.Id.Should().Be(device.Id);

        var conflict = await SendJson(demo, HttpMethod.Post, "/api/v1/desktop/devices",
            """{"platform":"windows-x64","appVersion":"1.2.3"}""",
            "desktop-reg-" + suffix);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var prefs = await SendJson(demo, HttpMethod.Put, $"/api/v1/desktop/devices/{device.Id}/preferences",
            """{"launchAtLogin":true,"notifications":true,"tray":false}""",
            "desktop-prefs-" + suffix);
        prefs.StatusCode.Should().Be(HttpStatusCode.OK, await prefs.Content.ReadAsStringAsync());
        (await prefs.Content.ReadFromJsonAsync<DeviceDto>(Json))!.LaunchAtLogin.Should().BeTrue();

        var loggedOut = await Send(demo, HttpMethod.Delete, $"/api/v1/desktop/devices/{device.Id}", "desktop-out-" + suffix);
        loggedOut.StatusCode.Should().Be(HttpStatusCode.OK, await loggedOut.Content.ReadAsStringAsync());
        var wiped = await loggedOut.Content.ReadFromJsonAsync<DeviceDto>(Json);
        wiped!.Revoked.Should().BeTrue();
        wiped.WipeSecureStorage.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_feed_rejects_updates()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Desktop:UpdateFeedUrl", ""));
        using var demo = Client(host, "demo");
        var response = await SendJson(demo, HttpMethod.Post, "/api/v1/desktop/updates/verify", SignedUpdate("updates.example"));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(DesktopErrors.UpdateOff);
    }

    private static string SignedUpdate(string feedHost, bool corrupt = false)
    {
        var sha = new string('a', 64);
        var canonical = DesktopRules.CanonicalUpdate("1.2.3", feedHost, ["linux-x64"], sha);
        var signature = Convert.ToBase64String(DesktopTestKeys.SigningKey.SignData(
            Encoding.UTF8.GetBytes(canonical),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss));
        if (corrupt)
        {
            signature += "aa";
        }

        return JsonSerializer.Serialize(new
        {
            channel = "stable",
            productVersion = "1.2.3",
            feedHost,
            targets = new[] { "linux-x64" },
            sha256 = sha,
            signature
        });
    }

    private static HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static Task<HttpResponseMessage> SendJson(HttpClient client, HttpMethod method, string path, string json, string? key = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string key)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private sealed class ShellDto
    {
        public bool UiFork { get; set; }
        public string? ApiBase { get; set; }
        public string? Updates { get; set; }
        public bool SignedBuilds { get; set; }
        public string? Signing { get; set; }
        public bool BrowserFallback { get; set; }
        public bool LaunchAtLoginDefault { get; set; }
    }

    private sealed class LinkDto
    {
        public string? Kind { get; set; }
    }

    private sealed class UpdateDto
    {
        public bool Accepted { get; set; }
    }

    private sealed class DeviceDto
    {
        public Guid Id { get; set; }
        public string? Platform { get; set; }
        public bool LaunchAtLogin { get; set; }
        public bool Revoked { get; set; }
        public bool WipeSecureStorage { get; set; }
    }
}
