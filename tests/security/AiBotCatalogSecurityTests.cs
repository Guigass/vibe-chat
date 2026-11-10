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
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class AiBotCatalogSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private const string Secret = "sk-live-do-not-store";

    [Fact]
    public async Task Member_cannot_manage_and_provider_key_never_returns()
    {
        using var host = BotHost();
        using var member = Client(host, "alice");
        var denied = await SendAsync(member, HttpMethod.Post, ListUrl(WorkspaceId), Draft("Nope", "segredo"), "member-denied");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("segredo");

        using var admin = Client(host, "demo");
        var leaked = await SendAsync(admin, HttpMethod.Post, ListUrl(WorkspaceId), Draft("Chave", "prompt", apiKey: Secret), "reject-key");
        leaked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await leaked.Content.ReadAsStringAsync()).Should().Contain(AiBotErrors.ProviderKeyRejected).And.NotContain(Secret);

        var created = await SendAsync(admin, HttpMethod.Post, ListUrl(WorkspaceId), Draft("Seguro", "prompt limpo"), "create-safe");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var bot = (await created.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;
        var body = await admin.GetStringAsync($"{ListUrl(WorkspaceId)}/{bot.Id}");
        body.Should().NotContain(Secret);
        var diff = await admin.GetStringAsync($"{ListUrl(WorkspaceId)}/{bot.Id}/diff?fromVersion=0&toVersion=0");
        diff.Should().NotContain(Secret);

        await using var db = factory.CreateMigratorDbContext();
        var stored = await db.AiModelBindings.IgnoreQueryFilters().Where(x => x.BotId == bot.Id).ToListAsync();
        stored.Should().NotBeEmpty();
        stored.Should().OnlyContain(x => !x.ParametersJson.Contains(Secret));
        var audits = await db.AuditEvents.IgnoreQueryFilters().Where(x => x.EntityId == bot.Id.ToString()).ToListAsync();
        audits.Should().NotBeEmpty();
        audits.Should().OnlyContain(x => !x.MetadataJson.Contains(Secret) && !x.MetadataJson.Contains("prompt limpo"));
    }

    [Fact]
    public async Task Another_tenant_cannot_read_the_bot_prompt()
    {
        using var host = BotHost();
        var foreign = await SeedForeignWorkspaceAsync();
        using var admin = Client(host, "demo");
        const string prompt = "segredo-do-outro-tenant";
        var created = await SendAsync(admin, HttpMethod.Post, ListUrl(foreign), Draft("Estrangeiro", prompt), "foreign-bot");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var bot = (await created.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;

        var hidden = await admin.GetAsync($"{ListUrl(WorkspaceId)}/{bot.Id}");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().NotContain(prompt);

        using var alice = Client(host, "alice");
        var foreignList = await alice.GetAsync(ListUrl(foreign));
        foreignList.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreignList.Content.ReadAsStringAsync()).Should().NotContain(prompt);
    }

    private WebApplicationFactory<Program> BotHost() =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("Features:AiBots:Enabled", "true"));

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

    private static string ListUrl(Guid workspaceId) => $"/api/v1/admin/ai/bots/workspaces/{workspaceId}";

    private static object Draft(string name, string prompt, string? apiKey = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["description"] = "segurança",
            ["allowDms"] = true,
            ["systemPrompt"] = prompt,
            ["provider"] = "mock",
            ["modelId"] = "mock-small",
            ["parameters"] = new { temperature = 0.1 },
            ["dataClass"] = "confidential",
            ["channelIds"] = Array.Empty<Guid>(),
            ["dailyBudgetUsd"] = 0
        };
        if (apiKey is not null)
        {
            body["apiKey"] = apiKey;
        }

        return body;
    }

    private async Task<Guid> SeedForeignWorkspaceAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = VibeChat.SharedKernel.WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = new TenantId(workspaceId.Value),
            Name = "Foreign AI bots",
            Slug = $"foreign-ai-{workspaceId.Value:N}",
            AiEnabled = false,
            CreatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = new TenantId(workspaceId.Value),
            WorkspaceId = workspaceId,
            UserId = SeedData.DemoUserId,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });
        await db.SaveChangesAsync();
        return workspaceId.Value;
    }

    private sealed record IdDto(Guid Id);
}
