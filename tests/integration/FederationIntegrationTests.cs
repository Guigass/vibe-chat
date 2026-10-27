using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using VibeChat.Administration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-065 — allowlisted federation: signature, dedupe, revoke, DLP and visible limitation.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class FederationIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Disabled_flag_hides_federation_without_writing()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Integrations:Federation:Enabled", "false"));
        using var admin = Client(disabled, "demo");
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts", new
        {
            domain = "peer.example",
            remoteInstanceId = "remote-off",
            capabilities = new[] { "message" },
            remoteCapabilities = new[] { "message" },
            remoteSchemaVersion = 1,
            idempotencyKey = "fed-off-" + Guid.NewGuid().ToString("N")
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(FederationErrors.Disabled);
    }

    [Fact]
    public async Task Signed_events_dedupe_revoke_blocks_and_limitation_stays_visible()
    {
        using var admin = Client(factory, "demo");
        using var alice = Client(factory, "alice");
        var channel = await CreateChannelAsync(admin);
        var remoteInstance = "remote-" + Guid.NewGuid().ToString("N")[..8];
        string? token = null;
        try
        {
            var unpinned = await CreateRawAsync(admin, "other.example", remoteInstance, "fed-pin-" + Guid.NewGuid().ToString("N"));
            unpinned.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await unpinned.Content.ReadAsStringAsync()).Should().Contain(FederationErrors.DomainUnpinned);

            var remoteUrl = await CreateRawAsync(admin, "https://169.254.169.254", remoteInstance, "fed-url-" + Guid.NewGuid().ToString("N"));
            (await remoteUrl.Content.ReadAsStringAsync()).Should().Contain(FederationErrors.EgressBlocked);

            var created = await CreateAsync(admin, remoteInstance);
            created.Status.Should().Be(HttpStatusCode.Created);
            created.Body.Token.Should().StartWith("vc_int_");
            created.Body.Domain.Should().Be("peer.example");
            created.Body.DeleteRemoteGuaranteed.Should().BeFalse();
            created.Body.DeleteLimitation.Should().Be(FederationRules.DeleteLimitation);
            created.Body.Copied.Should().Contain(FederationRules.CopiedData);
            created.Body.Status.Should().Be(FederationRules.StatusPending);
            created.Body.ConsentRequired.Should().BeTrue();
            token = created.Body.Token;
            var trustId = created.Body.Id;

            var replay = await CreateAsync(admin, remoteInstance, created.Key);
            replay.Status.Should().Be(HttpStatusCode.OK);
            replay.Body.Token.Should().BeNull();
            replay.Raw.Should().NotContain(token);

            var room = await admin.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts/{trustId}/rooms", new
            {
                channelId = channel,
                remoteRoom = "room-1",
                idempotencyKey = "fed-room-" + Guid.NewGuid().ToString("N")
            });
            room.StatusCode.Should().Be(HttpStatusCode.Created);
            var roomBody = await room.Content.ReadFromJsonAsync<TrustBody>(Json);
            var roomId = roomBody!.RoomId!.Value;

            var beforeConsent = await PostEventAsync(token!, roomId, channel, "remote-early", "other-instance", "cedo");
            beforeConsent.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await beforeConsent.Content.ReadAsStringAsync()).Should().Contain(FederationErrors.TrafficBlocked);

            (await admin.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts/{trustId}/consent", new { acknowledgeCopy = true }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var forged = await PostEventAsync(token!, roomId, channel, "remote-forged", "other-instance", "forjado", signature: "hmac-sha256=00");
            forged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await forged.Content.ReadAsStringAsync()).Should().Contain(FederationErrors.SignatureInvalid);

            var first = await PostEventAsync(token!, roomId, channel, "remote-ada-event", "other-instance", "olá da federação");
            first.StatusCode.Should().Be(HttpStatusCode.Created);
            var duplicate = await PostEventAsync(token!, roomId, channel, "remote-ada-event", "other-instance", "olá da federação");
            duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
            (await duplicate.Content.ReadAsStringAsync()).Should().Contain("\"duplicated\":true");

            var limited = await PostEventAsync(token!, roomId, channel, "remote-delete-event", "other-instance", null, "delete");
            limited.StatusCode.Should().Be(HttpStatusCode.OK);
            (await limited.Content.ReadAsStringAsync()).Should().Contain(FederationRules.DeleteLimitation);

            var page = await alice.GetFromJsonAsync<PageDto>($"/api/v1/channels/{channel}/messages", Json);
            var remote = page!.Messages.Single(item => item.Body == "olá da federação");
            remote.AuthorIsBot.Should().BeTrue();
            remote.RemoteOrigin!.Subject.Should().Be("remote-ada");
            remote.RemoteOrigin.DisplayName.Should().Be("Remote Ada");
            remote.RemoteOrigin.Network.Should().Be("peer.example");
            remote.RemoteOrigin.DeleteRemoteGuaranteed.Should().BeFalse();
            remote.DeletedAt.Should().BeNull();

            var banner = await alice.GetFromJsonAsync<TrustBody>($"/api/v1/workspaces/{WorkspaceId}/channels/{channel}/federation", Json);
            banner!.Domain.Should().Be("peer.example");
            banner.Status.Should().Be(FederationRules.StatusActive);
            banner.DeleteLimitation.Should().Be(FederationRules.DeleteLimitation);
            banner.CanManage.Should().BeFalse();
            banner.Remote.Should().BeTrue();

            var rotated = await admin.PostAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts/{trustId}/keys/rotate", null);
            rotated.StatusCode.Should().Be(HttpStatusCode.OK);
            var rotatedBody = await rotated.Content.ReadFromJsonAsync<TrustBody>(Json);
            rotatedBody!.Token.Should().StartWith("vc_int_");
            rotatedBody.CurrentKeyId.Should().Be("k2");
            var still = await PostEventAsync(token!, roomId, channel, "remote-old-key", "other-instance", "chave antiga", keyId: "k1");
            still.StatusCode.Should().Be(HttpStatusCode.Created);
            (await admin.PostAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts/{trustId}/keys/retire", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            var retired = await PostEventAsync(token!, roomId, channel, "remote-retired", "other-instance", "apos", keyId: "k1");
            retired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var localBody = "local-fed-" + Guid.NewGuid().ToString("N");
            (await alice.PostAsJsonAsync($"/api/v1/channels/{channel}/messages", new
            {
                messageId = Guid.NewGuid(),
                idempotencyKey = "fed-local-" + Guid.NewGuid().ToString("N"),
                body = localBody
            })).StatusCode.Should().Be(HttpStatusCode.Accepted);

            using var peer = TokenClient(rotatedBody.Token!);
            var pulled = await peer.PostAsync($"/api/v1/federation/rooms/{roomId}/reconcile", null);
            var pulledText = await pulled.Content.ReadAsStringAsync();
            pulled.StatusCode.Should().Be(HttpStatusCode.OK);
            pulledText.Should().Contain(localBody).And.Contain(FederationRules.DeleteLimitation);
            pulledText.Should().NotContain("olá da federação");
            var again = await peer.PostAsync($"/api/v1/federation/rooms/{roomId}/reconcile", null);
            (await again.Content.ReadAsStringAsync()).Should().NotContain(localBody);

            (await admin.PostAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts/{trustId}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            var blocked = await PostEventAsync(rotatedBody.Token!, roomId, channel, "remote-after-revoke", "other-instance", "não");
            blocked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            await using var db = factory.CreateMigratorDbContext();
            (await db.Messages.IgnoreQueryFilters().CountAsync(item => item.ConversationId == new ChannelId(channel) && item.Body == "olá da federação")).Should().Be(1);
            (await db.FederationEvents.IgnoreQueryFilters().CountAsync(item => item.TrustId == trustId && item.RemoteEventId == "remote-ada-event")).Should().Be(1);
            (await db.FederationEvents.IgnoreQueryFilters().CountAsync(item => item.TrustId == trustId && item.RemoteEventId == "remote-forged")).Should().Be(0);
            var audits = await db.AuditEvents.IgnoreQueryFilters()
                .Where(item => item.EntityId == trustId.ToString())
                .Select(item => item.Action + " " + item.MetadataJson)
                .ToListAsync();
            audits.Should().Contain(item => item.StartsWith(AuditActions.FederationSignatureRejected, StringComparison.Ordinal));
            audits.Should().Contain(item => item.StartsWith(AuditActions.FederationDeleteLimited, StringComparison.Ordinal));
            audits.Should().OnlyContain(item => !item.Contains(token, StringComparison.Ordinal) && !item.Contains("olá da federação", StringComparison.Ordinal));
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
        var remoteInstance = "dlp-" + Guid.NewGuid().ToString("N")[..8];
        var previousSimulation = true;
        try
        {
            var created = await CreateAsync(admin, remoteInstance);
            (await admin.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts/{created.Body.Id}/consent", new { acknowledgeCopy = true }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            var room = await admin.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts/{created.Body.Id}/rooms", new
            {
                channelId = channel,
                remoteRoom = "room-dlp",
                idempotencyKey = "fed-dlp-room-" + Guid.NewGuid().ToString("N")
            });
            var roomId = (await room.Content.ReadFromJsonAsync<TrustBody>(Json))!.RoomId!.Value;
            var secret = "dlp-fed-" + Guid.NewGuid().ToString("N");
            (await alice.PostAsJsonAsync($"/api/v1/channels/{channel}/messages", new
            {
                messageId = Guid.NewGuid(),
                idempotencyKey = "fed-dlp-" + Guid.NewGuid().ToString("N"),
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
                grant.Headers.TryAddWithoutValidation("Idempotency-Key", "fed-dlp-grant-" + Guid.NewGuid().ToString("N"));
                (await admin.SendAsync(grant)).StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
            }

            (await PutAsync(alice, $"/api/v1/workspaces/{WorkspaceId}/dlp/policy", new { simulation = false })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutAsync(alice, $"/api/v1/workspaces/{WorkspaceId}/dlp/assignments", new { scopeKind = "channel", scopeId = channel, labelKey = "restricted" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutAsync(alice, $"/api/v1/workspaces/{WorkspaceId}/dlp/rules", new { action = "federation", minRank = 3, effect = "block" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            using var peer = TokenClient(created.Body.Token!);
            var pulled = await peer.PostAsync($"/api/v1/federation/rooms/{roomId}/reconcile", null);
            var body = await pulled.Content.ReadAsStringAsync();
            pulled.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Should().NotContain(secret);

            await using var db = factory.CreateMigratorDbContext();
            (await db.AuditEvents.IgnoreQueryFilters().AnyAsync(item =>
                item.Action == AuditActions.FederationCopyBlocked && item.EntityId == created.Body.Id.ToString() && !item.MetadataJson.Contains(secret))).Should().BeTrue();
        }
        finally
        {
            await using var db = factory.CreateMigratorDbContext();
            var rules = await db.DlpRules.IgnoreQueryFilters()
                .Where(item => item.WorkspaceId == new WorkspaceId(WorkspaceId) && item.Action == DlpActions.Federation)
                .ToListAsync();
            db.DlpRules.RemoveRange(rules);
            var assignments = await db.LabelAssignments.IgnoreQueryFilters().Where(item => item.ScopeId == channel).ToListAsync();
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
            new { name = ("fd-" + Guid.NewGuid().ToString("N"))[..18], type = "Public" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>(Json))!.Id;
    }

    private async Task<Call> CreateAsync(HttpClient client, string remoteInstance, string? key = null)
    {
        key ??= "fed-" + Guid.NewGuid().ToString("N");
        var response = await CreateRawAsync(client, "peer.example", remoteInstance, key);
        var raw = await response.Content.ReadAsStringAsync();
        var body = response.IsSuccessStatusCode ? JsonSerializer.Deserialize<TrustBody>(raw, Json) ?? new TrustBody() : new TrustBody();
        return new Call(response.StatusCode, raw, body, key);
    }

    private static Task<HttpResponseMessage> CreateRawAsync(HttpClient client, string domain, string remoteInstance, string key) =>
        client.PostAsJsonAsync($"/api/v1/admin/workspaces/{WorkspaceId}/federation/trusts", new
        {
            domain,
            remoteInstanceId = remoteInstance,
            capabilities = new[] { "message" },
            remoteCapabilities = new[] { "message", "delete" },
            remoteSchemaVersion = 1,
            idempotencyKey = key
        });

    private async Task<HttpResponseMessage> PostEventAsync(
        string token, Guid roomId, Guid channelId, string eventId, string origin, string? body,
        string eventType = "message", string? signature = null, string keyId = "k1")
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds();
        var canonical = FederationRules.Canonical(
            eventType, eventId, origin, "peer.example", "lab-instance", "room-1", channelId,
            "remote-ada", "Remote Ada", body, 1, expiresAt, keyId, eventId);
        using var client = TokenClient(token);
        return await client.PostAsJsonAsync($"/api/v1/federation/rooms/{roomId}/events", new
        {
            schema = FederationRules.EnvelopeSchema,
            schemaVersion = FederationRules.SchemaVersion,
            eventType,
            eventId,
            originInstanceId = origin,
            originDomain = "peer.example",
            destinationInstanceId = "lab-instance",
            remoteRoom = "room-1",
            channelId,
            remoteSubject = "remote-ada",
            displayName = "Remote Ada",
            body,
            cursor = 1,
            expiresAt,
            keyId,
            signature = signature ?? FederationRules.Sign(IntegrationToken.Hash(token), canonical)
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
        var rooms = await db.FederationRooms.IgnoreQueryFilters().Where(item => item.ChannelId == channel).ToListAsync();
        var roomIds = rooms.Select(item => item.Id).ToArray();
        var trustIds = rooms.Select(item => item.TrustId).Distinct().ToArray();
        db.FederationEvents.RemoveRange(await db.FederationEvents.IgnoreQueryFilters().Where(item => roomIds.Contains(item.RoomId)).ToListAsync());
        db.FederationIdentities.RemoveRange(await db.FederationIdentities.IgnoreQueryFilters().Where(item => trustIds.Contains(item.TrustId)).ToListAsync());
        db.FederationRooms.RemoveRange(rooms);
        db.FederationTrusts.RemoveRange(await db.FederationTrusts.IgnoreQueryFilters().Where(item => trustIds.Contains(item.Id)).ToListAsync());
        await db.SaveChangesAsync();
    }

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

    private sealed record Call(HttpStatusCode Status, string Raw, TrustBody Body, string Key);
    private sealed record IdDto(Guid Id);
    private sealed record PageDto(MessageDto[] Messages);
    private sealed record MessageDto(string Body, bool AuthorIsBot, DateTimeOffset? DeletedAt, RemoteDto? RemoteOrigin);
    private sealed record RemoteDto(string Network, string Room, string Subject, string DisplayName, string? Link, bool DeleteRemoteGuaranteed);

    private sealed class TrustBody
    {
        public Guid Id { get; set; }
        public Guid? RoomId { get; set; }
        public string? Token { get; set; }
        public string? Status { get; set; }
        public string? Domain { get; set; }
        public string? CurrentKeyId { get; set; }
        public bool ConsentRequired { get; set; }
        public bool DeleteRemoteGuaranteed { get; set; }
        public string? DeleteLimitation { get; set; }
        public string[]? Copied { get; set; }
        public bool CanManage { get; set; }
        public bool Remote { get; set; }
    }
}
