using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.AI;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Ai;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class BotKnowledgeIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private const string Policy = "Politica de ferias coletivas no mes de dezembro para o time.";

    [Fact]
    public async Task Disabled_flag_does_not_store_a_source()
    {
        using var host = Host(false);
        using var admin = Client(host, "demo");
        await using var db = factory.CreateMigratorDbContext();
        var before = await db.BotKnowledgeSources.IgnoreQueryFilters().CountAsync();
        var response = await SendAsync(admin, HttpMethod.Post, ListUrl(), FileBody("off", Policy), "knowledge-off");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(BotKnowledgeErrors.Disabled);
        (await db.BotKnowledgeSources.IgnoreQueryFilters().CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task File_and_page_reach_ready_with_a_stable_checksum()
    {
        using var host = Host(true);
        using var admin = Client(host, "demo");
        await admin.GetAsync(ListUrl());
        var pageId = await SeedPageAsync(Policy + " pagina");

        var first = await CreateAsync(admin, FileBody("Ferias", Policy), "knowledge-file-a");
        var second = await CreateAsync(admin, FileBody("Ferias copia", Policy), "knowledge-file-b");
        first.Status.Should().Be("Ready");
        first.ChunkCount.Should().BeGreaterThan(0);
        second.Checksum.Should().Be(first.Checksum);
        first.ContentSha256.Should().NotBeNullOrEmpty();

        var page = await CreateAsync(admin, new { name = "Pagina", classification = "internal", kind = "page", pageId }, "knowledge-page-a");
        page.Status.Should().Be("Ready");
        page.Checksum.Should().NotBe(first.Checksum);

        var eicar = await CreateAsync(admin, new
        {
            name = "Eicar",
            classification = "internal",
            kind = "file",
            text = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"
        }, "knowledge-file-eicar");
        eicar.Status.Should().Be("Failed");
        eicar.LastError.Should().Be(BotKnowledgeErrors.MalwareQuarantined);

        await using var db = factory.CreateMigratorDbContext();
        var chunks = await db.BotKnowledgeChunks.IgnoreQueryFilters().Where(x => x.SourceId == eicar.Id).ToListAsync();
        chunks.Should().BeEmpty();
        var stored = await db.BotKnowledgeSourceVersions.IgnoreQueryFilters().Where(x => x.SourceId == eicar.Id).ToListAsync();
        stored.Should().BeEmpty();
    }

    [Fact]
    public async Task Url_snapshot_is_ready_and_a_private_redirect_is_blocked()
    {
        using var host = Host(true, "docs.example", new ScriptedDirectory());
        using var admin = Client(host, "demo");
        var ready = await CreateAsync(admin, new
        {
            name = "Site",
            classification = "internal",
            kind = "url",
            url = "https://docs.example/policy"
        }, "knowledge-url-ready");
        ready.Status.Should().Be("Ready");
        ready.ChunkCount.Should().BeGreaterThan(0);

        var blocked = await CreateAsync(admin, new
        {
            name = "Hop",
            classification = "internal",
            kind = "url",
            url = "https://docs.example/hop"
        }, "knowledge-url-hop");
        blocked.Status.Should().Be("Failed");
        blocked.LastError.Should().Be(BotKnowledgeErrors.EgressBlocked);

        using var plain = Host(true);
        using var plainAdmin = Client(plain, "demo");
        var loopback = await CreateAsync(plainAdmin, new
        {
            name = "Loop",
            classification = "internal",
            kind = "url",
            url = "https://127.0.0.1/secret"
        }, "knowledge-url-loop");
        loopback.Status.Should().Be("Failed");
        loopback.LastError.Should().Be(BotKnowledgeErrors.EgressBlocked);
        (await plainAdmin.GetStringAsync($"{ListUrl()}/{loopback.Id}")).Should().NotContain("secret-body");
    }

    [Fact]
    public async Task Grant_retrieval_revoke_and_delete_drop_the_excerpt()
    {
        using var host = Host(true);
        using var admin = Client(host, "demo");
        var source = await CreateAsync(admin, FileBody("Citavel", Policy), "grant-source");
        var bot = await CreateBotAsync(admin, "grant-bot");

        var granted = await SendAsync(admin, HttpMethod.Post, $"{ListUrl()}/{source.Id}/grants", new { botId = bot }, "grant-add");
        granted.StatusCode.Should().Be(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        var grant = (await granted.Content.ReadFromJsonAsync<GrantDto>(JsonOptions))!;

        var found = await AskAsync(admin, bot, "ferias coletivas dezembro");
        found.Evidence.Should().Be(BotKnowledgeErrors.EvidenceCited);
        found.Citations.Should().Contain(x => x.SourceId == source.Id && x.Version == 1 && x.Excerpt.Contains("ferias", StringComparison.OrdinalIgnoreCase));

        var revoked = await SendAsync(admin, HttpMethod.Post, $"{ListUrl()}/{source.Id}/grants/{grant.Id}/revoke", new { }, "grant-revoke");
        revoked.StatusCode.Should().Be(HttpStatusCode.OK, await revoked.Content.ReadAsStringAsync());
        var hidden = await AskAsync(admin, bot, "ferias coletivas dezembro");
        hidden.Evidence.Should().Be(BotKnowledgeErrors.EvidenceInsufficient);
        hidden.Citations.Should().BeEmpty();

        var deleted = await SendAsync(admin, HttpMethod.Post, $"{ListUrl()}/{source.Id}/delete", new { }, "grant-delete");
        deleted.StatusCode.Should().Be(HttpStatusCode.OK, await deleted.Content.ReadAsStringAsync());
        var evidence = await deleted.Content.ReadFromJsonAsync<DeleteDto>(JsonOptions);
        evidence!.RemovedChunks.Should().BeGreaterThan(0);
        evidence.EvidenceChecksum.Should().NotBeNullOrEmpty();

        await using var db = factory.CreateMigratorDbContext();
        var chunks = await db.BotKnowledgeChunks.IgnoreQueryFilters().Where(x => x.SourceId == source.Id).ToListAsync();
        chunks.Should().OnlyContain(x => x.Text == string.Empty && x.DeletedAt != null);
    }

    [Fact]
    public async Task Qdrant_outage_keeps_the_source_ready()
    {
        using var host = Host(true, qdrantUrl: "http://127.0.0.1:9");
        using var admin = Client(host, "demo");
        var source = await CreateAsync(admin, FileBody("Sem vetor", Policy), "qdrant-down");
        source.Status.Should().Be("Ready");
        source.Jobs.Should().Contain(x => x.Error == BotKnowledgeErrors.QdrantDeferred);
    }

    private async Task<SourceDto> CreateAsync(HttpClient admin, object body, string key)
    {
        var response = await SendAsync(admin, HttpMethod.Post, ListUrl(), body, key);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SourceDto>(JsonOptions))!;
    }

    private async Task<AnswerDto> AskAsync(HttpClient admin, Guid botId, string query)
    {
        var response = await admin.PostAsJsonAsync($"{ListUrl()}/retrieve", new { botId, query });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AnswerDto>(JsonOptions))!;
    }

    private static async Task<Guid> CreateBotAsync(HttpClient admin, string key)
    {
        var response = await SendAsync(admin, HttpMethod.Post, $"/api/v1/admin/ai/bots/workspaces/{WorkspaceId}", new
        {
            name = "Conhecimento",
            description = "Bot de teste",
            allowDms = true,
            systemPrompt = "Seja breve.",
            provider = "mock",
            modelId = "mock-small",
            parameters = new { temperature = 0.2 },
            dataClass = "internal",
            channelIds = Array.Empty<Guid>(),
            skillVersionIds = Array.Empty<Guid>(),
            dailyBudgetUsd = 1
        }, key);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;
    }

    private async Task<Guid> SeedPageAsync(string body)
    {
        await using var db = factory.CreateMigratorDbContext();
        var page = new KnowledgePage
        {
            Id = Guid.NewGuid(),
            TenantId = SeedData.DemoTenantId,
            WorkspaceId = SeedData.DemoWorkspaceId,
            CollectionId = Guid.NewGuid(),
            Title = "Ferias",
            Slug = $"ferias-{Guid.NewGuid():N}"[..24],
            Body = body,
            Version = 1,
            CreatedBy = SeedData.DemoUserId,
            UpdatedBy = SeedData.DemoUserId,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.KnowledgePages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    private WebApplicationFactory<Program> Host(bool enabled, string? allowlist = null, IBotKnowledgeDirectory? directory = null, string? qdrantUrl = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Features:BotKnowledge:Enabled", enabled ? "true" : "false");
            builder.UseSetting("Features:AiBots:Enabled", "true");
            if (allowlist is not null)
            {
                builder.UseSetting("Features:BotKnowledge:UrlAllowlist", allowlist);
            }

            if (qdrantUrl is not null)
            {
                builder.UseSetting("SemanticSearch:QdrantUrl", qdrantUrl);
            }

            if (directory is not null)
            {
                builder.ConfigureTestServices(services => services.AddScoped<IBotKnowledgeDirectory>(_ => directory));
            }
        });

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

    private static string ListUrl() => $"/api/v1/admin/ai/knowledge/workspaces/{WorkspaceId}";

    private static object FileBody(string name, string text) => new
    {
        name,
        classification = "internal",
        kind = "file",
        text
    };

    private sealed class ScriptedDirectory : IBotKnowledgeDirectory
    {
        public Task<IReadOnlyList<IPAddress>> LookupAsync(string host, CancellationToken cancellationToken)
        {
            if (IPAddress.TryParse(host, out var literal))
            {
                return Task.FromResult<IReadOnlyList<IPAddress>>([literal]);
            }

            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
        }

        public Task<BotKnowledgeHttpResult> GetAsync(Uri url, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (url.AbsolutePath == "/hop")
            {
                return Task.FromResult(new BotKnowledgeHttpResult(302, "https://127.0.0.1/secret", [], string.Empty));
            }

            var body = Encoding.UTF8.GetBytes(Policy);
            return Task.FromResult(new BotKnowledgeHttpResult(200, null, body, "text/plain"));
        }
    }

    private sealed record SourceDto(Guid Id, string Status, int ChunkCount, string? Checksum, string? ContentSha256, string? LastError, JobDto[] Jobs);
    private sealed record JobDto(string? Error);
    private sealed record GrantDto(Guid Id);
    private sealed record DeleteDto(int RemovedChunks, string EvidenceChecksum);
    private sealed record AnswerDto(string Evidence, CitationDto[] Citations);
    private sealed record CitationDto(Guid SourceId, int Version, string Excerpt);
    private sealed record IdDto(Guid Id);
}
