using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-066 — malicious manifests, SSRF and cross-tenant callbacks.</summary>
[Collection(SecurityCollection.Name)]
public sealed class PluginPlatformSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Malicious_manifest_and_store_routes_are_rejected()
    {
        using var admin = Client("demo");
        var metadata = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new
            {
                manifest = new
                {
                    schema = PluginManifestRules.SchemaV1,
                    id = "metadata-hook",
                    name = "Metadata",
                    version = "1.0.0",
                    capabilities = new[] { PluginManifestRules.CapabilityEventsSubscribe },
                    callback = new { url = "https://169.254.169.254/latest", events = new[] { "MessageCreated" } }
                },
                channelIds = new[] { ChannelId },
                allowDms = false
            });
        metadata.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await metadata.Content.ReadAsStringAsync()).Should().Contain(PluginPlatformErrors.EgressBlocked);

        var script = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new
            {
                manifest = new
                {
                    schema = PluginManifestRules.SchemaV1,
                    id = "script-hook",
                    name = "Script",
                    version = "1.0.0",
                    capabilities = new[] { PluginManifestRules.CapabilityUiHooks },
                    hooks = new[] { "<script>" },
                    entry = "plugin.js"
                },
                channelIds = new[] { ChannelId },
                allowDms = false
            });
        script.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var store = await admin.GetAsync("/api/v1/plugins/marketplace");
        store.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var directory = await admin.GetAsync("/api/v1/admin/plugins/directory");
        directory.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = factory.CreateMigratorDbContext();
        var leaked = await db.InstalledPlugins.IgnoreQueryFilters()
            .AnyAsync(plugin => plugin.PluginId == "metadata-hook" || plugin.PluginId == "script-hook");
        leaked.Should().BeFalse();
    }

    [Fact]
    public async Task Callback_does_not_cross_tenants()
    {
        using var sink = new CallbackSink();
        var foreign = await SeedForeignPluginAsync(sink.Url("/foreign"));
        using var admin = Client("demo");
        var marker = $"tenant-{Guid.NewGuid():N}";
        var send = await admin.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new { messageId = Guid.NewGuid(), idempotencyKey = $"xt-{Guid.NewGuid():N}", body = marker });
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await DrainOutboxAsync();
        sink.EventsFor("/foreign").Should().BeEmpty();

        var invoke = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{ChannelId}/plugin-commands",
            new { name = "foreign-hook/ping", args = marker, idempotencyKey = $"cmd-{Guid.NewGuid():N}" });
        invoke.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.InstalledPlugins.IgnoreQueryFilters().FirstAsync(plugin => plugin.Id == foreign);
        db.InstalledPlugins.Remove(row);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedForeignPluginAsync(string callbackUrl)
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = VibeChat.SharedKernel.WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = "Foreign plugin platform",
            Slug = $"foreign-platform-{workspaceId.Value:N}",
            AiEnabled = false,
            CreatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            UserId = SeedData.DemoUserId,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });
        var botUser = UserId.New();
        db.UserProfiles.Add(new UserProfile
        {
            Id = botUser,
            Subject = $"bot:{botUser.Value:N}",
            Email = $"bot-{botUser.Value:N}@bots.vibechat.local",
            DisplayName = "Foreign Hook",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            UserId = botUser,
            Role = Role.Bot,
            JoinedAt = now
        });
        var bot = new IntegrationBot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            UserId = botUser,
            Name = "Foreign Hook",
            Enabled = true,
            AllowDms = false,
            CreatedAt = now
        };
        db.IntegrationBots.Add(bot);
        db.IntegrationBotChannelScopes.Add(new IntegrationBotChannelScope
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BotId = bot.Id,
            ChannelId = new ChannelId(ChannelId)
        });
        var manifestJson =
            "{\"schema\":\"vibechat.plugin.manifest.v1\",\"id\":\"foreign-hook\",\"name\":\"Foreign Hook\",\"version\":\"1.0.0\",\"capabilities\":[\"commands.slash\",\"events.subscribe\"],\"commands\":[{\"name\":\"ping\",\"description\":\"Ping\"}],\"callback\":{\"url\":\""
            + callbackUrl
            + "\",\"events\":[\"MessageCreated\"]}}";
        PluginManifestRules.TryParse(manifestJson, out var manifest, out var error).Should().BeTrue(error);
        var pluginId = Guid.NewGuid();
        var secrets = factory.Services.GetRequiredService<RuntimeSecretProtector>();
        db.InstalledPlugins.Add(new InstalledPlugin
        {
            Id = pluginId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            PluginId = manifest!.Id,
            Name = manifest.Name,
            Version = manifest.Version,
            ManifestJson = manifest.CanonicalJson,
            Capabilities = manifest.Capabilities,
            BotId = bot.Id,
            Enabled = true,
            InstalledAt = now,
            UpdatedAt = now,
            CallbackUrl = manifest.Callback!.Url,
            SubscribedEvents = manifest.Callback.Events,
            Hooks = manifest.Hooks,
            CallbackSecret = secrets.Protect(
                PluginCallbackSecret.CreateRaw(),
                VibeChat.Administration.RuntimeSecretKinds.PluginCallbackSecret,
                tenantId,
                workspaceId,
                pluginId.ToString("D"),
                now)
        });
        await db.SaveChangesAsync();
        return pluginId;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
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

    private sealed class CallbackSink : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<string> _paths = new();
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

        public IReadOnlyList<string> EventsFor(string path) =>
            _paths.Where(item => item.Equals(path, StringComparison.Ordinal)).ToArray();

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
        }

        private async Task PumpAsync()
        {
            while (!_stop.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _paths.Enqueue(context.Request.Url?.AbsolutePath ?? string.Empty);
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                }
                catch
                {
                    break;
                }
            }
        }
    }
}
