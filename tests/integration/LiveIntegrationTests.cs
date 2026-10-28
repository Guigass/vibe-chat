using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-147 — huddle session, audio grant, replay and provider failure.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class LiveIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Flag_off_hides_live_and_chat_still_sends()
    {
        using var alice = Client(factory, "alice");
        var health = await alice.GetAsync($"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/live/health");
        health.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadError(health)).Should().Be(LiveErrors.Disabled);

        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId = Guid.NewGuid(), idempotencyKey = "live-off-" + Guid.NewGuid().ToString("N"), body = "chat segue" });
        sent.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Two_members_connect_audio_and_replay_or_outsider_cannot()
    {
        using var host = LiveHost(factory, video: false, participants: 8, sessions: 4);
        using var demo = Client(host, "demo");
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        var channelId = await CreateChannelAsync(demo);
        string? aliceToken = null;
        Guid sessionId = Guid.Empty;
        try
        {
            var started = await StartAsync(alice, channelId);
            started.StatusCode.Should().Be(HttpStatusCode.Created);
            sessionId = (await started.Content.ReadFromJsonAsync<LiveState>(Json))!.SessionId;
            var again = await StartAsync(alice, channelId);
            again.StatusCode.Should().Be(HttpStatusCode.OK);
            (await again.Content.ReadFromJsonAsync<LiveState>(Json))!.SessionId.Should().Be(sessionId);

            var aliceJoin = await JoinAsync(alice, channelId, sessionId);
            aliceJoin.StatusCode.Should().Be(HttpStatusCode.OK);
            var aliceGrant = (await aliceJoin.Content.ReadFromJsonAsync<LiveGrant>(Json))!;
            aliceGrant.CanPublishAudio.Should().BeTrue();
            aliceGrant.CanPublishVideo.Should().BeFalse();
            aliceGrant.Room.Should().Contain(channelId.ToString("N"));
            aliceToken = aliceGrant.Token;

            (await ConnectAsync(alice, channelId, sessionId, aliceToken)).StatusCode.Should().Be(HttpStatusCode.OK);
            var replay = await ConnectAsync(alice, channelId, sessionId, aliceToken);
            replay.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReadError(replay)).Should().Be(LiveErrors.TokenReplayed);

            var bobJoin = await JoinAsync(bob, channelId, sessionId);
            bobJoin.EnsureSuccessStatusCode();
            var bobGrant = (await bobJoin.Content.ReadFromJsonAsync<LiveGrant>(Json))!;
            (await ConnectAsync(bob, channelId, sessionId, bobGrant.Token)).EnsureSuccessStatusCode();

            var state = await alice.GetFromJsonAsync<LiveState>($"/api/v1/channels/{channelId}/live", Json);
            state!.VideoAllowed.Should().BeFalse();
            state.Participants.Should().HaveCount(2);
            state.Participants.Should().OnlyContain(row => row.AudioConnected);

            var outsider = await alice.PostAsync($"/api/v1/channels/{Guid.NewGuid()}/live/sessions/{sessionId}/join", null);
            outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var stolen = await bob.PostAsJsonAsync(
                $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/connect",
                new { token = aliceToken });
            stolen.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReadError(stolen)).Should().Be(LiveErrors.TokenReplayed);

            var camera = await alice.PutAsJsonAsync(
                $"/api/v1/channels/{channelId}/live/sessions/{sessionId}/media",
                new { camera = true });
            camera.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReadError(camera)).Should().Be(LiveErrors.VideoDisabled);

            await using var db = factory.CreateMigratorDbContext();
            var payload = await db.OutboxMessages.IgnoreQueryFilters()
                .Where(x => x.Type == LiveRules.EventType)
                .OrderByDescending(x => x.OccurredAt)
                .Select(x => x.Payload)
                .FirstAsync();
            payload.Should().NotContain("token").And.NotContain(aliceToken);
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
    public async Task Capacity_room_mismatch_and_provider_failure_do_not_drop_chat()
    {
        using var host = LiveHost(factory, video: false, participants: 2, sessions: 1);
        using var demo = Client(host, "demo");
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        var first = await CreateChannelAsync(demo);
        var second = await CreateChannelAsync(demo);
        Guid firstSession = Guid.Empty;
        Guid secondAttempt = Guid.Empty;
        try
        {
            var started = await StartAsync(alice, first);
            started.EnsureSuccessStatusCode();
            firstSession = (await started.Content.ReadFromJsonAsync<LiveState>(Json))!.SessionId;
            (await JoinAsync(alice, first, firstSession)).EnsureSuccessStatusCode();
            (await JoinAsync(bob, first, firstSession)).EnsureSuccessStatusCode();
            var third = await JoinAsync(demo, first, firstSession);
            third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            (await ReadError(third)).Should().Be(LiveErrors.Capacity);

            var blocked = await StartAsync(alice, second);
            blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

            var aliceGrant = await JoinAgainAsync(alice, first, firstSession);
            var other = await StartOnOpenWorkspaceAsync();
            if (other is not null)
            {
                var mismatch = await alice.PostAsJsonAsync(
                    $"/api/v1/channels/{other.Value.ChannelId}/live/sessions/{other.Value.SessionId}/connect",
                    new { token = aliceGrant });
                mismatch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                (await ReadError(mismatch)).Should().Be(LiveErrors.RoomMismatch);
                secondAttempt = other.Value.SessionId;
                await alice.PostAsync($"/api/v1/channels/{other.Value.ChannelId}/live/sessions/{secondAttempt}/leave", null);
            }
        }
        finally
        {
            await alice.PostAsync($"/api/v1/channels/{first}/live/sessions/{firstSession}/leave", null);
            await bob.PostAsync($"/api/v1/channels/{first}/live/sessions/{firstSession}/leave", null);
            await demo.PostAsync($"/api/v1/channels/{first}/live/sessions/{firstSession}/leave", null);
        }

        using var down = LiveHost(factory, video: false, participants: 8, sessions: 2, provider: LiveRules.ProviderDown);
        using var sender = Client(down, "alice");
        var channel = await CreateChannelAsync(Client(factory, "demo"));
        var unavailable = await StartAsync(sender, channel);
        unavailable.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ReadError(unavailable)).Should().Be(LiveErrors.ProviderUnavailable);
        var sent = await sender.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId = Guid.NewGuid(), idempotencyKey = "live-down-" + Guid.NewGuid().ToString("N"), body = "chat segue com o provider fora" });
        sent.IsSuccessStatusCode.Should().BeTrue();
    }

    private async Task<(Guid ChannelId, Guid SessionId)?> StartOnOpenWorkspaceAsync()
    {
        using var open = LiveHost(factory, video: false, participants: 8, sessions: 4);
        using var demo = Client(open, "demo");
        using var alice = Client(open, "alice");
        var channelId = await CreateChannelAsync(demo);
        var started = await StartAsync(alice, channelId);
        if (!started.IsSuccessStatusCode)
        {
            return null;
        }

        var sessionId = (await started.Content.ReadFromJsonAsync<LiveState>(Json))!.SessionId;
        (await JoinAsync(alice, channelId, sessionId)).EnsureSuccessStatusCode();
        return (channelId, sessionId);
    }

    private static async Task<string> JoinAgainAsync(HttpClient client, Guid channelId, Guid sessionId)
    {
        var response = await JoinAsync(client, channelId, sessionId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LiveGrant>(Json))!.Token;
    }

    private static WebApplicationFactory<Program> LiveHost(
        VibeChatApiFactory factory, bool video, int participants, int sessions, string provider = LiveRules.ProviderDev) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Features:Live:Enabled", "true");
            builder.UseSetting("Features:Live:VideoEnabled", video ? "true" : "false");
            builder.UseSetting("Features:Live:Provider", provider);
            builder.UseSetting("Features:Live:MaxParticipants", participants.ToString());
            builder.UseSetting("Features:Live:MaxSessionsPerWorkspace", sessions.ToString());
        });

    private static async Task<Guid> CreateChannelAsync(HttpClient demo)
    {
        var created = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/channels",
            new { name = "live-" + Guid.NewGuid().ToString("N")[..10], type = "Public" });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<CreatedChannel>(Json))!.Id;
    }

    private static Task<HttpResponseMessage> StartAsync(HttpClient client, Guid channelId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/channels/{channelId}/live/sessions");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "live-" + Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> JoinAsync(HttpClient client, Guid channelId, Guid sessionId) =>
        client.PostAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/join", null);

    private static Task<HttpResponseMessage> ConnectAsync(HttpClient client, Guid channelId, Guid sessionId, string token) =>
        client.PostAsJsonAsync($"/api/v1/channels/{channelId}/live/sessions/{sessionId}/connect", new { token });

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
    private sealed record LiveState(Guid SessionId, string State, bool VideoAllowed, LivePerson[] Participants);
    private sealed record LivePerson(Guid UserId, bool AudioConnected);
    private sealed record LiveGrant(string Token, string Room, bool CanPublishAudio, bool CanPublishVideo);
}
