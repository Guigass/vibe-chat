using System.Net;
using System.Text;
using FluentAssertions;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-146 — capacity objectives stay inside workspace.admin and carry no PII.</summary>
[Collection(SecurityCollection.Name)]
public sealed class CapacitySloSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_cannot_read_capacity_and_the_envelope_drops_pii()
    {
        using var alice = Client("alice");
        var denied = await alice.GetAsync($"/api/v1/workspaces/{WorkspaceId}/capacity");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("0.999");

        using var bob = Client("bob");
        var posted = await bob.PostAsync(
            $"/api/v1/workspaces/{WorkspaceId}/capacity/evaluations",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        posted.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var demo = Client("demo");
        var body = await demo.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/capacity");
        body.Should().NotContain("alice@").And.NotContain("bob@").And.NotContain("demo@");

        var leaked = await demo.PostAsync(
            $"/api/v1/workspaces/{WorkspaceId}/capacity/evaluations",
            new StringContent("""
                {
                  "schemaVersion": 1,
                  "profile": "small",
                  "workload": "send",
                  "sampleKind": "measured",
                  "capturedAt": "2026-10-21T12:00:00Z",
                  "commit": "57dfeb6",
                  "environment": "compose-lab",
                  "errorMargin": "planning",
                  "isSla": false,
                  "email": "alice@vibechat.local",
                  "observations": { "api-availability": 0.999 }
                }
                """, Encoding.UTF8, "application/json"));
        leaked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await leaked.Content.ReadAsStringAsync()).Should().Contain(CapacitySloErrors.EnvelopeRejected);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
