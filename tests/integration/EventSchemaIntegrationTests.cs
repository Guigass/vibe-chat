using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class EventSchemaIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Flag_off_hides_the_catalog_and_still_delivers_the_current_body()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var hidden = await demo.GetAsync("/api/v1/admin/event-schemas");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().Contain(EventSchemaReasons.Disabled);

        await DeleteDemoEndpointsAsync();
        using var sink = new SchemaWebhookSink();
        var secret = "schema-off-secret-11";
        var endpoint = await CreateEndpoint(demo, sink.Url("/off"), secret);
        var body = """{"schemaVersion":99}""";
        await DispatchAsync(factory.Services, WebhookEventTypes.MessageCreated, body);

        var captured = sink.EventsFor("/off").Single();
        captured.Body.Should().Be(body);
        captured.SchemaVersion.Should().BeNull();
        captured.Signature.Should().Be(WebhookDelivery.ComputeSignature(secret, body));
        await DeleteEndpointAsync(endpoint.Id);
    }

    [Fact]
    public async Task Flag_on_lists_both_versions_checks_examples_and_dead_letters_unknown_versions()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Integrations:EventSchemas:Enabled", "true"));
        using var demo = host.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var list = await demo.GetStringAsync("/api/v1/admin/event-schemas");
        list.Should().Contain(PublicEventCatalog.MessageCreated);
        list.Should().Contain("\"schemaVersion\":1").And.Contain("\"schemaVersion\":2");
        list.Should().NotContain(SeedData.DemoTenantId.Value.ToString());

        var missing = await demo.GetAsync(
            $"/api/v1/admin/event-schemas/{PublicEventCatalog.MessageCreated}/versions/99");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).Should().Contain(EventSchemaReasons.UnknownSchemaVersion);

        var example = PublicEventCatalog.Versions.Single(version =>
            version.EventType == PublicEventCatalog.MessageCreated && version.SchemaVersion == 1).Example;
        var check = await demo.PostAsJsonAsync("/api/v1/admin/event-schemas/check", new
        {
            eventType = PublicEventCatalog.MessageCreated,
            schemaVersion = 1,
            document = JsonSerializer.Deserialize<JsonElement>(example.ToJsonString())
        });
        check.StatusCode.Should().Be(HttpStatusCode.OK);

        var mismatch = await demo.PostAsJsonAsync("/api/v1/admin/event-schemas/check", new
        {
            eventType = PublicEventCatalog.MessageCreated,
            schemaVersion = 1,
            document = new { tenantId = "nope" }
        });
        mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await mismatch.Content.ReadAsStringAsync()).Should().Contain(EventSchemaReasons.SchemaMismatch);

        await DeleteDemoEndpointsAsync();
        using var sink = new SchemaWebhookSink();
        var secret = "schema-on-secret-22";
        using var parent = factory.CreateClient();
        parent.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var endpoint = await CreateEndpoint(parent, sink.Url("/on"), secret);
        var good = example.ToJsonString();
        await DispatchAsync(host.Services, WebhookEventTypes.MessageCreated, good);
        var delivered = sink.EventsFor("/on").Single();
        delivered.SchemaVersion.Should().Be("1");
        delivered.EventType.Should().Be(PublicEventCatalog.MessageCreated);
        delivered.Signature.Should().Be(WebhookDelivery.ComputeSignature(secret, good));

        await DispatchAsync(host.Services, WebhookEventTypes.MessageCreated, """{"schemaVersion":99}""");
        sink.EventsFor("/on").Should().ContainSingle();

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.OutboundWebhookEndpoints.IgnoreQueryFilters().SingleAsync(x => x.Id == endpoint.Id);
        row.LastError.Should().Be(EventSchemaReasons.UnknownSchemaVersion);
        await DeleteEndpointAsync(endpoint.Id);
    }

    private static async Task DispatchAsync(IServiceProvider services, string eventName, string body)
    {
        using var scope = services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetTenant(SeedData.DemoTenantId);
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        await RlsSession.EnsureAppliedAsync(db, tenant, CancellationToken.None);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDispatcher>();
        await dispatcher.TryDispatchAsync(
            SeedData.DemoTenantId,
            eventName,
            Guid.NewGuid(),
            body,
            SeedData.DemoChannelId.Value,
            CancellationToken.None);
        await db.SaveChangesAsync();
        await RlsSession.CommitAsync(db, CancellationToken.None);
    }

    private async Task<CreatedEndpoint> CreateEndpoint(HttpClient demo, string url, string secret)
    {
        var response = await demo.PostAsJsonAsync("/api/v1/admin/webhooks", new
        {
            workspaceId = SeedData.DemoWorkspaceId.Value,
            name = "schema-registry",
            url,
            enabled = true,
            subscribedEvents = new[] { WebhookEventTypes.MessageCreated },
            secret
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedEndpoint>(JsonOptions))!;
    }

    private async Task DeleteDemoEndpointsAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.OutboundWebhookEndpoints.IgnoreQueryFilters()
            .Where(x => x.TenantId == SeedData.DemoTenantId)
            .ToListAsync();
        if (rows.Count == 0)
        {
            return;
        }

        db.OutboundWebhookEndpoints.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private async Task DeleteEndpointAsync(Guid id)
    {
        await using var db = factory.CreateMigratorDbContext();
        var row = await db.OutboundWebhookEndpoints.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id);
        if (row is null)
        {
            return;
        }

        db.OutboundWebhookEndpoints.Remove(row);
        await db.SaveChangesAsync();
    }

    private sealed record CreatedEndpoint(Guid Id);

    private sealed class SchemaWebhookSink : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly List<Captured> _captured = [];

        public SchemaWebhookSink()
        {
            var attempts = 0;
            while (true)
            {
                var port = Random.Shared.Next(20000, 32000);
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    _listener.Start();
                    Port = port;
                    break;
                }
                catch (HttpListenerException) when (++attempts < 8)
                {
                }
            }

            _ = Task.Run(PumpAsync);
        }

        public int Port { get; }

        public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

        public IReadOnlyList<Captured> EventsFor(string path) =>
            _captured.Where(item => item.Path.Equals(path, StringComparison.Ordinal)).ToArray();

        public void Dispose() => _listener.Close();

        private async Task PumpAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                string body;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync();
                }

                lock (_captured)
                {
                    _captured.Add(new Captured(
                        context.Request.Url?.AbsolutePath ?? "",
                        body,
                        context.Request.Headers[WebhookDelivery.SchemaVersionHeader],
                        context.Request.Headers[WebhookDelivery.EventTypeHeader],
                        context.Request.Headers[WebhookDelivery.SignatureHeader] ?? ""));
                }

                context.Response.StatusCode = 200;
                context.Response.Close();
            }
        }
    }

    private sealed record Captured(string Path, string Body, string? SchemaVersion, string? EventType, string Signature);
}
