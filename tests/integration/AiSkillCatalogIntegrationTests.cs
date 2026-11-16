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
public sealed class AiSkillCatalogIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Disabled_flag_does_not_store_a_skill()
    {
        using var host = SkillHost(false);
        using var admin = Client(host, "demo");
        await using var db = factory.CreateMigratorDbContext();
        var before = await db.AiSkillDefinitions.IgnoreQueryFilters().CountAsync();
        var response = await SendAsync(admin, HttpMethod.Post, ListUrl(), Draft("off-skill", "Off", "não gravar"), "skill-off-flag");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(AiSkillErrors.Disabled);
        (await db.AiSkillDefinitions.IgnoreQueryFilters().CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Published_skill_is_reused_and_a_draft_does_not_change_bots()
    {
        using var host = SkillHost(true);
        using var admin = Client(host, "demo");
        var slug = $"oracle-{Guid.NewGuid():N}"[..20];
        const string first = "Responda com a política vigente.";
        const string second = "Consulte o anexo antes de responder.";

        var created = await SendAsync(admin, HttpMethod.Post, ListUrl(), Draft(slug, "Oráculo", first), $"skill-create-{slug}");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var skill = (await created.Content.ReadFromJsonAsync<SkillDto>(JsonOptions))!;
        var published = await SendAsync(admin, HttpMethod.Post, $"{ItemUrl(skill.Id)}/publish", Draft(slug, "Oráculo", first, skill.Revision), $"skill-publish-{slug}");
        published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var v1 = (await published.Content.ReadFromJsonAsync<SkillDto>(JsonOptions))!;
        v1.ActiveVersion.Should().Be(1);
        var versionId = v1.Versions.Single().Id;

        var alpha = await PublishBotAsync(admin, "Alpha", versionId, $"{slug}-a");
        var beta = await PublishBotAsync(admin, "Beta", versionId, $"{slug}-b");
        alpha.Should().NotBe(beta);

        var edited = await SendAsync(admin, HttpMethod.Put, ItemUrl(skill.Id), Draft(slug, "Oráculo", second, v1.Revision), $"skill-draft-{slug}");
        edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        await using var db = factory.CreateMigratorDbContext();
        var stored = await db.AiSkillVersions.IgnoreQueryFilters().SingleAsync(x => x.Id == versionId);
        stored.Instructions.Should().Be(first);
        stored.VersionNumber.Should().Be(1);
        var bots = await db.AiBotVersions.IgnoreQueryFilters().Where(x => x.BotId == alpha || x.BotId == beta).ToListAsync();
        bots.Should().OnlyContain(x => x.SkillVersionIds.SequenceEqual(new[] { versionId }));

        var v2Response = await SendAsync(admin, HttpMethod.Post, $"{ItemUrl(skill.Id)}/publish", Draft(slug, "Oráculo", second, v1.Revision + 1), $"skill-v2-{slug}");
        v2Response.StatusCode.Should().Be(HttpStatusCode.OK, await v2Response.Content.ReadAsStringAsync());
        (await db.AiSkillVersions.IgnoreQueryFilters().SingleAsync(x => x.Id == versionId)).Instructions.Should().Be(first);
        (await db.AiBotVersions.IgnoreQueryFilters().Where(x => x.BotId == alpha || x.BotId == beta).ToListAsync())
            .Should().OnlyContain(x => x.SkillVersionIds.SequenceEqual(new[] { versionId }));

        var current = (await v2Response.Content.ReadFromJsonAsync<SkillDto>(JsonOptions))!;
        var rolled = await SendAsync(admin, HttpMethod.Post, $"{ItemUrl(skill.Id)}/rollback", new { expectedRevision = current.Revision, versionNumber = 1 }, $"skill-rollback-{slug}");
        rolled.StatusCode.Should().Be(HttpStatusCode.OK, await rolled.Content.ReadAsStringAsync());
        var restored = (await rolled.Content.ReadFromJsonAsync<SkillDto>(JsonOptions))!;
        restored.ActiveVersion.Should().Be(1);
        restored.Draft.Instructions.Should().Be(first);
        (await db.AiSkillVersions.IgnoreQueryFilters().CountAsync(x => x.SkillId == skill.Id)).Should().Be(2);

        var exported = await admin.GetStringAsync($"{ItemUrl(skill.Id)}/export?version=1");
        exported.Should().Contain(AiSkillCatalogPolicies.ManifestSchema).And.Contain("\"version\":1").And.NotContain(second);
        var events = await db.OutboxMessages.IgnoreQueryFilters().Where(x => x.Payload.Contains(skill.Id.ToString())).ToListAsync();
        events.Should().Contain(x => x.Type == "ai.skill.published.v1");
        events.Should().OnlyContain(x => !x.Payload.Contains(first) && !x.Payload.Contains(second));

        var deprecated = await SendAsync(admin, HttpMethod.Post, $"{ItemUrl(skill.Id)}/deprecate", new { expectedRevision = restored.Revision }, $"skill-deprecate-{slug}");
        deprecated.StatusCode.Should().Be(HttpStatusCode.OK, await deprecated.Content.ReadAsStringAsync());
        var denied = await SendAsync(admin, HttpMethod.Post, BotListUrl(), BotDraft("Gamma", versionId), $"skill-deny-{slug}");
        denied.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await denied.Content.ReadAsStringAsync()).Should().Contain(AiSkillErrors.SkillReferenceRejected);
    }

    [Fact]
    public async Task Import_rejects_code_and_preserves_the_declared_version()
    {
        using var host = SkillHost(true);
        using var admin = Client(host, "demo");
        var slug = $"import-{Guid.NewGuid():N}"[..18];
        var malicious = await SendAsync(admin, HttpMethod.Post, $"{ListUrl()}/import", new
        {
            schema = AiSkillCatalogPolicies.ManifestSchema,
            slug,
            version = 3,
            name = "Ruim",
            description = "não",
            instructions = "ok",
            script = "eval(1)"
        }, $"skill-bad-{slug}");
        malicious.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await malicious.Content.ReadAsStringAsync()).Should().Contain(AiSkillErrors.SkillCodeRejected).And.NotContain("eval(1)");

        var imported = await SendAsync(admin, HttpMethod.Post, $"{ListUrl()}/import", Manifest(slug, 3, "Importe a política interna."), $"skill-import-{slug}");
        imported.StatusCode.Should().Be(HttpStatusCode.Created, await imported.Content.ReadAsStringAsync());
        var skill = (await imported.Content.ReadFromJsonAsync<SkillDto>(JsonOptions))!;
        skill.ActiveVersion.Should().Be(3);
        var collision = await SendAsync(admin, HttpMethod.Post, $"{ListUrl()}/import", Manifest(slug, 3, "Importe a política interna."), $"skill-collision-{slug}");
        collision.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await collision.Content.ReadAsStringAsync()).Should().Contain(AiSkillErrors.SkillSlugVersionConflict);
    }

    private async Task<Guid> PublishBotAsync(HttpClient admin, string name, Guid skillVersionId, string key)
    {
        var created = await SendAsync(admin, HttpMethod.Post, BotListUrl(), BotDraft(name, skillVersionId), $"bot-create-{key}");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var bot = (await created.Content.ReadFromJsonAsync<BotDto>(JsonOptions))!;
        var published = await SendAsync(admin, HttpMethod.Post, $"{BotListUrl()}/{bot.Id}/publish", BotDraft(name, skillVersionId, bot.Revision), $"bot-publish-{key}");
        published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        return bot.Id;
    }

    private static WebApplicationFactory<Program> SkillHost(VibeChatApiFactory source, bool enabled) =>
        source.WithWebHostBuilder(builder => builder.UseSetting("Features:AiBots:Enabled", enabled ? "true" : "false"));

    private WebApplicationFactory<Program> SkillHost(bool enabled) => SkillHost(factory, enabled);

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

    private static string ListUrl() => $"/api/v1/admin/ai/skills/workspaces/{WorkspaceId}";

    private static string ItemUrl(Guid skillId) => $"{ListUrl()}/{skillId}";

    private static string BotListUrl() => $"/api/v1/admin/ai/bots/workspaces/{WorkspaceId}";

    private static object Draft(string slug, string name, string instructions, int revision = 0) => new
    {
        slug,
        name,
        description = "Skill de teste",
        instructions,
        argumentsSchema = new { type = "object" },
        examples = new[] { new { name = "ping", input = new { }, expected = "pong" } },
        compatibility = "bots.v1",
        classification = "internal",
        knowledgeGrantIds = Array.Empty<Guid>(),
        mcpToolGrantIds = Array.Empty<Guid>(),
        expectedRevision = revision
    };

    private static object Manifest(string slug, int version, string instructions) => new
    {
        schema = AiSkillCatalogPolicies.ManifestSchema,
        slug,
        version,
        name = "Importada",
        description = "Skill importada",
        instructions,
        argumentsSchema = new { type = "object" },
        examples = Array.Empty<object>(),
        compatibility = "bots.v1",
        classification = "internal",
        knowledgeGrantIds = Array.Empty<Guid>(),
        mcpToolGrantIds = Array.Empty<Guid>()
    };

    private static object BotDraft(string name, Guid skillVersionId, int revision = 0) => new
    {
        name,
        description = "Bot de teste",
        allowDms = true,
        systemPrompt = "Seja breve.",
        provider = "mock",
        modelId = "mock-small",
        parameters = new { temperature = 0.2 },
        dataClass = "internal",
        channelIds = new[] { SeedData.DemoChannelId.Value },
        skillVersionIds = new[] { skillVersionId },
        dailyBudgetUsd = 1,
        expectedRevision = revision
    };

    private sealed record SkillDto(Guid Id, string Status, int Revision, int? ActiveVersion, DraftDto Draft, VersionDto[] Versions);

    private sealed record DraftDto(string Instructions);

    private sealed record VersionDto(Guid Id, int VersionNumber);

    private sealed record BotDto(Guid Id, int Revision);
}
