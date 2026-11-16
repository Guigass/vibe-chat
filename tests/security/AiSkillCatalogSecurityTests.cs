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
public sealed class AiSkillCatalogSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private const string Secret = "sk-live-do-not-store";

    [Fact]
    public async Task Member_cannot_manage_and_secret_never_returns()
    {
        using var host = SkillHost();
        using var member = Client(host, "alice");
        var slug = $"sec-{Guid.NewGuid():N}"[..16];
        var denied = await SendAsync(member, HttpMethod.Post, ListUrl(WorkspaceId), Draft(slug, "segredo do membro"), $"skill-member-{slug}");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("segredo do membro");

        using var admin = Client(host, "demo");
        var leaked = await SendAsync(admin, HttpMethod.Post, $"{ListUrl(WorkspaceId)}/import", new
        {
            schema = AiSkillCatalogPolicies.ManifestSchema,
            slug,
            version = 1,
            name = "Chave",
            description = "não",
            instructions = "Responda sem segredo.",
            apiKey = Secret
        }, $"skill-key-{slug}");
        leaked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await leaked.Content.ReadAsStringAsync()).Should().Contain(AiSkillErrors.SkillSecretRejected).And.NotContain(Secret);

        var created = await SendAsync(admin, HttpMethod.Post, ListUrl(WorkspaceId), Draft(slug, "Responda sem segredo."), $"skill-safe-{slug}");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var skill = (await created.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;
        var body = await admin.GetStringAsync($"{ListUrl(WorkspaceId)}/{skill.Id}");
        body.Should().NotContain(Secret);

        await using var db = factory.CreateMigratorDbContext();
        var stored = await db.AiSkillDefinitions.IgnoreQueryFilters().Where(x => x.Id == skill.Id).ToListAsync();
        stored.Should().OnlyContain(x => !x.Instructions.Contains(Secret) && !x.ArgumentsSchemaJson.Contains(Secret));
        var audits = await db.AuditEvents.IgnoreQueryFilters().Where(x => x.EntityId == skill.Id.ToString()).ToListAsync();
        audits.Should().NotBeEmpty();
        audits.Should().OnlyContain(x => !x.MetadataJson.Contains(Secret) && !x.MetadataJson.Contains("Responda sem segredo."));
    }

    [Fact]
    public async Task Another_tenant_cannot_read_the_skill()
    {
        using var host = SkillHost();
        var foreign = await SeedForeignWorkspaceAsync();
        using var admin = Client(host, "demo");
        var slug = $"foreign-{Guid.NewGuid():N}"[..18];
        const string instructions = "segredo-do-outro-tenant";
        var created = await SendAsync(admin, HttpMethod.Post, ListUrl(foreign), Draft(slug, instructions), $"skill-foreign-{slug}");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var skill = (await created.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;

        var hidden = await admin.GetAsync($"{ListUrl(WorkspaceId)}/{skill.Id}");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().NotContain(instructions).And.NotContain(slug);

        using var alice = Client(host, "alice");
        var foreignList = await alice.GetAsync(ListUrl(foreign));
        foreignList.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreignList.Content.ReadAsStringAsync()).Should().NotContain(instructions);
    }

    private WebApplicationFactory<Program> SkillHost() =>
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

    private static string ListUrl(Guid workspaceId) => $"/api/v1/admin/ai/skills/workspaces/{workspaceId}";

    private static object Draft(string slug, string instructions) => new
    {
        slug,
        name = "Segura",
        description = "segurança",
        instructions,
        argumentsSchema = new { type = "object" },
        examples = Array.Empty<object>(),
        compatibility = "bots.v1",
        classification = "confidential",
        knowledgeGrantIds = Array.Empty<Guid>(),
        mcpToolGrantIds = Array.Empty<Guid>()
    };

    private async Task<Guid> SeedForeignWorkspaceAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = VibeChat.SharedKernel.WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = new TenantId(workspaceId.Value),
            Name = "Foreign AI skills",
            Slug = $"foreign-skill-{workspaceId.Value:N}",
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
