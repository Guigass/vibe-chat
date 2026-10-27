using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class HaSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Member_cannot_drain_or_read_runtime_and_responses_have_no_message_body()
    {
        using var alice = factory.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var messageId = Guid.NewGuid();
        var secret = $"ha-secret-{messageId:N}";
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new { messageId, idempotencyKey = $"idem-{messageId:N}", body = secret });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var memberDrain = await alice.PostAsync("/api/v1/admin/ha/drain", null);
        var memberRuntime = await alice.GetAsync("/api/v1/meta/runtime");
        memberDrain.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        memberRuntime.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await memberDrain.Content.ReadAsStringAsync()).Should().NotContain(secret);
        (await memberRuntime.Content.ReadAsStringAsync()).Should().NotContain(secret);

        using var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var ownerDrain = await owner.PostAsync("/api/v1/admin/ha/drain", null);
        ownerDrain.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ownerDrain.Content.ReadAsStringAsync()).Should().Contain("HaDisabled").And.NotContain(secret);

        var history = await alice.GetStringAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages");
        history.Should().Contain(secret);
    }
}
