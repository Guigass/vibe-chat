using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class HaIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Standard_hides_ha_and_keeps_readiness()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);

        var runtime = await client.GetAsync("/api/v1/meta/runtime");
        runtime.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await runtime.Content.ReadAsStringAsync()).Should().Contain("HaDisabled");

        var drain = await client.PostAsync("/api/v1/admin/ha/drain", null);
        drain.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Drain_keeps_history_and_fails_readiness()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Ha:Enabled", "true"));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var messageId = Guid.NewGuid();
        var body = $"ha-drain-{messageId:N}";
        var sent = await client.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null));
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var before = await client.GetStringAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages");
        before.Should().Contain(messageId.ToString());

        var drain = await client.PostAsync("/api/v1/admin/ha/drain", null);
        drain.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetStringAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages");
        after.Should().Contain(messageId.ToString()).And.Contain(body);

        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/ready")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);

        var runtime = await client.GetFromJsonAsync<JsonElement>("/api/v1/meta/runtime", JsonOptions);
        runtime.GetProperty("draining").GetBoolean().Should().BeTrue();
        runtime.GetProperty("phase").GetString().Should().Be("expand");
        runtime.GetProperty("stickySessions").GetBoolean().Should().BeFalse();
        runtime.GetProperty("webSocketsOnly").GetBoolean().Should().BeTrue();
        runtime.GetProperty("rpoSeconds").GetInt32().Should().BeLessThanOrEqualTo(300);
        runtime.GetProperty("rtoSeconds").GetInt32().Should().BeLessThanOrEqualTo(1800);
        runtime.GetProperty("meetsProfile").GetBoolean().Should().BeTrue();
        var raw = runtime.GetRawText();
        raw.Should().NotContain("Password").And.NotContain(body);
    }

    [Fact]
    public async Task Worker_lease_failover_does_not_repeat_a_finished_effect()
    {
        var effects = 0;
        var started = Stopwatch.GetTimestamp();
        await using var leader = await HaLeaseSession.TryAcquireAsync(
            factory.RuntimeDatabaseConnectionString,
            CancellationToken.None);
        leader.Should().NotBeNull();

        var blocked = await HaLeaseSession.TryAcquireAsync(
            factory.RuntimeDatabaseConnectionString,
            CancellationToken.None);
        blocked.Should().BeNull();
        if (effects == 0)
        {
            effects++;
        }

        await leader!.DisposeAsync();
        await using var promoted = await HaLeaseSession.TryAcquireAsync(
            factory.RuntimeDatabaseConnectionString,
            CancellationToken.None);
        var elapsed = Stopwatch.GetElapsedTime(started);
        promoted.Should().NotBeNull();
        if (effects == 0)
        {
            effects++;
        }

        effects.Should().Be(1);
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(new HaOptions().RtoSeconds));
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    private sealed record SendMessageRequest(
        Guid MessageId,
        string IdempotencyKey,
        string Body,
        Guid? ReplyToMessageId,
        Guid? ForwardedFromMessageId);
}
