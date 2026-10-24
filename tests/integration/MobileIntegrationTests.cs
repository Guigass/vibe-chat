using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using VibeChat.Identity;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MobileIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid ChannelId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Disabled_flag_hides_mobile_and_leaves_chat()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Mobile:Enabled", "false"));
        using var demo = Client(disabled, "demo");

        var hidden = await demo.GetAsync("/api/v1/mobile/shell");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().Contain(MobileErrors.Disabled);

        (await demo.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Shell_link_share_push_register_preferences_and_logout()
    {
        using var demo = Client(factory, "demo");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var shellResponse = await demo.GetAsync("/api/v1/mobile/shell");
        shellResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var shellBody = await shellResponse.Content.ReadAsStringAsync();
        var shell = JsonSerializer.Deserialize<ShellDto>(shellBody, Json);
        shell!.UiFork.Should().BeFalse();
        shell.ApiBase.Should().Be("/api/v1");
        shell.Distribution.Should().Be("sideload");
        shell.Store.Should().Be("off");
        shell.PushContent.Should().Be("ids-only");
        shell.SideloadReady.Should().BeTrue();
        shell.BiometricLockDefault.Should().BeFalse();
        shell.BrowserFallback.Should().BeTrue();
        shell.Targets.Should().Equal("android-arm64", "ios-arm64");

        var link = await demo.PostAsJsonAsync("/api/v1/mobile/deep-links/validate", new
        {
            url = $"vibechat://chat.example/v1/channel/{ChannelId:D}"
        });
        link.StatusCode.Should().Be(HttpStatusCode.OK, await link.Content.ReadAsStringAsync());
        (await link.Content.ReadFromJsonAsync<LinkDto>(Json))!.Kind.Should().Be("channel");

        var attack = await demo.PostAsJsonAsync("/api/v1/mobile/deep-links/validate", new
        {
            url = "vibechat://evil.example/v1/channel/" + ChannelId.ToString("D")
        });
        attack.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await attack.Content.ReadAsStringAsync()).Should().Contain(MobileErrors.DeepLinkRejected);

        var share = await demo.PostAsJsonAsync("/api/v1/mobile/share/validate", new
        {
            host = "chat.example",
            kind = "thread",
            id = ChannelId,
            confirm = true
        });
        share.StatusCode.Should().Be(HttpStatusCode.OK, await share.Content.ReadAsStringAsync());
        (await share.Content.ReadFromJsonAsync<ShareDto>(Json))!.SendsMessage.Should().BeFalse();

        var leakedShare = await SendJson(demo, HttpMethod.Post, "/api/v1/mobile/share/validate",
            """{"host":"chat.example","kind":"channel","id":"22222222-2222-2222-2222-222222222222","confirm":true,"tenantId":"11111111-1111-1111-1111-111111111111","text":"secret"}""");
        leakedShare.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await leakedShare.Content.ReadAsStringAsync()).Should().Contain(MobileErrors.TokenRejected);

        var push = await demo.PostAsJsonAsync("/api/v1/mobile/push/preview", new
        {
            kind = "message",
            conversationId = ChannelId
        });
        push.StatusCode.Should().Be(HttpStatusCode.OK, await push.Content.ReadAsStringAsync());
        var preview = await push.Content.ReadFromJsonAsync<PushDto>(Json);
        preview!.ContainsContent.Should().BeFalse();
        (await push.Content.ReadAsStringAsync()).Should().NotContain("secret");

        var leakedPush = await SendJson(demo, HttpMethod.Post, "/api/v1/mobile/push/preview",
            """{"kind":"message","conversationId":"22222222-2222-2222-2222-222222222222","text":"secret","fcmToken":"abc"}""");
        leakedPush.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await leakedPush.Content.ReadAsStringAsync()).Should().Contain(MobileErrors.TokenRejected);

        var leaked = await SendJson(demo, HttpMethod.Post, "/api/v1/mobile/devices",
            """{"platform":"ios-arm64","appVersion":"1.0.0","access_token":"should-not-persist"}""",
            "mobile-token-" + suffix);
        leaked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await leaked.Content.ReadAsStringAsync()).Should().Contain(MobileErrors.TokenRejected);

        var created = await SendJson(demo, HttpMethod.Post, "/api/v1/mobile/devices",
            """{"platform":"ios-arm64","appVersion":"1.2.3"}""",
            "mobile-reg-" + suffix);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var device = await created.Content.ReadFromJsonAsync<DeviceDto>(Json);
        device!.Platform.Should().Be("ios-arm64");
        device.BiometricLock.Should().BeFalse();
        device.WipeSecureStorage.Should().BeFalse();
        (await created.Content.ReadAsStringAsync()).Should().NotContain("access_token");

        var replay = await SendJson(demo, HttpMethod.Post, "/api/v1/mobile/devices",
            """{"platform":"ios-arm64","appVersion":"1.2.3"}""",
            "mobile-reg-" + suffix);
        (await replay.Content.ReadFromJsonAsync<DeviceDto>(Json))!.Id.Should().Be(device.Id);

        var conflict = await SendJson(demo, HttpMethod.Post, "/api/v1/mobile/devices",
            """{"platform":"android-arm64","appVersion":"1.2.3"}""",
            "mobile-reg-" + suffix);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var android = await SendJson(demo, HttpMethod.Post, "/api/v1/mobile/devices",
            """{"platform":"android-arm64","appVersion":"1.2.3"}""",
            "mobile-and-" + suffix);
        android.StatusCode.Should().Be(HttpStatusCode.OK, await android.Content.ReadAsStringAsync());

        var prefs = await SendJson(demo, HttpMethod.Put, $"/api/v1/mobile/devices/{device.Id}/preferences",
            """{"biometricLock":true,"notifications":false,"camera":false,"filePicker":false}""",
            "mobile-prefs-" + suffix);
        prefs.StatusCode.Should().Be(HttpStatusCode.OK, await prefs.Content.ReadAsStringAsync());
        (await prefs.Content.ReadFromJsonAsync<DeviceDto>(Json))!.BiometricLock.Should().BeTrue();

        var loggedOut = await Send(demo, HttpMethod.Delete, $"/api/v1/mobile/devices/{device.Id}", "mobile-out-" + suffix);
        loggedOut.StatusCode.Should().Be(HttpStatusCode.OK, await loggedOut.Content.ReadAsStringAsync());
        var wiped = await loggedOut.Content.ReadFromJsonAsync<DeviceDto>(Json);
        wiped!.Revoked.Should().BeTrue();
        wiped.WipeSecureStorage.Should().BeTrue();
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
        public string? Distribution { get; set; }
        public string? Store { get; set; }
        public string? PushContent { get; set; }
        public bool SideloadReady { get; set; }
        public bool BiometricLockDefault { get; set; }
        public bool BrowserFallback { get; set; }
        public string[]? Targets { get; set; }
    }

    private sealed class LinkDto
    {
        public string? Kind { get; set; }
    }

    private sealed class ShareDto
    {
        public bool SendsMessage { get; set; }
    }

    private sealed class PushDto
    {
        public bool ContainsContent { get; set; }
    }

    private sealed class DeviceDto
    {
        public Guid Id { get; set; }
        public string? Platform { get; set; }
        public bool BiometricLock { get; set; }
        public bool Revoked { get; set; }
        public bool WipeSecureStorage { get; set; }
    }
}
