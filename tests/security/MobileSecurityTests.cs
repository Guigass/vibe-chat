using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Identity;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class MobileSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Devices_stay_in_the_tenant_and_logout_does_not_store_a_token()
    {
        using var warmup = Client("demo");
        (await warmup.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var foreignId = Guid.NewGuid();
        await using (var seed = factory.CreateMigratorDbContext())
        {
            seed.MobileDevices.Add(new MobileDevice
            {
                Id = foreignId,
                TenantId = new TenantId(Guid.NewGuid()),
                UserId = new UserId(Guid.NewGuid()),
                Platform = "ios-arm64",
                AppVersion = "1.0.0",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        using var alice = Client("alice");
        var demo = warmup;
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var aliceDevice = await Register(alice, "ios-arm64", "mobile-sec-alice-" + suffix);
        var demoDevice = await Register(demo, "android-arm64", "mobile-sec-demo-" + suffix);

        var stolen = await Send(alice, HttpMethod.Delete, $"/api/v1/mobile/devices/{demoDevice}", "mobile-sec-steal-" + suffix);
        stolen.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var hidden = await Send(alice, HttpMethod.Delete, $"/api/v1/mobile/devices/{foreignId}", "mobile-sec-foreign-" + suffix);
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = await alice.GetFromJsonAsync<DeviceDto[]>("/api/v1/mobile/devices", Json);
        var ids = (list ?? []).Select(item => item.Id).ToArray();
        ids.Should().NotContain(foreignId);
        ids.Should().Contain(aliceDevice);

        var remote = await Send(demo, HttpMethod.Delete, $"/api/v1/mobile/devices/{aliceDevice}", "mobile-sec-remote-" + suffix);
        remote.StatusCode.Should().Be(HttpStatusCode.OK, await remote.Content.ReadAsStringAsync());
        var wiped = await remote.Content.ReadFromJsonAsync<DeviceDto>(Json);
        wiped!.Revoked.Should().BeTrue();
        wiped.WipeSecureStorage.Should().BeTrue();
        (await remote.Content.ReadAsStringAsync()).Should().NotContain("access_token").And.NotContain("Bearer");

        await using var check = factory.CreateMigratorDbContext();
        var foreign = await check.MobileDevices.IgnoreQueryFilters().SingleAsync(x => x.Id == foreignId);
        foreign.Platform.Should().Be("ios-arm64");
        var audit = await check.AuditEvents.IgnoreQueryFilters()
            .SingleAsync(x => x.Action == AuditActions.MobileDeviceLogout && x.EntityId == aliceDevice.ToString("D"));
        audit.MetadataJson.Should().Be("{}");
        typeof(MobileDevice).GetProperties().Should().NotContain(property =>
            property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Guid> Register(HttpClient client, string platform, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/mobile/devices")
        {
            Content = new StringContent($$"""{"platform":"{{platform}}","appVersion":"1.2.3"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeviceDto>(Json))!.Id;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string key)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private sealed class DeviceDto
    {
        public Guid Id { get; set; }
        public bool Revoked { get; set; }
        public bool WipeSecureStorage { get; set; }
    }
}
