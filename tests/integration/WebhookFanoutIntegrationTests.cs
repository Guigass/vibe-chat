using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class WebhookFanoutIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Admin_webhook_fanout_respects_subscription_filter_limit_and_masks_secret()
    {
        using var demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        using var bob = factory.CreateClient();
        bob.DefaultRequestHeaders.Add("X-Dev-User", "bob");

        var workspaceId = SeedData.DemoWorkspaceId.Value;
        await DeleteDemoEndpointsAsync();
        await ResetMessagingPolicyAsync();
        await DrainOutboxAsync();

        var denied = await bob.PostAsJsonAsync("/api/v1/admin/webhooks", new { workspaceId, name = "nope", url = "https://hooks.example.test/nope" });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreignChannel = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var foreign = await demo.PostAsJsonAsync("/api/v1/admin/webhooks", new
        {
            workspaceId,
            name = "foreign",
            url = "https://hooks.example.test/foreign",
            channelFilter = new[] { foreignChannel }
        });
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await foreign.Content.ReadAsStringAsync()).Should().Contain("InvalidChannelFilter");

        var channelResponse = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/channels",
            new CreateChannelRequest($"hooks-{Guid.NewGuid():N}"[..18], "Public", SeedData.DemoSpaceGeralId));
        channelResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdChannel = await channelResponse.Content.ReadFromJsonAsync<ChannelCreated>(JsonOptions);
        var otherChannelId = createdChannel!.Id;

        using var sink = new WebhookSink();
        var secretEdited = "edited-only-secret-11";
        var secretBoth = "both-events-secret-22";
        var secretFail = "failing-hook-secret-33";

        var edited = await CreateEndpoint(demo, workspaceId, "edited-only", sink.Url("/edited"), secretEdited,
            [WebhookEventTypes.MessageEdited], null, true);
        var both = await CreateEndpoint(demo, workspaceId, "created-and-edited", sink.Url("/both"), secretBoth,
            [WebhookEventTypes.MessageCreated, WebhookEventTypes.MessageEdited], [SeedData.DemoChannelId.Value], true);
        var failing = await CreateEndpoint(demo, workspaceId, "failing", sink.Url("/fail"), secretFail,
            [WebhookEventTypes.MessageCreated], [SeedData.DemoChannelId.Value], true);

        (await demo.GetStringAsync($"/api/v1/admin/settings?workspaceId={workspaceId}"))
            .Should().NotContain(secretEdited).And.NotContain(secretBoth);

        var marker = $"hook-fanout-{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid();
        var send = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", marker, null, null));
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();

        sink.EventsFor("/edited").Where(x => x.Body.Contains(marker, StringComparison.Ordinal)).Should().BeEmpty();
        sink.EventsFor("/both").Where(x => x.Body.Contains(marker, StringComparison.Ordinal))
            .Select(x => x.EventName).Should().Equal(WebhookEventTypes.MessageCreated);
        sink.EventsFor("/fail").Where(x => x.Body.Contains(marker, StringComparison.Ordinal))
            .Select(x => x.EventName).Should().Equal(WebhookEventTypes.MessageCreated);
        var createdDelivery = sink.EventsFor("/both").Single(x => x.Body.Contains(marker, StringComparison.Ordinal));
        createdDelivery.Signature.Should().Be(WebhookDelivery.ComputeSignature(secretBoth, createdDelivery.Body));
        createdDelivery.Body.Should().Contain(marker);

        var editedBody = marker + "-edited";
        var edit = await demo.PutAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}",
            new EditMessageRequest(editedBody));
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        await DrainOutboxAsync();

        sink.EventsFor("/edited").Where(x => x.Body.Contains(marker, StringComparison.Ordinal))
            .Select(x => x.EventName).Should().Equal(WebhookEventTypes.MessageEdited);
        sink.EventsFor("/both").Where(x => x.Body.Contains(marker, StringComparison.Ordinal))
            .Select(x => x.EventName).Should().Equal(
                WebhookEventTypes.MessageCreated,
                WebhookEventTypes.MessageEdited);

        var otherMessageId = Guid.NewGuid();
        var otherMarker = marker + "-other";
        var otherSend = await demo.PostAsJsonAsync(
            $"/api/v1/channels/{otherChannelId}/messages",
            new SendMessageRequest(otherMessageId, $"idem-{otherMessageId:N}", otherMarker, null, null));
        otherSend.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();

        sink.EventsFor("/both").Should().NotContain(x => x.Body.Contains(otherMarker, StringComparison.Ordinal));
        sink.EventsFor("/edited").Should().NotContain(x => x.EventName == WebhookEventTypes.MessageCreated);

        var otherEdit = await demo.PutAsJsonAsync(
            $"/api/v1/channels/{otherChannelId}/messages/{otherMessageId}",
            new EditMessageRequest(otherMarker + "-edited"));
        otherEdit.StatusCode.Should().Be(HttpStatusCode.OK);
        await DrainOutboxAsync();
        sink.EventsFor("/edited").Count(x => x.Body.Contains(otherMarker, StringComparison.Ordinal)).Should().Be(1);
        sink.EventsFor("/both").Should().NotContain(x => x.Body.Contains(otherMarker, StringComparison.Ordinal));

        await using (var db = factory.CreateMigratorDbContext())
        {
            var failed = await db.OutboundWebhookEndpoints.IgnoreQueryFilters().SingleAsync(x => x.Id == failing.Id);
            failed.LastStatusCode.Should().Be(500);
            var processed = await db.OutboxMessages.IgnoreQueryFilters()
                .Where(x => x.Payload.Contains(marker))
                .ToListAsync();
            processed.Should().NotBeEmpty();
            processed.Should().OnlyContain(x => x.ProcessedAt != null);
        }

        var ping = await demo.PostAsJsonAsync(
            $"/api/v1/admin/webhooks/{edited.Id}/test",
            new { workspaceId });
        ping.StatusCode.Should().Be(HttpStatusCode.OK);
        var pingBody = await ping.Content.ReadFromJsonAsync<PingDto>(JsonOptions);
        pingBody!.Ok.Should().BeTrue();
        pingBody.StatusCode.Should().Be(200);
        sink.EventsFor("/edited").Should().Contain(x => x.EventName == WebhookEventTypes.WebhookTest);

        await using (var db = factory.CreateMigratorDbContext())
        {
            var row = await db.OutboundWebhookEndpoints.IgnoreQueryFilters().SingleAsync(x => x.Id == edited.Id);
            row.LastStatusCode.Should().Be(200);
            row.Secret.Should().BeNull();
            Encoding.UTF8.GetString(row.SigningSecret.Ciphertext!).Should().NotContain(secretEdited);
        }

        for (var i = 0; i < 2; i++)
        {
            var extra = await CreateEndpoint(demo, workspaceId, $"extra-{i}", $"https://hooks.example.test/extra-{i}",
                $"extra-secret-{i}-ok", [WebhookEventTypes.MessageCreated], null, false);
            extra.Id.Should().NotBe(Guid.Empty);
        }

        var limit = await demo.PostAsJsonAsync("/api/v1/admin/webhooks", new
        {
            workspaceId,
            name = "sixth",
            url = "https://hooks.example.test/sixth",
            secret = "sixth-secret-value"
        });
        limit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await limit.Content.ReadAsStringAsync()).Should().Contain("WebhookEndpointLimit");

        await DeleteDemoEndpointsAsync();
    }

    private static async Task<CreatedEndpoint> CreateEndpoint(
        HttpClient demo,
        Guid workspaceId,
        string name,
        string url,
        string secret,
        string[] events,
        Guid[]? channelFilter,
        bool enabled)
    {
        var response = await demo.PostAsJsonAsync("/api/v1/admin/webhooks", new
        {
            workspaceId,
            name,
            url,
            enabled,
            subscribedEvents = events,
            channelFilter,
            secret
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<CreatedEndpoint>(JsonOptions);
        created.Should().NotBeNull();
        created!.SecretConfigured.Should().BeTrue();
        created.SecretMask.Should().NotContain(secret);
        return created;
    }

    private async Task DrainOutboxAsync()
    {
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        for (var i = 0; i < 8; i++)
        {
            var processed = await processor.ProcessBatchAsync(CancellationToken.None);
            if (processed == 0)
            {
                return;
            }
        }
    }

    private async Task ResetMessagingPolicyAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var row = await db.MessageLifecyclePolicies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == SeedData.DemoTenantId);
        if (row is null)
        {
            return;
        }

        db.MessageLifecyclePolicies.Remove(row);
        await db.SaveChangesAsync();
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

    private sealed record ChannelCreated(Guid Id);
    private sealed record CreatedEndpoint(Guid Id, bool SecretConfigured, string? SecretMask);
    private sealed record PingDto(bool Ok, int? StatusCode);

    private sealed class WebhookSink : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<Captured> _captured = new();
        private readonly CancellationTokenSource _stop = new();

        public WebhookSink()
        {
            var port = 0;
            var attempts = 0;
            while (true)
            {
                port = Random.Shared.Next(20000, 32000);
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    _listener.Start();
                    break;
                }
                catch (HttpListenerException) when (++attempts < 8)
                {
                }
            }

            Port = port;
            _ = Task.Run(PumpAsync);
        }

        public int Port { get; }

        public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

        public IReadOnlyList<Captured> EventsFor(string path) =>
            _captured.Where(x => x.Path.Equals(path, StringComparison.Ordinal)).ToArray();

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
        }

        private async Task PumpAsync()
        {
            while (!_stop.IsCancellationRequested && _listener.IsListening)
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
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync();
                }

                var path = context.Request.Url?.AbsolutePath ?? "";
                _captured.Enqueue(new Captured(
                    path,
                    context.Request.Headers["X-VibeChat-Event"] ?? "",
                    body,
                    context.Request.Headers["X-VibeChat-Signature"] ?? ""));
                context.Response.StatusCode = path.Contains("/fail", StringComparison.Ordinal) ? 500 : 200;
                context.Response.Close();
            }
        }
    }

    private sealed record Captured(string Path, string EventName, string Body, string Signature);
}
