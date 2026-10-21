using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using VibeChat.Administration;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-146 — admin reads objectives; members and a closed flag do not.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class CapacitySloIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Owner_sees_objectives_and_a_measured_sample_returns_burn_rate()
    {
        using var demo = Client("demo");
        var dashboard = await demo.GetAsync($"/api/v1/workspaces/{WorkspaceId}/capacity");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await dashboard.Content.ReadAsStringAsync();
        body.Should().Contain("\"isSla\":false")
            .And.Contain("\"sampleKind\":\"unmeasured\"")
            .And.Contain("0.999")
            .And.Contain("adr-015-outbox")
            .And.NotContain("@");

        var evaluation = await demo.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/capacity/evaluations", new
        {
            schemaVersion = 1,
            profile = "small",
            workload = "send",
            sampleKind = "measured",
            capturedAt = "2026-10-21T12:00:00Z",
            commit = "57dfeb6",
            environment = "compose-lab",
            errorMargin = "planning",
            isSla = false,
            observations = new { apiAvailability = 0.999 }
        });
        evaluation.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var valid = await demo.PostAsync(
            $"/api/v1/workspaces/{WorkspaceId}/capacity/evaluations",
            new StringContent("""
                {
                  "schemaVersion": 1,
                  "profile": "small",
                  "workload": "history",
                  "sampleKind": "measured",
                  "capturedAt": "2026-10-21T12:00:00Z",
                  "commit": "57dfeb6",
                  "environment": "compose-lab",
                  "errorMargin": "planning",
                  "isSla": false,
                  "observations": { "api-availability": 0.999, "outbox-lag": 5000, "search-lag": 10000, "reconnect": 3000 }
                }
                """, System.Text.Encoding.UTF8, "application/json"));
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        var measured = await valid.Content.ReadAsStringAsync();
        measured.Should().Contain("\"burnRate\":1").And.Contain("\"isSla\":false").And.NotContain("@");

        var sla = await demo.PostAsync(
            $"/api/v1/workspaces/{WorkspaceId}/capacity/evaluations",
            new StringContent("""
                {
                  "schemaVersion": 1,
                  "profile": "small",
                  "workload": "history",
                  "sampleKind": "measured",
                  "capturedAt": "2026-10-21T12:00:00Z",
                  "commit": "57dfeb6",
                  "environment": "compose-lab",
                  "errorMargin": "planning",
                  "isSla": true,
                  "observations": { "api-availability": 0.999 }
                }
                """, System.Text.Encoding.UTF8, "application/json"));
        sla.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sla.Content.ReadAsStringAsync()).Should().Contain(CapacitySloErrors.SlaRejected);
        measured.Should().NotContain("@");
    }

    [Fact]
    public async Task Member_and_a_foreign_workspace_do_not_receive_the_catalog()
    {
        using var alice = Client("alice");
        var denied = await alice.GetAsync($"/api/v1/workspaces/{WorkspaceId}/capacity");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var demo = Client("demo");
        var foreign = await demo.GetAsync($"/api/v1/workspaces/{Guid.NewGuid()}/capacity");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreign.Content.ReadAsStringAsync()).Should().NotContain("adr-015-outbox");
    }

    [Fact]
    public async Task Disabled_flag_hides_the_dashboard_from_the_owner()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:CapacitySlo:Enabled", "false"));
        using var demo = host.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var response = await demo.GetAsync($"/api/v1/workspaces/{WorkspaceId}/capacity");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(CapacitySloErrors.Disabled);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }
}
