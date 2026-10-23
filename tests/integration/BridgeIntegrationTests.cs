using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-138 — bridge plumb: consent, loop, replay, remote identity, disconnect, DLP.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class BridgeIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Disabled_flag_hides_bridges_without_writing()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Integrations:Bridges:Enabled", "false"));
        using var admin = Client(disabled, "demo");
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/bridges", new
        {
            channelId = SeedData.DemoChannelId.Value,
            network = "lab",
            remoteRoom = "room-1",
            destinationHost = "fake.bridge.local",
            capabilities = new[] { "message" },
            remoteCapabilities = new[] { "message" },
            idempotencyKey = "bridge-off-" + Guid.NewGuid().ToString("N")
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(BridgeErrors.Disabled);
    }

    [Fact]
    public async Task Loop_replay_identity_order_and_disconnect_hold()
    {
        using var admin = Client(factory, "demo");
        using var alice = Client(factory, "alice");
        var channel = await CreateChannelAsync(admin);
        string? token = null;
        try
        {
            var unpinned = await CreateRawAsync(admin, channel, "other.example", "bridge-pin-" + Guid.NewGuid().ToString("N"));
            unpinned.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await unpinned.Content.ReadAsStringAsync()).Should().Contain(BridgeErrors.DestinationUnpinned);

            var remoteUrl = await CreateRawAsync(admin, channel, "https://169.254.169.254", "bridge-url-" + Guid.NewGuid().ToString("N"));
            (await remoteUrl.Content.ReadAsStringAsync()).Should().Contain(BridgeErrors.EgressBlocked);

            var created = await CreateAsync(admin, channel, ["message", "delete"], ["message"]);
            created.Status.Should().Be(HttpStatusCode.Created);
            created.Body.Token.Should().StartWith("vc_int_");
            created.Body.DeleteRemoteGuaranteed.Should().BeFalse();
            created.Body.DeleteLimitation.Should().Be(BridgeRules.DeleteLimitation);
            created.Body.Status.Should().Be(BridgeRules.StatusPending);
            token = created.Body.Token;
            var bridgeId = created.Body.Id;

            var replay = await CreateAsync(admin, channel, ["message", "delete"], ["message"], created.Key);
            replay.Status.Should().Be(HttpStatusCode.OK);
            replay.Body.Token.Should().BeNull();
            replay.Raw.Should().NotContain(token);

            var beforeConsent = await PostEventAsync(token, bridgeId, "remote-early-event", "other-net", "hello");
            beforeConsent.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await beforeConsent.Content.ReadAsStringAsync()).Should().Contain(BridgeErrors.TrafficBlocked);

            (await admin.PostAsJsonAsync(ConsentUrl(bridgeId), new { acknowledgeCopy = true })).StatusCode.Should().Be(HttpStatusCode.OK);

            var loop = await PostEventAsync(token, bridgeId, "remote-loop-event", "lab-instance", "echo");
            loop.StatusCode.Should().Be(HttpStatusCode.OK);
            (await loop.Content.ReadAsStringAsync()).Should().Contain("\"loop\":true");

            var first = await PostEventAsync(token, bridgeId, "remote-ada-event", "other-net", "olá da rede", "remote-ada", "Remote Ada", "https://fake.bridge.local/m/1");
            first.StatusCode.Should().Be(HttpStatusCode.Created);
            var duplicate = await PostEventAsync(token, bridgeId, "remote-ada-event", "other-net", "olá da rede", "remote-ada", "Remote Ada");
            duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
            (await duplicate.Content.ReadAsStringAsync()).Should().Contain("\"duplicated\":true");

            var limited = await PostEventAsync(token, bridgeId, "remote-delete-event", "other-net", null, "remote-ada", null, null, "delete", "remote-ada-event");
            limited.StatusCode.Should().Be(HttpStatusCode.OK);
            (await limited.Content.ReadAsStringAsync()).Should().Contain(BridgeRules.DeleteLimitation);

            var page = await alice.GetFromJsonAsync<PageDto>($"/api/v1/channels/{channel}/messages", Json);
            var remote = page!.Messages.Single(item => item.Body == "olá da rede");
            remote.AuthorIsBot.Should().BeTrue();
            remote.RemoteOrigin!.Subject.Should().Be("remote-ada");
            remote.RemoteOrigin.DisplayName.Should().Be("Remote Ada");
            remote.RemoteOrigin.Link.Should().Be("https://fake.bridge.local/m/1");
            remote.RemoteOrigin.DeleteRemoteGuaranteed.Should().BeFalse();
            remote.DeletedAt.Should().BeNull();

            var banner = await alice.GetFromJsonAsync<BridgeBody>($"/api/v1/workspaces/{WorkspaceId}/channels/{channel}/bridge", Json);
            banner!.Network.Should().Be("labnet");
            banner.DeleteLimitation.Should().Be(BridgeRules.DeleteLimitation);
            banner.CanManage.Should().BeFalse();

            var mapping = await admin.GetStringAsync($"/api/v1/admin/workspaces/{WorkspaceId}/bridges/{bridgeId}/mapping");
            mapping.Should().Contain("remote-ada").And.Contain("Remote Ada").And.NotContain("@");

            var firstLocal = await alice.PostAsJsonAsync($"/api/v1/channels/{channel}/messages", new
            {
                messageId = Guid.NewGuid(),
                idempotencyKey = "bridge-local-1-" + Guid.NewGuid().ToString("N"),
                body = "local-um"
            });
            firstLocal.StatusCode.Should().Be(HttpStatusCode.Accepted);
            var secondLocal = await alice.PostAsJsonAsync($"/api/v1/channels/{channel}/messages", new
            {
                messageId = Guid.NewGuid(),
                idempotencyKey = "bridge-local-2-" + Guid.NewGuid().ToString("N"),
                body = "local-dois"
            });
            secondLocal.StatusCode.Should().Be(HttpStatusCode.Accepted);

            using var bridgeClient = TokenClient(token);
            var pulled = await bridgeClient.PostAsync($"/api/v1/bridges/{bridgeId}/outbound", null);
            pulled.StatusCode.Should().Be(HttpStatusCode.OK);
            var pullBody = await pulled.Content.ReadAsStringAsync();
            pullBody.Should().Contain("local-um").And.Contain("local-dois").And.NotContain("olá da rede");
            pullBody.IndexOf("local-um", StringComparison.Ordinal).Should().BeLessThan(pullBody.IndexOf("local-dois", StringComparison.Ordinal));
            pullBody.Should().Contain(BridgeRules.DeleteLimitation).And.Contain("hmac-sha256=");

            var again = await bridgeClient.PostAsync($"/api/v1/bridges/{bridgeId}/outbound", null);
            (await again.Content.ReadAsStringAsync()).Should().NotContain("local-um");

            (await admin.PostAsync($"/api/v1/admin/workspaces/{WorkspaceId}/bridges/{bridgeId}/disconnect", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            var blocked = await PostEventAsync(token, bridgeId, "remote-after-off", "other-net", "não");
            blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await blocked.Content.ReadAsStringAsync()).Should().Contain(BridgeErrors.TrafficBlocked);
            var blockedPull = await bridgeClient.PostAsync($"/api/v1/bridges/{bridgeId}/outbound", null);
            blockedPull.StatusCode.Should().Be(HttpStatusCode.Conflict);

            await using var db = factory.CreateMigratorDbContext();
            var audits = await db.AuditEvents.IgnoreQueryFilters()
                .Where(item => item.EntityId == bridgeId.ToString())
                .Select(item => item.Action + " " + item.MetadataJson)
                .ToListAsync();
            audits.Should().Contain(item => item.StartsWith(AuditActions.BridgeDeleteLimited, StringComparison.Ordinal));
            audits.Should().OnlyContain(item => !item.Contains(token, StringComparison.Ordinal) && !item.Contains("olá da rede", StringComparison.Ordinal));
            (await db.Messages.IgnoreQueryFilters().CountAsync(item => item.ConversationId == new ChannelId(channel) && item.Body == "olá da rede" && item.DeletedAt == null)).Should().Be(1);
            (await db.BridgeEvents.IgnoreQueryFilters().CountAsync(item => item.BridgeId == bridgeId && item.RemoteEventId == "remote-ada-event")).Should().Be(1);
        }
        finally
        {
            await CleanupAsync(channel);
        }
    }

    [Fact]
    public async Task Dlp_block_skips_the_outbound_copy()
    {
        using var admin = Client(factory, "demo");
        using var alice = Client(factory, "alice");
        var channel = await CreateChannelAsync(admin);
        var previousSimulation = true;
        try
        {
            var created = await CreateAsync(admin, channel, ["message"], ["message"]);
            (await admin.PostAsJsonAsync(ConsentUrl(created.Body.Id), new { acknowledgeCopy = true })).StatusCode.Should().Be(HttpStatusCode.OK);
            var secret = "dlp-bridge-" + Guid.NewGuid().ToString("N");
            (await alice.PostAsJsonAsync($"/api/v1/channels/{channel}/messages", new
            {
                messageId = Guid.NewGuid(),
                idempotencyKey = "bridge-dlp-" + Guid.NewGuid().ToString("N"),
                body = secret
            })).StatusCode.Should().Be(HttpStatusCode.Accepted);

            await using (var before = factory.CreateMigratorDbContext())
            {
                var policy = await before.DlpPolicies.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.WorkspaceId == new WorkspaceId(WorkspaceId));
                previousSimulation = policy?.Simulation ?? true;
            }

            using (var grant = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/compliance/grants")
            {
                Content = JsonContent.Create(new { userId = SeedData.AliceUserId.Value, capability = "dlp" })
            })
            {
                grant.Headers.TryAddWithoutValidation("Idempotency-Key", "bridge-dlp-grant-" + Guid.NewGuid().ToString("N"));
                (await admin.SendAsync(grant)).StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
            }
            (await PutAsync(alice, $"/api/v1/workspaces/{WorkspaceId}/dlp/policy", new { simulation = false })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutAsync(alice, $"/api/v1/workspaces/{WorkspaceId}/dlp/assignments", new { scopeKind = "channel", scopeId = channel, labelKey = "restricted" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutAsync(alice, $"/api/v1/workspaces/{WorkspaceId}/dlp/rules", new { action = "federation", minRank = 3, effect = "block" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            using var bridgeClient = TokenClient(created.Body.Token!);
            var pulled = await bridgeClient.PostAsync($"/api/v1/bridges/{created.Body.Id}/outbound", null);
            var body = await pulled.Content.ReadAsStringAsync();
            pulled.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Should().NotContain(secret);

            await using var db = factory.CreateMigratorDbContext();
            (await db.AuditEvents.IgnoreQueryFilters().AnyAsync(item =>
                item.Action == AuditActions.BridgeCopyBlocked && item.EntityId == created.Body.Id.ToString() && !item.MetadataJson.Contains(secret))).Should().BeTrue();
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var rules = await db.DlpRules.IgnoreQueryFilters()
                .Where(item => item.WorkspaceId == new WorkspaceId(WorkspaceId) && item.Action == DlpActions.Federation)
                .ToListAsync();
            db.DlpRules.RemoveRange(rules);
            var assignments = await db.LabelAssignments.IgnoreQueryFilters()
                .Where(item => item.ScopeId == channel)
                .ToListAsync();
            db.LabelAssignments.RemoveRange(assignments);
            var policy = await db.DlpPolicies.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.WorkspaceId == new WorkspaceId(WorkspaceId));
            if (policy is not null) policy.Simulation = previousSimulation;
            await db.SaveChangesAsync();
            await CleanupAsync(channel);
        }
    }

    private async Task<Guid> CreateChannelAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = ("br-" + Guid.NewGuid().ToString("N"))[..18], type = "Public" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>(Json))!.Id;
    }

    private async Task<Call> CreateAsync(HttpClient client, Guid channel, string[] capabilities, string[] remote, string? key = null)
    {
        key ??= "bridge-" + Guid.NewGuid().ToString("N");
        var response = await CreateRawAsync(client, channel, "fake.bridge.local", key, capabilities, remote);
        var raw = await response.Content.ReadAsStringAsync();
        var body = response.IsSuccessStatusCode ? JsonSerializer.Deserialize<BridgeBody>(raw, Json) ?? new BridgeBody() : new BridgeBody();
        return new Call(response.StatusCode, raw, body, key);
    }

    private static Task<HttpResponseMessage> CreateRawAsync(HttpClient client, Guid channel, string host, string key, string[]? capabilities = null, string[]? remote = null) =>
        client.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/bridges", new
        {
            channelId = channel,
            network = "labnet",
            remoteRoom = "room-1",
            destinationHost = host,
            capabilities = capabilities ?? new[] { "message" },
            remoteCapabilities = remote ?? new[] { "message" },
            idempotencyKey = key
        });

    private async Task<HttpResponseMessage> PostEventAsync(
        string token, Guid bridgeId, string eventId, string origin, string? body,
        string subject = "remote-ada", string? displayName = "Remote Ada", string? link = null,
        string eventType = "message", string? target = null)
    {
        using var client = TokenClient(token);
        return await client.PostAsJsonAsync($"/api/v1/bridges/{bridgeId}/events", new
        {
            schema = BridgeRules.EnvelopeSchema,
            schemaVersion = BridgeRules.SchemaVersion,
            eventType,
            eventId,
            originInstanceId = origin,
            remoteSubject = subject,
            displayName,
            body,
            link,
            targetEventId = target,
            cursor = 1
        });
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        return await client.SendAsync(request);
    }

    private async Task CleanupAsync(Guid channelId)
    {
        await using var db = factory.CreateMigratorDbContext();
        var channel = new ChannelId(channelId);
        var bridges = await db.Bridges.IgnoreQueryFilters().Where(item => item.ChannelId == channel).ToListAsync();
        var bridgeIds = bridges.Select(item => item.Id).ToArray();
        var botIds = bridges.Select(item => item.BotId).ToArray();
        db.BridgeEvents.RemoveRange(await db.BridgeEvents.IgnoreQueryFilters().Where(item => bridgeIds.Contains(item.BridgeId)).ToListAsync());
        db.BridgeIdentities.RemoveRange(await db.BridgeIdentities.IgnoreQueryFilters().Where(item => bridgeIds.Contains(item.BridgeId)).ToListAsync());
        db.Bridges.RemoveRange(bridges);
        await db.SaveChangesAsync();
    }

    private static string ConsentUrl(Guid bridgeId) => $"/api/v1/admin/workspaces/{WorkspaceId}/bridges/{bridgeId}/consent";

    private static HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private HttpClient TokenClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-VibeChat-Integration-Token", token);
        return client;
    }

    private sealed record Call(HttpStatusCode Status, string Raw, BridgeBody Body, string Key);
    private sealed record IdDto(Guid Id);
    private sealed record PageDto(MessageDto[] Messages);
    private sealed record MessageDto(string Body, bool AuthorIsBot, DateTimeOffset? DeletedAt, RemoteDto? RemoteOrigin);
    private sealed record RemoteDto(string Subject, string DisplayName, string? Link, bool DeleteRemoteGuaranteed);

    private sealed class BridgeBody
    {
        public Guid Id { get; set; }
        public string? Token { get; set; }
        public string? Status { get; set; }
        public string? Network { get; set; }
        public bool DeleteRemoteGuaranteed { get; set; }
        public string? DeleteLimitation { get; set; }
        public bool CanManage { get; set; }
    }
}
