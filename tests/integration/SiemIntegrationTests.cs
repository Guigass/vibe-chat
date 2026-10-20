using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Compliance;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class SiemIntegrationTests(VibeChatApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    public Task InitializeAsync()
    {
        SiemTestDoubles.PauseBackgroundDrain = true;
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SiemTestDoubles.PauseBackgroundDrain = false;
        SiemSandbox.Shared.Reset();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Retry_reuses_event_id_and_backfill_stops_at_the_limit_without_a_body()
    {
        SiemSandbox.Shared.Reset();
        using var alice = Client("alice");
        await GrantAsync();
        var marker = $"zzprobe-{Guid.NewGuid():N}";
        var secretBody = $"body-{marker}";
        Guid? destinationId = null;
        try
        {
            var created = await SendAsync(alice, HttpMethod.Post, Destinations(), new
            {
                name = "siem",
                url = $"https://siem.example.test/{marker}",
                transport = SiemTransports.WebhookJson,
                categories = new[] { "zzprobe" },
                minSeverity = "info",
                minimize = false
            });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var createdBody = await created.Content.ReadFromJsonAsync<DestinationDto>(Json);
            destinationId = createdBody!.Id;
            createdBody.Secret.Should().NotBeNullOrWhiteSpace();
            var listed = await alice.GetStringAsync(Destinations());
            listed.Should().NotContain(createdBody.Secret!);

            var liveId = Guid.NewGuid();
            await InsertAuditAsync(liveId, $"zzprobe.{marker}", DateTimeOffset.UtcNow.AddMinutes(2), secretBody);
            SiemSandbox.Shared.FailRemaining = 1;
            await DrainAsync();
            SiemSandbox.Shared.Calls.Should().ContainSingle(call => call.EventId == liveId.ToString("D"));
            await Task.Delay(TimeSpan.FromSeconds(3));
            await DrainAsync();
            var liveCalls = SiemSandbox.Shared.Calls.Where(call => call.EventId == liveId.ToString("D")).ToArray();
            liveCalls.Should().HaveCount(2);
            liveCalls[0].Body.Should().Be(liveCalls[1].Body);
            liveCalls[0].Body.Should().NotContain(secretBody).And.NotContain(createdBody.Secret!);
            await DrainAsync();
            SiemSandbox.Shared.Calls.Count(call => call.EventId == liveId.ToString("D")).Should().Be(2);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenant.SetTenant(SeedData.DemoTenantId);
                var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
                await RlsSession.EnsureAppliedAsync(db, tenant, CancellationToken.None);
                var delivery = await db.SiemDeliveries.SingleAsync(x => x.EventId == liveId);
                delivery.Status.Should().Be(SiemDeliveryStatuses.Delivered);
                delivery.Attempts.Should().Be(2);
                await RlsSession.CommitAsync(db, CancellationToken.None);
            }

            SiemSandbox.Shared.Reset();
            var older = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            foreach (var id in older)
            {
                await InsertAuditAsync(id, $"zzprobe.{marker}", DateTimeOffset.UtcNow.AddHours(-2), secretBody);
            }

            await InsertAuditAsync(Guid.NewGuid(), $"zzprobe.{marker}", DateTimeOffset.UtcNow.AddDays(-3), secretBody);
            var backfill = await SendAsync(alice, HttpMethod.Post, $"{Destinations()}/{destinationId}/backfill", new
            {
                since = DateTimeOffset.UtcNow.AddDays(-4),
                limit = 2
            });
            backfill.StatusCode.Should().Be(HttpStatusCode.OK);
            var exported = SiemSandbox.Shared.Calls.Where(call => call.Body.Contains($"zzprobe.{marker}", StringComparison.Ordinal)).ToArray();
            exported.Should().HaveCount(2);
            exported.Should().OnlyContain(call => !call.Body.Contains(secretBody, StringComparison.Ordinal));
        }
        finally
        {
            if (destinationId is Guid id)
            {
                await SendAsync(alice, HttpMethod.Post, $"{Destinations()}/{id}/revoke", new { });
            }
        }
    }

    [Fact]
    public async Task Concurrent_drains_keep_one_delivery_row_for_the_event()
    {
        SiemSandbox.Shared.Reset();
        using var alice = Client("alice");
        await GrantAsync();
        var marker = $"zzrace-{Guid.NewGuid():N}";
        var secretBody = $"body-{marker}";
        Guid? destinationId = null;
        try
        {
            var created = await SendAsync(alice, HttpMethod.Post, Destinations(), new
            {
                name = "race",
                url = $"https://siem.example.test/{marker}",
                transport = SiemTransports.WebhookJson,
                categories = new[] { "zzrace" },
                minSeverity = "info",
                minimize = false
            });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            destinationId = (await created.Content.ReadFromJsonAsync<DestinationDto>(Json))!.Id;
            var liveId = Guid.NewGuid();
            await InsertAuditAsync(liveId, $"zzrace.{marker}", DateTimeOffset.UtcNow.AddMinutes(2), secretBody);

            await Task.WhenAll(DrainAsync(), DrainAsync());

            await using var scope = factory.Services.CreateAsyncScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.SetTenant(SeedData.DemoTenantId);
            var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
            await RlsSession.EnsureAppliedAsync(db, tenant, CancellationToken.None);
            var deliveries = await db.SiemDeliveries.Where(x => x.EventId == liveId).ToListAsync();
            deliveries.Should().ContainSingle();
            deliveries[0].Status.Should().Be(SiemDeliveryStatuses.Delivered);
            deliveries[0].Attempts.Should().BeGreaterThan(0);
            var calls = SiemSandbox.Shared.Calls.Where(call => call.EventId == liveId.ToString("D")).ToArray();
            calls.Should().NotBeEmpty();
            calls.Select(call => call.Body).Distinct().Should().ContainSingle();
            calls.Should().OnlyContain(call => !call.Body.Contains(secretBody, StringComparison.Ordinal));
            var sent = calls.Length;
            await DrainAsync();
            SiemSandbox.Shared.Calls.Count(call => call.EventId == liveId.ToString("D")).Should().Be(sent);
            await RlsSession.CommitAsync(db, CancellationToken.None);
        }
        finally
        {
            if (destinationId is Guid id)
            {
                await SendAsync(alice, HttpMethod.Post, $"{Destinations()}/{id}/revoke", new { });
            }
        }
    }

    [Fact]
    public async Task Invalid_destination_opens_the_circuit_and_stops_retries()
    {
        SiemSandbox.Shared.Reset();
        SiemSandbox.Shared.Mode = "invalid";
        using var alice = Client("alice");
        await GrantAsync();
        Guid? destinationId = null;
        try
        {
            var created = await SendAsync(alice, HttpMethod.Post, Destinations(), new
            {
                name = "breaker",
                url = "https://siem.example.test/breaker",
                transport = SiemTransports.WebhookJson
            });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            destinationId = (await created.Content.ReadFromJsonAsync<DestinationDto>(Json))!.Id;
            var test = await SendAsync(alice, HttpMethod.Post, $"{Destinations()}/{destinationId}/test", new { });
            test.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await test.Content.ReadAsStringAsync()).Should().Contain(SiemErrors.CircuitOpen);
            var again = await SendAsync(alice, HttpMethod.Post, $"{Destinations()}/{destinationId}/test", new { });
            again.StatusCode.Should().Be(HttpStatusCode.Conflict);
            SiemSandbox.Shared.Calls.Should().ContainSingle();
            var listed = await alice.GetStringAsync(Destinations());
            listed.Should().Contain("\"circuit\":\"open\"");
        }
        finally
        {
            SiemSandbox.Shared.Reset();
            if (destinationId is Guid id)
            {
                await SendAsync(alice, HttpMethod.Post, $"{Destinations()}/{id}/revoke", new { });
            }
        }
    }

    private async Task GrantAsync()
    {
        using var demo = Client("demo");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/compliance/grants")
        {
            Content = JsonContent(new { userId = SeedData.AliceUserId.Value, capability = ComplianceCapabilities.AuditExport })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await demo.SendAsync(request);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    private async Task InsertAuditAsync(Guid id, string action, DateTimeOffset occurredAt, string secretBody)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetTenant(SeedData.DemoTenantId);
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        await RlsSession.EnsureAppliedAsync(db, tenant, CancellationToken.None);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = id,
            TenantId = SeedData.DemoTenantId,
            ActorUserId = SeedData.AliceUserId,
            Action = action,
            EntityType = "probe",
            EntityId = id.ToString("D"),
            MetadataJson = $$"""{"body":"{{secretBody}}","note":"visible"}""",
            OccurredAt = occurredAt
        });
        await db.SaveChangesAsync();
        await RlsSession.CommitAsync(db);
    }

    private async Task DrainAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SiemExportService>();
        await service.DrainAsync(CancellationToken.None);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object body)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent(body) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static string Destinations() => $"/api/v1/workspaces/{WorkspaceId}/siem/destinations";

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed class DestinationDto
    {
        public Guid Id { get; set; }
        public string? Secret { get; set; }
    }
}
