using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-110 — install built-in, disable, uninstall and manifest rejection.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class IntegrationPluginIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Builtin_install_sends_then_disable_and_uninstall_block_new_sends()
    {
        using var admin = Client("demo");
        await RemoveSlugAsync(SeedData.DemoWorkspaceId, PluginManifestRules.BuiltinIncomingMessagesId);
        var created = await InstallBuiltinAsync(admin);
        created.Token.Should().StartWith("vc_int_");
        created.PluginId.Should().Be(PluginManifestRules.BuiltinIncomingMessagesId);
        created.Capabilities.Should().Equal(PluginManifestRules.CapabilityMessagesSend);

        var listed = await admin.GetStringAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins");
        listed.Should().NotContain(created.Token);
        listed.Should().Contain(created.Token[^4..]);
        listed.Should().Contain("incoming-messages");

        var rotated = await admin.PostAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{created.Id}/rotate",
            null);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await rotated.Content.ReadFromJsonAsync<InstalledDto>(Json);
        again!.Token.Should().StartWith("vc_int_").And.NotBe(created.Token);

        using var stale = BotClient(created.Token);
        var staleSend = await stale.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "old token", idempotencyKey = $"old-{Guid.NewGuid():N}" });
        staleSend.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var bot = BotClient(again.Token);
        var body = $"plugin-hello-{Guid.NewGuid():N}";
        var sent = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body, idempotencyKey = $"plug-{Guid.NewGuid():N}" });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var message = await sent.Content.ReadFromJsonAsync<SentDto>(Json);

        var disabled = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{created.Id}",
            new { enabled = false });
        disabled.StatusCode.Should().Be(HttpStatusCode.OK);

        var blocked = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "after disable", idempotencyKey = $"off-{Guid.NewGuid():N}" });
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var alice = Client("alice");
        var history = await alice.GetFromJsonAsync<HistoryDto>(
            $"/api/v1/channels/{ChannelId}/messages?after={message!.Sequence - 1}",
            Json);
        history!.Messages.Should().Contain(m => m.Id == message.MessageId && m.Body == body && m.AuthorIsBot);

        var removed = await admin.DeleteAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{created.Id}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "after uninstall", idempotencyKey = $"gone-{Guid.NewGuid():N}" });
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var db = factory.CreateMigratorDbContext();
        var stillThere = await db.Messages.IgnoreQueryFilters().CountAsync(x => x.Id == new MessageId(message.MessageId));
        stillThere.Should().Be(1);
        var pluginRow = await db.InstalledPlugins.IgnoreQueryFilters().CountAsync(x => x.Id == created.Id);
        pluginRow.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_manifest_and_unknown_capability_are_rejected()
    {
        using var admin = Client("demo");
        var extra = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new
            {
                manifest = new
                {
                    schema = PluginManifestRules.SchemaV1,
                    id = "pager",
                    name = "Pager",
                    version = "1.0.0",
                    capabilities = new[] { PluginManifestRules.CapabilityMessagesSend },
                    entry = "plugin.js"
                },
                channelIds = new[] { ChannelId },
                allowDms = false
            });
        extra.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await extra.Content.ReadAsStringAsync()).Should().Contain(PluginManifestRules.Invalid);

        var unknown = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new
            {
                manifest = new
                {
                    schema = PluginManifestRules.SchemaV1,
                    id = "reader",
                    name = "Reader",
                    version = "1.0.0",
                    capabilities = new[] { "messages.read" }
                },
                channelIds = new[] { ChannelId },
                allowDms = false
            });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain(PluginManifestRules.UnknownCapability);
    }

    [Fact]
    public async Task Duplicate_builtin_conflicts_and_workspace_cap_is_enforced()
    {
        using var admin = Client("demo");
        await RemoveSlugAsync(SeedData.DemoWorkspaceId, PluginManifestRules.BuiltinIncomingMessagesId);
        var first = await InstallBuiltinAsync(admin);
        var second = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new { builtinId = PluginManifestRules.BuiltinIncomingMessagesId, channelIds = new[] { ChannelId }, allowDms = false });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await admin.DeleteAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins/{first.Id}");

        var isolated = await SeedWorkspaceAsync();
        var installed = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{isolated}/plugins",
            new { builtinId = PluginManifestRules.BuiltinIncomingMessagesId, channelIds = Array.Empty<Guid>(), allowDms = false });
        installed.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var tenantId = new TenantId(isolated);
        var workspaceId = new VibeChat.SharedKernel.WorkspaceId(isolated);
        for (var i = 0; i < PluginManifestRules.MaxPluginsPerWorkspace - 1; i++)
        {
            var botId = Guid.NewGuid();
            db.IntegrationBots.Add(new IntegrationBot
            {
                Id = botId,
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                UserId = UserId.New(),
                Name = $"cap-{i}",
                Enabled = false,
                AllowDms = false,
                CreatedAt = now
            });
            db.InstalledPlugins.Add(new InstalledPlugin
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                PluginId = $"cap-{i}",
                Name = $"Cap {i}",
                Version = "1.0.0",
                ManifestJson = "{}",
                Capabilities = [PluginManifestRules.CapabilityMessagesSend],
                BotId = botId,
                Enabled = false,
                InstalledAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
        var capped = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{isolated}/plugins",
            new
            {
                manifest = new
                {
                    schema = PluginManifestRules.SchemaV1,
                    id = "one-more",
                    name = "One More",
                    version = "1.0.0",
                    capabilities = new[] { PluginManifestRules.CapabilityMessagesSend }
                },
                channelIds = Array.Empty<Guid>(),
                allowDms = false
            });
        capped.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await capped.Content.ReadAsStringAsync()).Should().Contain(PluginManifestRules.LimitReached);
    }

    [Fact]
    public async Task Kill_switch_hides_plugin_admin()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Integrations:Bots:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var list = await client.GetAsync($"/api/v1/admin/workspaces/{WorkspaceId}/plugins");
        list.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private HttpClient BotClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task RemoveSlugAsync(VibeChat.SharedKernel.WorkspaceId workspaceId, string slug)
    {
        await using var db = factory.CreateMigratorDbContext();
        var rows = await db.InstalledPlugins.IgnoreQueryFilters()
            .Where(x => x.WorkspaceId == workspaceId && x.PluginId == slug)
            .ToListAsync();
        if (rows.Count == 0)
        {
            return;
        }

        db.InstalledPlugins.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedWorkspaceAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = VibeChat.SharedKernel.WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = new TenantId(workspaceId.Value),
            Name = "Plugin cap",
            Slug = $"plugin-cap-{workspaceId.Value:N}",
            AiEnabled = false,
            CreatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = new TenantId(workspaceId.Value),
            WorkspaceId = workspaceId,
            UserId = SeedData.DemoUserId,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });
        await db.SaveChangesAsync();
        return workspaceId.Value;
    }

    private async Task<InstalledDto> InstallBuiltinAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/plugins",
            new { builtinId = PluginManifestRules.BuiltinIncomingMessagesId, channelIds = new[] { ChannelId }, allowDms = false });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<InstalledDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private sealed record InstalledDto(Guid Id, string PluginId, string Token, string[] Capabilities);
    private sealed record SentDto(Guid MessageId, long Sequence);
    private sealed record HistoryDto(HistoryMessage[] Messages);
    private sealed record HistoryMessage(Guid Id, string Body, bool AuthorIsBot);
}
