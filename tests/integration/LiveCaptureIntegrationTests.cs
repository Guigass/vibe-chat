using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-148 — screen share stops on revoke, and recording waits for consent.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class LiveCaptureIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Share_stops_when_revoked_and_recording_never_starts_on_decline()
    {
        using var host = LiveHost(factory, share: true, recording: true, quota: LiveRules.DefaultQuotaBytes);
        using var demo = Client(host, "demo");
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        var channelId = await CreateChannelAsync(demo);
        await EnablePolicyAsync(demo, share: true, recording: true);
        Guid sessionId = Guid.Empty;
        try
        {
            sessionId = await StartSessionAsync(alice, channelId);
            (await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/share", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await JoinAsync(alice, channelId, sessionId)).EnsureSuccessStatusCode();
            var started = await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/share", null);
            started.EnsureSuccessStatusCode();
            var grant = await JoinAsync(alice, channelId, sessionId);
            grant.EnsureSuccessStatusCode();
            (await grant.Content.ReadFromJsonAsync<LiveGrant>(Json))!.CanPublishScreen.Should().BeTrue();

            var revoke = await demo.PostAsJsonAsync(
                $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/share/revoke",
                new { userId = SeedData.AliceUserId.Value });
            revoke.EnsureSuccessStatusCode();
            var after = await alice.GetFromJsonAsync<LiveView>($"/api/v1/channels/{channelId}/live", Json);
            after!.Participants.Should().ContainSingle(person => person.UserId == SeedData.AliceUserId.Value)
                .Which.SharingScreen.Should().BeFalse();

            (await JoinAsync(bob, channelId, sessionId)).EnsureSuccessStatusCode();
            var denied = await alice.SendAsync(RecordingRequest(channelId, sessionId));
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var requested = await demo.SendAsync(RecordingRequest(channelId, sessionId));
            requested.StatusCode.Should().Be(HttpStatusCode.Created);
            var recording = (await requested.Content.ReadFromJsonAsync<RecordingView>(Json))!;
            recording.State.Should().Be(LiveRules.RecordingConsent);
            recording.StartedBy.Should().Be(SeedData.DemoUserId.Value);

            var early = await alice.PostAsync(DownloadPath(channelId, sessionId, recording.Id), null);
            early.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadError(early)).Should().Be(LiveErrors.RecordingNotReady);

            var declined = await alice.PostAsJsonAsync(ConsentPath(channelId, sessionId, recording.Id), new { decision = "decline" });
            declined.EnsureSuccessStatusCode();
            (await declined.Content.ReadFromJsonAsync<RecordingView>(Json))!.State.Should().Be(LiveRules.RecordingDeclined);

            await using var db = factory.CreateMigratorDbContext();
            var stored = await db.LiveRecordings.IgnoreQueryFilters().SingleAsync(x => x.Id == recording.Id);
            stored.ObjectKey.Should().BeNull();
            var audit = await db.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.EntityId == recording.Id.ToString("D") && x.Action == AuditActions.LiveRecordingRequest)
                .SingleAsync();
            audit.ActorUserId.Should().Be(SeedData.DemoUserId);
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
                await bob.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
            }
        }
    }

    [Fact]
    public async Task Recording_download_expires_respects_acl_and_hold_keeps_the_object()
    {
        using var host = LiveHost(factory, share: true, recording: true, quota: LiveRules.DefaultQuotaBytes);
        using var demo = Client(host, "demo");
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        var channelId = await CreateChannelAsync(demo);
        await EnablePolicyAsync(demo, share: true, recording: true);
        var sessionId = await StartSessionAsync(alice, channelId);
        Guid? holdId = null;
        try
        {
            (await JoinAsync(alice, channelId, sessionId)).EnsureSuccessStatusCode();
            (await JoinAsync(bob, channelId, sessionId)).EnsureSuccessStatusCode();
            var requested = await demo.SendAsync(RecordingRequest(channelId, sessionId));
            requested.StatusCode.Should().Be(HttpStatusCode.Created);
            var recording = (await requested.Content.ReadFromJsonAsync<RecordingView>(Json))!;
            (await alice.PostAsJsonAsync(ConsentPath(channelId, sessionId, recording.Id), new { decision = "accept" })).EnsureSuccessStatusCode();
            var still = await bob.GetAsync(RecordingPath(channelId, sessionId, recording.Id));
            still.EnsureSuccessStatusCode();
            (await still.Content.ReadFromJsonAsync<RecordingView>(Json))!.State.Should().Be(LiveRules.RecordingConsent);

            (await bob.PostAsJsonAsync(ConsentPath(channelId, sessionId, recording.Id), new { decision = "accept" })).EnsureSuccessStatusCode();
            var stopped = await demo.PostAsync(StopPath(channelId, sessionId, recording.Id), null);
            stopped.EnsureSuccessStatusCode();
            (await stopped.Content.ReadFromJsonAsync<RecordingView>(Json))!.State.Should().Be(LiveRules.RecordingReady);

            await using (var db = factory.CreateMigratorDbContext())
            {
                var row = await db.LiveRecordings.IgnoreQueryFilters().SingleAsync(x => x.Id == recording.Id);
                row.ObjectKey.Should().NotBeNull();
                LiveRules.IsPublicUrl(row.ObjectKey).Should().BeFalse();
                var payload = await db.OutboxMessages.IgnoreQueryFilters()
                    .Where(x => x.Type == LiveRules.RecordingEvent)
                    .OrderByDescending(x => x.OccurredAt)
                    .Select(x => x.Payload)
                    .FirstAsync();
                payload.Should().Contain(SeedData.DemoUserId.Value.ToString()).And.NotContain("http");
            }

            var download = await bob.PostAsync(DownloadPath(channelId, sessionId, recording.Id), null);
            download.EnsureSuccessStatusCode();
            var link = (await download.Content.ReadFromJsonAsync<DownloadLink>(Json))!;
            link.Url.Should().Contain("http");
            link.ExpiresAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddMinutes(6));

            var outsider = await alice.PostAsync(
                $"/api/v1/channels/{Guid.NewGuid()}/live/sessions/{sessionId}/recordings/{recording.Id}/download",
                null);
            outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            await using (var db = factory.CreateMigratorDbContext())
            {
                var row = await db.LiveRecordings.IgnoreQueryFilters().SingleAsync(x => x.Id == recording.Id);
                row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
                await db.SaveChangesAsync();
            }

            var expired = await alice.PostAsync(DownloadPath(channelId, sessionId, recording.Id), null);
            expired.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadError(expired)).Should().Be(LiveErrors.RecordingExpired);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
                var key = LiveRules.ObjectKey(SeedData.DemoTenantId, recording.Id);
                (await storage.GetObjectAsync(key, CancellationToken.None)).Should().BeNull();
            }

            var second = await demo.SendAsync(RecordingRequest(channelId, sessionId));
            second.StatusCode.Should().Be(HttpStatusCode.Created);
            var held = (await second.Content.ReadFromJsonAsync<RecordingView>(Json))!;
            (await alice.PostAsJsonAsync(ConsentPath(channelId, sessionId, held.Id), new { decision = "accept" })).EnsureSuccessStatusCode();
            (await bob.PostAsJsonAsync(ConsentPath(channelId, sessionId, held.Id), new { decision = "accept" })).EnsureSuccessStatusCode();
            (await demo.PostAsync(StopPath(channelId, sessionId, held.Id), null)).EnsureSuccessStatusCode();
            await using (var db = factory.CreateMigratorDbContext())
            {
                var row = await db.LiveRecordings.IgnoreQueryFilters().SingleAsync(x => x.Id == held.Id);
                row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
                var holdKey = Guid.NewGuid();
                holdId = holdKey;
                db.LegalHolds.Add(new LegalHold
                {
                    Id = holdKey,
                    TenantId = SeedData.DemoTenantId,
                    WorkspaceId = SeedData.DemoWorkspaceId,
                    ScopeKind = LegalHoldScope.Workspace,
                    Reason = "reter gravacao",
                    Status = LegalHoldStatus.Active,
                    RequestedBy = SeedData.DemoUserId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    IdempotencyKey = "hold-" + held.Id.ToString("N")
                });
                await db.SaveChangesAsync();
            }

            var kept = await bob.PostAsync(DownloadPath(channelId, sessionId, held.Id), null);
            kept.EnsureSuccessStatusCode();
        }
        finally
        {
            await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
            await bob.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
            if (holdId is Guid created)
            {
                await using var db = factory.CreateMigratorDbContext();
                var hold = await db.LegalHolds.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == created);
                if (hold is not null)
                {
                    db.LegalHolds.Remove(hold);
                    await db.SaveChangesAsync();
                }
            }
        }
    }

    [Fact]
    public async Task Workspace_policy_cannot_outrun_the_instance_flag()
    {
        using var host = LiveHost(factory, share: false, recording: false, quota: LiveRules.DefaultQuotaBytes);
        using var demo = Client(host, "demo");
        using var alice = Client(host, "alice");
        var channelId = await CreateChannelAsync(demo);
        await EnablePolicyAsync(demo, share: true, recording: true);
        var health = await demo.GetFromJsonAsync<LiveHealth>($"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/live/health", Json);
        health!.ShareEnabled.Should().BeFalse();
        health.CanRecord.Should().BeFalse();
        var sessionId = await StartSessionAsync(alice, channelId);
        (await JoinAsync(alice, channelId, sessionId)).EnsureSuccessStatusCode();
        var share = await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/share", null);
        share.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadError(share)).Should().Be(LiveErrors.ShareDisabled);
        await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
    }

    [Fact]
    public async Task Recording_over_quota_does_not_store_an_object()
    {
        using var host = LiveHost(factory, share: true, recording: true, quota: 1);
        using var demo = Client(host, "demo");
        using var alice = Client(host, "alice");
        var channelId = await CreateChannelAsync(demo);
        await EnablePolicyAsync(demo, share: true, recording: true);
        var sessionId = await StartSessionAsync(alice, channelId);
        (await JoinAsync(alice, channelId, sessionId)).EnsureSuccessStatusCode();
        var requested = await demo.SendAsync(RecordingRequest(channelId, sessionId));
        requested.StatusCode.Should().Be(HttpStatusCode.Created);
        var recording = (await requested.Content.ReadFromJsonAsync<RecordingView>(Json))!;
        (await alice.PostAsJsonAsync(ConsentPath(channelId, sessionId, recording.Id), new { decision = "accept" })).EnsureSuccessStatusCode();
        var stopped = await demo.PostAsync(StopPath(channelId, sessionId, recording.Id), null);
        stopped.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadError(stopped)).Should().Be(LiveErrors.RecordingQuota);
        await using var db = factory.CreateMigratorDbContext();
        (await db.LiveRecordings.IgnoreQueryFilters().SingleAsync(x => x.Id == recording.Id)).ObjectKey.Should().BeNull();
        await alice.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/leave", null);
    }

    private static WebApplicationFactory<Program> LiveHost(
        VibeChatApiFactory factory, bool share, bool recording, long quota) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Features:Live:Enabled", "true");
            builder.UseSetting("Features:Live:VideoEnabled", "false");
            builder.UseSetting("Features:Live:Provider", LiveRules.ProviderDev);
            builder.UseSetting("Features:Live:ShareEnabled", share ? "true" : "false");
            builder.UseSetting("Features:Live:RecordingEnabled", recording ? "true" : "false");
            builder.UseSetting("Features:Live:RecordingQuotaBytes", quota.ToString());
            builder.UseSetting("Features:Live:MaxParticipants", "8");
            builder.UseSetting("Features:Live:MaxSessionsPerWorkspace", "4");
        });

    private static async Task EnablePolicyAsync(HttpClient demo, bool share, bool recording)
    {
        var response = await demo.PutAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/live/policy",
            new { shareEnabled = share, recordingEnabled = recording });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> StartSessionAsync(HttpClient client, Guid channelId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/channels/{channelId}/live/sessions");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "live-" + Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LiveView>(Json))!.SessionId;
    }

    private static async Task<Guid> CreateChannelAsync(HttpClient demo)
    {
        var created = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name = "cap-" + Guid.NewGuid().ToString("N")[..10], type = "Public" });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<CreatedChannel>(Json))!.Id;
    }

    private static HttpRequestMessage RecordingRequest(Guid channelId, Guid sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/recordings");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "rec-" + Guid.NewGuid().ToString("N"));
        return request;
    }

    private static string ConsentPath(Guid channelId, Guid sessionId, Guid recordingId) =>
        $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/recordings/{recordingId}/consent";

    private static string StopPath(Guid channelId, Guid sessionId, Guid recordingId) =>
        $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/recordings/{recordingId}/stop";

    private static string RecordingPath(Guid channelId, Guid sessionId, Guid recordingId) =>
        $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/recordings/{recordingId}";

    private static string DownloadPath(Guid channelId, Guid sessionId, Guid recordingId) =>
        RecordingPath(channelId, sessionId, recordingId) + "/download";

    private static Task<HttpResponseMessage> JoinAsync(HttpClient client, Guid channelId, Guid sessionId) =>
        client.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/join", null);

    private static HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static async Task<string?> ReadError(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("error").GetString();
    }

    private sealed record CreatedChannel(Guid Id);
    private sealed record LiveHealth(bool ShareEnabled, bool CanRecord);
    private sealed record LiveView(Guid SessionId, LivePerson[] Participants);
    private sealed record LivePerson(Guid UserId, bool SharingScreen);
    private sealed record LiveGrant(bool CanPublishScreen);
    private sealed record RecordingView(Guid Id, string State, Guid StartedBy);
    private sealed record DownloadLink(string Url, DateTimeOffset ExpiresAt);
}
