using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-109 — bot token send, idempotency, DM, revoke and kill switch.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class IntegrationBotIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Bot_posts_to_scoped_channel_and_replay_is_idempotent()
    {
        using var admin = Client("demo");
        var created = await CreateBotAsync(admin, [ChannelId], allowDms: true);
        created.Token.Should().StartWith("vc_int_");

        var listed = await admin.GetStringAsync($"/api/v1/admin/workspaces/{WorkspaceId}/bots");
        listed.Should().NotContain(created.Token);
        listed.Should().Contain(created.Token[^4..]);

        using var bot = BotClient(created.Token);
        var key = $"idem-{Guid.NewGuid():N}";
        var body = $"bot-hello-{Guid.NewGuid():N}";
        var first = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body, idempotencyKey = key });
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var sent = await first.Content.ReadFromJsonAsync<SentDto>(Json);
        sent.Should().NotBeNull();
        sent!.Idempotent.Should().BeFalse();

        var second = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body, idempotencyKey = key });
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var replay = await second.Content.ReadFromJsonAsync<SentDto>(Json);
        replay!.MessageId.Should().Be(sent.MessageId);
        replay.Idempotent.Should().BeTrue();

        using var alice = Client("alice");
        var history = await alice.GetFromJsonAsync<HistoryDto>(
            $"/api/v1/channels/{ChannelId}/messages?after={sent.Sequence - 1}",
            Json);
        history!.Messages.Should().Contain(m =>
            m.Id == sent.MessageId && m.Body == body && m.AuthorIsBot && m.AuthorName == "CI Bot");

        await using var db = factory.CreateMigratorDbContext();
        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == nameof(MessageCreatedEvent))
            .OrderByDescending(x => x.OccurredAt)
            .Take(15)
            .ToListAsync();
        outbox.Should().Contain(x =>
            x.Payload.Contains(sent.MessageId.ToString(), StringComparison.OrdinalIgnoreCase)
            && x.Payload.Contains("\"authorIsBot\":true", StringComparison.Ordinal));

        var count = await db.Messages.IgnoreQueryFilters().CountAsync(x => x.Id == new MessageId(sent.MessageId));
        count.Should().Be(1);
    }

    [Fact]
    public async Task Out_of_scope_channel_is_forbidden_and_dm_requires_allow_dms()
    {
        using var admin = Client("demo");
        var privateChannel = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = $"bot-private-{Guid.NewGuid():N}"[..24], type = "Private" });
        privateChannel.EnsureSuccessStatusCode();
        var channel = await privateChannel.Content.ReadFromJsonAsync<ChannelDto>(Json);

        var created = await CreateBotAsync(admin, [ChannelId], allowDms: false);
        using var bot = BotClient(created.Token);

        var denied = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{channel!.Id}/messages",
            new { body = "nope", idempotencyKey = $"scope-{Guid.NewGuid():N}" });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var dmDenied = await bot.PostAsJsonAsync(
            "/api/v1/integrations/v1/dms",
            new { userId = SeedData.BobUserId.Value, body = "oi", idempotencyKey = $"dm-{Guid.NewGuid():N}" });
        dmDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var allowed = await CreateBotAsync(admin, [ChannelId], allowDms: true);
        using var dmBot = BotClient(allowed.Token);
        var dm = await dmBot.PostAsJsonAsync(
            "/api/v1/integrations/v1/dms",
            new { userId = SeedData.BobUserId.Value, body = "ola bob", idempotencyKey = $"dm-ok-{Guid.NewGuid():N}" });
        dm.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var sent = await dm.Content.ReadFromJsonAsync<SentDto>(Json);

        using var bob = Client("bob");
        var history = await bob.GetFromJsonAsync<HistoryDto>(
            $"/api/v1/channels/{sent!.ChannelId}/messages?after=0",
            Json);
        history!.Messages.Should().Contain(m => m.Id == sent.MessageId && m.AuthorIsBot);
    }

    [Fact]
    public async Task Revoked_token_is_rejected_immediately()
    {
        using var admin = Client("demo");
        var created = await CreateBotAsync(admin, [ChannelId], allowDms: false);
        var revoke = await admin.PostAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/bots/{created.Id}/revoke",
            null);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        using var bot = BotClient(created.Token);
        var send = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "after revoke", idempotencyKey = $"rev-{Guid.NewGuid():N}" });
        send.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Kill_switch_hides_admin_and_send()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Integrations:Bots:Enabled", "false"));
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "demo");

        var list = await client.GetAsync($"/api/v1/admin/workspaces/{WorkspaceId}/bots");
        list.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var bot = disabled.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "vc_int_deadbeef");
        var send = await bot.PostAsJsonAsync(
            $"/api/v1/integrations/v1/channels/{ChannelId}/messages",
            new { body = "off", idempotencyKey = "off" });
        send.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    private async Task<CreatedBot> CreateBotAsync(HttpClient admin, Guid[] channelIds, bool allowDms)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{WorkspaceId}/bots",
            new { name = "CI Bot", channelIds, allowDms });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedBot>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private sealed record CreatedBot(Guid Id, string Token, string TokenLast4);
    private sealed record SentDto(Guid MessageId, Guid ChannelId, long Sequence, bool Idempotent);
    private sealed record HistoryDto(HistoryMessage[] Messages);
    private sealed record HistoryMessage(Guid Id, string Body, string AuthorName, bool AuthorIsBot);
    private sealed record ChannelDto(Guid Id);
}
