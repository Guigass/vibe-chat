using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VibeChat.Infrastructure;
using VibeChat.Realtime;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class LiveRulesTests
{
    [Fact]
    public void Room_is_bound_to_tenant_channel_and_session()
    {
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var channel = new ChannelId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var session = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var room = LiveRules.Room(tenant, channel, session);

        room.Should().Be("t/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/c/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/s/cccccccccccccccccccccccccccccccc");
        LiveRules.RoomMatches(room, room).Should().BeTrue();
        LiveRules.RoomMatches(room, LiveRules.Room(tenant, channel, Guid.NewGuid())).Should().BeFalse();
        LiveRules.RoomMatches("room-anywhere", room).Should().BeFalse();
    }

    [Fact]
    public void Video_policy_limits_sources_and_capacity_stops_at_the_quota()
    {
        LiveRules.PublishSources(false).Should().Equal("microphone");
        LiveRules.PublishSources(true).Should().Equal("microphone", "camera");
        LiveRules.HasCapacity(1, 2).Should().BeTrue();
        LiveRules.HasCapacity(2, 2).Should().BeFalse();
        LiveRules.TryReaction("👍", out var ok).Should().BeTrue();
        ok.Should().Be("👍");
        LiveRules.TryReaction("nope", out _).Should().BeFalse();
        LiveRules.ParticipantLimit(1).Should().Be(2);
        LiveRules.TokenTtl(1).Should().Be(LiveRules.MinTokenTtlSeconds);
    }

    [Fact]
    public void Dev_token_cannot_be_read_as_another_room()
    {
        var provider = Provider();
        var room = LiveRules.Room(new TenantId(Guid.NewGuid()), new ChannelId(Guid.NewGuid()), Guid.NewGuid());
        var issued = provider.Issue(new LiveTokenRequest(room, "user", true, false, DateTimeOffset.UtcNow.AddMinutes(1), "jti-1"));
        var claims = provider.Read(issued.Token);

        claims.Should().NotBeNull();
        claims!.Room.Should().Be(room);
        claims.CanPublishAudio.Should().BeTrue();
        claims.CanPublishVideo.Should().BeFalse();
        claims.Jti.Should().Be("jti-1");
        issued.SignalUrl.Should().BeNull();
        provider.Read(issued.Token + "x").Should().BeNull();
    }

    [Fact]
    public async Task Down_provider_is_unhealthy_and_disabled_live_stays_healthy()
    {
        var down = Provider(enabled: true, provider: LiveRules.ProviderDown);
        (await down.StatusAsync(CancellationToken.None)).Healthy.Should().BeFalse();

        var off = Provider(enabled: false, provider: LiveRules.ProviderDown);
        var status = await off.StatusAsync(CancellationToken.None);
        status.Healthy.Should().BeTrue();
        status.Detail.Should().Be("disabled");
    }

    private static LiveMediaProvider Provider(bool enabled = true, string provider = LiveRules.ProviderDev)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(LiveRules.HttpClientName);
        services.AddSingleton<IOptions<LiveOptions>>(Options.Create(new LiveOptions
        {
            Enabled = enabled,
            Provider = provider,
            VideoEnabled = false
        }));
        var http = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new LiveMediaProvider(Options.Create(new LiveOptions
        {
            Enabled = enabled,
            Provider = provider,
            VideoEnabled = false
        }), http);
    }
}
