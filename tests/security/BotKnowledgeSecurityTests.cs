using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VibeChat.AI;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class BotKnowledgeSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid DemoWorkspace = SeedData.DemoWorkspaceId.Value;
    private const string Secret = "segredo-de-outro-tenant-ferias";

    [Fact]
    public async Task Member_cannot_manage_or_read_the_source_text()
    {
        using var host = Host();
        using var member = Client(host, "alice");
        var denied = await SendAsync(member, HttpMethod.Post, ListUrl(DemoWorkspace), FileBody("Membro", Secret), "knowledge-member");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain(Secret);

        using var admin = Client(host, "demo");
        var created = await SendAsync(admin, HttpMethod.Post, ListUrl(DemoWorkspace), FileBody("Admin", "texto interno sem segredo"), "knowledge-admin");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var source = (await created.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;
        var hidden = await member.GetAsync($"{ListUrl(DemoWorkspace)}/{source.Id}");
        hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await hidden.Content.ReadAsStringAsync()).Should().NotContain("texto interno");
    }

    [Fact]
    public async Task Another_tenant_does_not_reveal_the_source_or_a_foreign_page()
    {
        using var host = Host();
        using var admin = Client(host, "demo");
        await admin.GetAsync(ListUrl(DemoWorkspace));
        var foreign = await SeedForeignWorkspaceAsync();
        var created = await SendAsync(admin, HttpMethod.Post, ListUrl(foreign), FileBody("Estrangeira", Secret), "knowledge-foreign");
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var source = (await created.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!;

        var hidden = await admin.GetAsync($"{ListUrl(DemoWorkspace)}/{source.Id}");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().NotContain(Secret);

        var pageId = await SeedPageAsync(foreign, Secret + " pagina");
        var rejected = await SendAsync(admin, HttpMethod.Post, ListUrl(DemoWorkspace), new
        {
            name = "Pagina alheia",
            classification = "internal",
            kind = "page",
            pageId
        }, "knowledge-foreign-page");
        rejected.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await rejected.Content.ReadAsStringAsync();
        body.Should().Contain(BotKnowledgeErrors.PageNotFound).And.NotContain(Secret);

        await using var db = factory.CreateMigratorDbContext();
        var leaked = await db.BotKnowledgeChunks.IgnoreQueryFilters().Where(x => x.Text.Contains(Secret)).ToListAsync();
        leaked.Should().OnlyContain(x => x.TenantId == new TenantId(foreign));
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
            Name = "Foreign knowledge",
            Slug = $"foreign-knowledge-{workspaceId.Value:N}",
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

    private async Task<Guid> SeedPageAsync(Guid workspaceId, string body)
    {
        await using var db = factory.CreateMigratorDbContext();
        var page = new KnowledgePage
        {
            Id = Guid.NewGuid(),
            TenantId = new TenantId(workspaceId),
            WorkspaceId = new WorkspaceId(workspaceId),
            CollectionId = Guid.NewGuid(),
            Title = "Alheia",
            Slug = $"alheia-{Guid.NewGuid():N}"[..20],
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

    private WebApplicationFactory<Program> Host() =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Features:BotKnowledge:Enabled", "true");
            builder.UseSetting("Features:AiBots:Enabled", "true");
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

    private static string ListUrl(Guid workspaceId) => $"/api/v1/admin/ai/knowledge/workspaces/{workspaceId}";

    private static object FileBody(string name, string text) => new
    {
        name,
        classification = "confidential",
        kind = "file",
        text
    };

    private sealed record IdDto(Guid Id);
}
