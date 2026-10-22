using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-066 — capability gate, built-in slash and HMAC callback.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class PluginPlatformIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Builtin_slash_posts_only_inside_capability_and_scope()
    {
        using var admin = Client("demo");
        await RemoveSlugAsync(PluginManifestRules.BuiltinSlashNoteId);
        var created = await InstallAsync(admin, new { builtinId = PluginManifestRules.BuiltinSlashNoteId, channelIds = new[] { ChannelId }, allowDms = false });
        created.Capabilities.Should().Contain(PluginManifestRules.CapabilityCommandsSlash);
        created.Token.Should().StartWith("vc_int_");
        created.CallbackSecret.Should().BeNull();

        var builtins = await admin.GetStringAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins/builtins");
        builtins.Should().Contain(PluginManifestRules.BuiltinSlashNoteId).And.NotContain("marketplace");

        using var alice = Client("alice");
        var commands = await alice.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/commands");
        commands.Should().Contain("slash-note/nota");

        var other = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = $"note-{Guid.NewGuid():N}"[..12], type = "Public", spaceId = SeedData.DemoSpaceGeralId });
        other.StatusCode.Should().Be(HttpStatusCode.Created);
        var otherChannel = await other.Content.ReadFromJsonAsync<CreatedChannel>(Json);
        var outside = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{otherChannel!.Id}/plugin-commands",
            new { name = "slash-note/nota", args = "fora", idempotencyKey = $"out-{Guid.NewGuid():N}" });
        outside.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outside.Content.ReadAsStringAsync()).Should().Contain(PluginPlatformErrors.OutOfScope);

        var note = $"nota-{Guid.NewGuid():N}";
        var key = $"note-{Guid.NewGuid():N}";
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/plugin-commands",
            new { name = "slash-note/nota", args = note, idempotencyKey = key });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await sent.Content.ReadFromJsonAsync<CommandResult>(Json);
        body!.Posted.Should().BeTrue();
        body.MessageId.Should().NotBeNull();

        var again = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/plugin-commands",
            new { name = "slash-note/nota", args = note, idempotencyKey = key });
        again.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var replay = await again.Content.ReadFromJsonAsync<CommandResult>(Json);
        replay!.MessageId.Should().Be(body.MessageId);

        var history = await alice.GetFromJsonAsync<HistoryDto>(
            $"/api/v1/channels/{ChannelId}/messages?after={body.Sequence - 1}",
            Json);
        history!.Messages.Should().Contain(message => message.Id == body.MessageId && message.Body == note && message.AuthorIsBot);

        var denied = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/plugin-commands",
            new { name = "incoming-messages/nota", args = note, idempotencyKey = $"no-{Guid.NewGuid():N}" });
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Subscribed_callback_is_signed_and_ignores_other_channels()
    {
        using var sink = new CallbackSink();
        using var admin = Client("demo");
        await RemoveSlugAsync("hook-sink");
        await RemoveSlugAsync("hook-other");
        var other = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = $"hook-{Guid.NewGuid():N}"[..12], type = "Public", spaceId = SeedData.DemoSpaceGeralId });
        other.EnsureSuccessStatusCode();
        var otherChannel = await other.Content.ReadFromJsonAsync<CreatedChannel>(Json);

        var created = await InstallAsync(admin, new
        {
            manifest = new
            {
                schema = PluginManifestRules.SchemaV1,
                id = "hook-sink",
                name = "Hook Sink",
                version = "1.0.0",
                capabilities = new[] { PluginManifestRules.CapabilityEventsSubscribe, PluginManifestRules.CapabilityCommandsSlash },
                commands = new[] { new { name = "ping", description = "Ping" } },
                callback = new { url = sink.Url("/hook"), events = new[] { WebhookEventTypes.MessageCreated } }
            },
            channelIds = new[] { ChannelId },
            allowDms = false
        });
        created.CallbackSecret.Should().StartWith(PluginCallbackSecret.Prefix);
        var listed = await admin.GetStringAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins");
        listed.Should().NotContain(created.CallbackSecret);

        var scopedOut = await InstallAsync(admin, new
        {
            manifest = new
            {
                schema = PluginManifestRules.SchemaV1,
                id = "hook-other",
                name = "Hook Other",
                version = "1.0.0",
                capabilities = new[] { PluginManifestRules.CapabilityEventsSubscribe },
                callback = new { url = sink.Url("/other"), events = new[] { WebhookEventTypes.MessageCreated } }
            },
            channelIds = new[] { otherChannel!.Id },
            allowDms = false
        });
        scopedOut.CallbackSecret.Should().NotBeNull();

        var marker = $"hook-{Guid.NewGuid():N}";
        var send = await admin.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new { messageId = Guid.NewGuid(), idempotencyKey = $"msg-{Guid.NewGuid():N}", body = marker });
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();

        var delivered = sink.EventsFor("/hook").Where(item => item.Body.Contains(marker, StringComparison.Ordinal)).ToArray();
        delivered.Should().ContainSingle();
        delivered[0].Signature.Should().Be(WebhookDelivery.ComputeSignature(created.CallbackSecret!, delivered[0].Body));
        delivered[0].EventName.Should().Be(WebhookEventTypes.MessageCreated);
        sink.EventsFor("/other").Should().BeEmpty();

        var ping = $"ping-{Guid.NewGuid():N}";
        var command = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/plugin-commands",
            new { name = "hook-sink/ping", args = ping, idempotencyKey = $"ping-{Guid.NewGuid():N}" });
        command.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var invoked = sink.EventsFor("/hook").Single(item => item.Body.Contains(ping, StringComparison.Ordinal));
        invoked.EventName.Should().Be(PluginCallbackDispatcher.CommandEvent);
        invoked.Signature.Should().Be(WebhookDelivery.ComputeSignature(created.CallbackSecret!, invoked.Body));
        invoked.Body.Should().NotContain(created.CallbackSecret);

        await RemoveSlugAsync("hook-sink");
        await RemoveSlugAsync("hook-other");
    }

    [Fact]
    public async Task Platform_flag_off_keeps_send_only_plugins()
    {
        await using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:Plugins:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var advanced = await client.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new { builtinId = PluginManifestRules.BuiltinSlashNoteId, channelIds = new[] { ChannelId }, allowDms = false });
        advanced.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await advanced.Content.ReadAsStringAsync()).Should().Contain(PluginPlatformErrors.Disabled);

        var command = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/plugin-commands",
            new { name = "slash-note/nota", args = "off", idempotencyKey = $"off-{Guid.NewGuid():N}" });
        command.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var basic = await client.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new { builtinId = PluginManifestRules.BuiltinIncomingMessagesId, channelIds = new[] { ChannelId }, allowDms = false });
        basic.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private async Task<InstalledDto> InstallAsync(HttpClient admin, object body)
    {
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<InstalledDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private async Task RemoveSlugAsync(string slug)
    {
        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.InstalledPlugins.IgnoreQueryFilters()
            .Where(plugin => plugin.WorkspaceId == SeedData.DemoWorkspaceId && plugin.PluginId == slug)
            .ToListAsync();
        if (rows.Count == 0)
        {
            return;
        }

        db.InstalledPlugins.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private async Task DrainOutboxAsync()
    {
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        for (var i = 0; i < 8; i++)
        {
            if (await processor.ProcessBatchAsync(CancellationToken.None) == 0)
            {
                return;
            }
        }
    }

    private sealed record InstalledDto(Guid Id, Guid BotId, string PluginId, string Token, string[] Capabilities, string? CallbackSecret);
    private sealed record CommandResult(bool Posted, Guid? MessageId, long Sequence, bool CallbackDelivered);
    private sealed record CreatedChannel(Guid Id);
    private sealed record HistoryDto(HistoryMessage[] Messages);
    private sealed record HistoryMessage(Guid Id, string Body, bool AuthorIsBot);

    private sealed class CallbackSink : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<Captured> _captured = new();
        private readonly CancellationTokenSource _stop = new();

        public CallbackSink()
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

                _captured.Enqueue(new Captured(
                    context.Request.Url?.AbsolutePath ?? string.Empty,
                    context.Request.Headers["X-VibeChat-Event"] ?? string.Empty,
                    body,
                    context.Request.Headers["X-VibeChat-Signature"] ?? string.Empty));
                context.Response.StatusCode = 204;
                context.Response.Close();
            }
        }
    }

    private sealed record Captured(string Path, string EventName, string Body, string Signature);
}
