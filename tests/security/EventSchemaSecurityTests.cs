using System.Net;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-139 — catalog has no tenant payload; members cannot read it.</summary>
[Collection(SecurityCollection.Name)]
public sealed class EventSchemaSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Member_cannot_read_event_schemas_and_the_catalog_has_no_tenant_data()
    {
        using var bob = factory.CreateClient();
        bob.DefaultRequestHeaders.Add("X-Dev-User", "bob");
        var denied = await bob.GetAsync("/api/v1/admin/event-schemas");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = factory.CreateClient();
        var unauthenticated = await anonymous.GetAsync("/api/v1/admin/event-schemas");
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Integrations:EventSchemas:Enabled", "true"));
        using var demo = host.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var list = await demo.GetStringAsync("/api/v1/admin/event-schemas");
        list.Should().Contain(PublicEventCatalog.MessageCreated);
        list.Should().NotContain(SeedData.DemoTenantId.Value.ToString());
        list.Should().NotContain(SeedData.DemoUserId.Value.ToString());
        list.Should().Contain("00000000-0000-0000-0000-000000000001");

        using var bobEnabled = host.CreateClient();
        bobEnabled.DefaultRequestHeaders.Add("X-Dev-User", "bob");
        var stillDenied = await bobEnabled.GetAsync("/api/v1/admin/event-schemas");
        stillDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
