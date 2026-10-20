using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class DlpIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Block_stops_share_guest_and_webhook_before_the_effect_and_simulation_measures()
    {
        using var demo = Client("demo");
        using var alice = Client("alice");
        var channel = await CreateChannelAsync(demo);
        Guid? endpointId = null;
        try
        {
            (await PutAsync(demo, Policy(), new { simulation = false })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            await GrantAsync(demo, SeedData.AliceUserId.Value);
            (await PutAsync(alice, Policy(), new { simulation = false })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutAsync(alice, Assignments(), new { scopeKind = "channel", scopeId = channel, labelKey = "restricted" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutAsync(alice, Rules(), new { action = "share", minRank = 3, effect = "block" })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutAsync(alice, Rules(), new { action = "guest", minRank = 3, effect = "block" })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutAsync(alice, Rules(), new { action = "webhook", minRank = 3, effect = "block" })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutAsync(alice, Rules(), new { action = "ai", minRank = 3, effect = "block" })).StatusCode.Should().Be(HttpStatusCode.OK);

            var messageId = Guid.NewGuid();
            var send = await alice.PostAsJsonAsync(
                $"/api/v1/channels/{channel}/messages",
                new { messageId, idempotencyKey = $"idem-{messageId:N}", body = $"dlp-{messageId:N}" });
            send.StatusCode.Should().Be(HttpStatusCode.Accepted);

            var target = await CreateChannelAsync(demo);
            var blockedShare = await PostAsync(alice, Forward(messageId), new { idempotencyKey = $"fwd-{messageId:N}", targetChannelIds = new[] { target } });
            blockedShare.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await blockedShare.Content.ReadAsStringAsync()).Should().Contain(DlpErrors.Blocked);

            var blockedGuest = await PostAsync(demo, Invites(channel), new { email = "guest-dlp@example.com" });
            blockedGuest.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var blockedAi = await alice.PostAsJsonAsync(Summarize(channel), new { });
            blockedAi.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var created = await demo.PostAsJsonAsync("/api/v1/admin/webhooks", new
            {
                workspaceId = WorkspaceId,
                name = $"dlp-{channel:N}"[..20],
                url = "https://hooks.example.test/dlp",
                secret = "dlp-webhook-secret-1",
                enabled = true,
                channelFilter = new[] { channel }
            });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            endpointId = (await created.Content.ReadFromJsonAsync<IdDto>(Json))!.Id;

            using (var scope = factory.Services.CreateScope())
            {
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenant.SetTenant(SeedData.DemoTenantId);
                var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
                await RlsSession.EnsureAppliedAsync(db, tenant, CancellationToken.None);
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDispatcher>();
                await dispatcher.TryDispatchAsync(
                    SeedData.DemoTenantId,
                    "MessageCreated",
                    Guid.NewGuid(),
                    JsonSerializer.Serialize(new { body = "4111", messageId, channelId = channel }),
                    channel,
                    CancellationToken.None);
                await db.SaveChangesAsync();
                await RlsSession.CommitAsync(db);
            }

            await using (var db = factory.CreateMigratorDbContext())
            {
                var endpoint = await db.OutboundWebhookEndpoints.IgnoreQueryFilters().SingleAsync(x => x.Id == endpointId);
                endpoint.LastDeliveryAt.Should().BeNull();
                var decision = await db.DlpDecisions.IgnoreQueryFilters()
                    .Where(x => x.ChannelId == channel && x.Action == DlpActions.Webhook)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstAsync();
                decision.Effect.Should().Be(DlpEffects.Block);
                decision.ReasonCode.Should().Be(DlpReasons.Block);
                decision.Inspectable.Should().BeTrue();
            }

            (await PutAsync(alice, Policy(), new { simulation = true })).StatusCode.Should().Be(HttpStatusCode.OK);
            var simulated = await PostAsync(alice, Forward(messageId), new { idempotencyKey = $"fwd-sim-{messageId:N}", targetChannelIds = new[] { target } });
            simulated.StatusCode.Should().Be(HttpStatusCode.Accepted);

            var metrics = await alice.GetFromJsonAsync<MetricsDto>(Metrics(), Json);
            metrics!.Simulated.Should().BeGreaterThan(0);

            var preview = await PostAsync(alice, Simulate(), new { channelId = channel, action = "federation", inspectable = false, sample = "4111" });
            preview.StatusCode.Should().Be(HttpStatusCode.OK);
            var previewBody = await preview.Content.ReadFromJsonAsync<PreviewDto>(Json);
            previewBody!.Inspectable.Should().BeFalse();
            previewBody.Proceed.Should().BeTrue();
            previewBody.ReasonCode.Should().Be(DlpReasons.NotInspectable);
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var workspace = new WorkspaceId(WorkspaceId);
            db.DlpDecisions.RemoveRange(await db.DlpDecisions.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspace).ToListAsync());
            db.DlpRules.RemoveRange(await db.DlpRules.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspace).ToListAsync());
            db.LabelAssignments.RemoveRange(await db.LabelAssignments.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspace).ToListAsync());
            db.DlpPolicies.RemoveRange(await db.DlpPolicies.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspace).ToListAsync());
            if (endpointId is Guid id)
            {
                var endpoint = await db.OutboundWebhookEndpoints.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
                if (endpoint is not null)
                {
                    db.OutboundWebhookEndpoints.Remove(endpoint);
                }
            }

            await db.SaveChangesAsync();
        }
    }

    private async Task GrantAsync(HttpClient demo, Guid userId)
    {
        var response = await PostAsync(demo, $"/api/v1/workspaces/{WorkspaceId}/compliance/grants", new { userId, capability = "dlp" });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    private async Task<Guid> CreateChannelAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = $"dlp-{Guid.NewGuid():N}"[..18], type = "Public" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>(Json))!.Id;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static string Policy() => $"/api/v1/workspaces/{WorkspaceId}/dlp/policy";
    private static string Assignments() => $"/api/v1/workspaces/{WorkspaceId}/dlp/assignments";
    private static string Rules() => $"/api/v1/workspaces/{WorkspaceId}/dlp/rules";
    private static string Metrics() => $"/api/v1/workspaces/{WorkspaceId}/dlp/metrics";
    private static string Simulate() => $"/api/v1/workspaces/{WorkspaceId}/dlp/simulate";
    private static string Forward(Guid messageId) => $"/api/v1/workspaces/{WorkspaceId}/messages/{messageId}/forward";
    private static string Invites(Guid channelId) => $"/api/v1/workspaces/{WorkspaceId}/channels/{channelId}/invites";
    private static string Summarize(Guid channelId) => $"/api/v1/workspaces/{WorkspaceId}/channels/{channelId}/ai/summarize";

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        return await client.SendAsync(request);
    }

    private sealed record IdDto(Guid Id);
    private sealed record MetricsDto(int Total, int Simulated, int Blocked);
    private sealed record PreviewDto(string ReasonCode, bool Proceed, bool Inspectable);
}
