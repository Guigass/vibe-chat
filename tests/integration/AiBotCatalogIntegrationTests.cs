using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VibeChat.AI;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class AiBotCatalogIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Disabled_flag_does_not_store_a_bot()
    {
        using var host = BotHost(false);
        using var admin = Client(host, "demo");
        await using var db = factory.CreateMigratorDbContext();
        var before = await db.AiBotDefinitions.IgnoreQueryFilters().CountAsync();
        var response = await SendAsync(admin, HttpMethod.Post, ListUrl(), Draft("Off", "não gravar"), "off-flag1");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(AiBotErrors.Disabled);
        (await db.AiBotDefinitions.IgnoreQueryFilters().CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Publish_rollback_and_replay_keep_an_immutable_history()
    {
        using var host = BotHost(true);
        using var admin = Client(host, "demo");
        using var member = Client(host, "alice");
        const string first = "Responda só com a política interna.";
        const string second = "Consulte o pedido antes de responder.";

        var created = await SendAsync(admin, HttpMethod.Post, ListUrl(), Draft("Oráculo", first), "create-oracle");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var bot = (await created.Content.ReadFromJsonAsync<BotDto>(JsonOptions))!;
        var trainer = await SendAsync(admin, HttpMethod.Post, ListUrl(), Draft("Treinador", "Explique o exercício."), "create-trainer");
        trainer.StatusCode.Should().Be(HttpStatusCode.Created, await trainer.Content.ReadAsStringAsync());
        (await trainer.Content.ReadFromJsonAsync<BotDto>(JsonOptions))!.Id.Should().NotBe(bot.Id);
        var replay = await SendAsync(admin, HttpMethod.Post, ListUrl(), Draft("Oráculo", first), "create-oracle");
        (await replay.Content.ReadFromJsonAsync<BotDto>(JsonOptions))!.Id.Should().Be(bot.Id);

        var conflict = await SendAsync(admin, HttpMethod.Post, ListUrl(), Draft("Outro", first), "create-oracle");
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var published = await SendAsync(admin, HttpMethod.Post, $"{ItemUrl(bot.Id)}/publish", Draft("Oráculo", first, bot.Revision), "publish-v1");
        published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var v1 = (await published.Content.ReadFromJsonAsync<BotDto>(JsonOptions))!;
        v1.ActiveVersion.Should().Be(1);
        v1.Status.Should().Be("Published");

        var stale = await SendAsync(admin, HttpMethod.Post, $"{ItemUrl(bot.Id)}/publish", Draft("Oráculo", second, bot.Revision), "publish-stale");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var edited = await SendAsync(admin, HttpMethod.Put, ItemUrl(bot.Id), Draft("ERP", second, v1.Revision), "edit-draft-2");
        edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var draft = (await edited.Content.ReadFromJsonAsync<BotDto>(JsonOptions))!;
        var v2Response = await SendAsync(admin, HttpMethod.Post, $"{ItemUrl(bot.Id)}/publish", Draft("ERP", second, draft.Revision), "publish-v2");
        v2Response.StatusCode.Should().Be(HttpStatusCode.OK, await v2Response.Content.ReadAsStringAsync());
        var v2 = (await v2Response.Content.ReadFromJsonAsync<BotDto>(JsonOptions))!;
        v2.ActiveVersion.Should().Be(2);

        var rolled = await SendAsync(admin, HttpMethod.Post, $"{ItemUrl(bot.Id)}/rollback", new { expectedRevision = v2.Revision, versionNumber = 1 }, "rollback-v1");
        rolled.StatusCode.Should().Be(HttpStatusCode.OK, await rolled.Content.ReadAsStringAsync());
        var restored = (await rolled.Content.ReadFromJsonAsync<BotDto>(JsonOptions))!;
        restored.ActiveVersion.Should().Be(1);
        restored.Status.Should().Be("Published");
        restored.Draft.SystemPrompt.Should().Be(first);

        await using var db = factory.CreateMigratorDbContext();
        var versions = await db.AiBotVersions.IgnoreQueryFilters().Where(x => x.BotId == bot.Id).ToListAsync();
        versions.Single(x => x.VersionNumber == 1).SystemPrompt.Should().Be(first);
        versions.Single(x => x.VersionNumber == 2).SystemPrompt.Should().Be(second);
        var events = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Payload.Contains(bot.Id.ToString()))
            .ToListAsync();
        events.Should().Contain(x => x.Type == "ai.bot.published.v1");
        events.Should().Contain(x => x.Type == "ai.bot.rolled_back.v1");
        events.Should().OnlyContain(x => !x.Payload.Contains(first) && !x.Payload.Contains(second));

        var visible = await member.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/ai/bots");
        visible.Should().Contain("Oráculo");
        visible.Should().NotContain(first);

        var disabled = await SendAsync(admin, HttpMethod.Post, $"{ItemUrl(bot.Id)}/disable", new { expectedRevision = restored.Revision }, "disable-bot");
        disabled.StatusCode.Should().Be(HttpStatusCode.OK, await disabled.Content.ReadAsStringAsync());
        var invoked = await SendAsync(member, HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/ai/bots/{bot.Id}/invocations", new { }, "invoke-bot");
        invoked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await invoked.Content.ReadAsStringAsync()).Should().Contain(AiBotErrors.BotNotInvocable);
        (await db.UserProfiles.IgnoreQueryFilters().AnyAsync(x => x.Id == new UserId(restored.PrincipalUserId))).Should().BeTrue();
    }

    private static WebApplicationFactory<Program> BotHost(VibeChatApiFactory factory, bool enabled) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("Features:AiBots:Enabled", enabled ? "true" : "false"));

    private WebApplicationFactory<Program> BotHost(bool enabled) => BotHost(factory, enabled);

    private static HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object body, string key)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static string ListUrl() => $"/api/v1/admin/ai/bots/workspaces/{WorkspaceId}";

    private static string ItemUrl(Guid botId) => $"{ListUrl()}/{botId}";

    private static object Draft(string name, string prompt, int revision = 0) => new
    {
        name,
        description = "Bot de teste",
        allowDms = true,
        systemPrompt = prompt,
        provider = name == "ERP" ? "openrouter" : "mock",
        modelId = name == "ERP" ? "erp-model" : "mock-small",
        parameters = new { temperature = 0.2 },
        dataClass = "internal",
        channelIds = new[] { SeedData.DemoChannelId.Value },
        dailyBudgetUsd = 1,
        expectedRevision = revision
    };

    private sealed record BotDto(Guid Id, string Status, int Revision, int? ActiveVersion, Guid PrincipalUserId, DraftDto Draft);

    private sealed record DraftDto(string SystemPrompt);
}
