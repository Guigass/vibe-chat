using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class KnowledgeSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Another_tenant_cannot_read_or_list_pages()
    {
        using var alice = Client("alice");
        var title = $"cross-{Guid.NewGuid():N}"[..20];
        var created = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/knowledge/collections",
            new CreateKnowledgeCollectionRequest($"sec-{Guid.NewGuid():N}", title, "workspace", null, null));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var collection = (await created.Content.ReadFromJsonAsync<CollectionDto>(JsonOptions))!;
        var page = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/knowledge/pages",
            new CreateKnowledgePageRequest($"p-{Guid.NewGuid():N}", collection.Id, "Privada", "nao vaza"));
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = (await page.Content.ReadFromJsonAsync<PageDto>(JsonOptions))!;

        await using var db = factory.CreateMigratorDbContext();
        var foreignId = WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = foreignId,
            TenantId = new TenantId(foreignId.Value),
            Name = "Outro",
            Slug = $"other-{foreignId.Value:N}",
            AiEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var foreign = foreignId.Value;

        var list = await alice.GetAsync($"/api/v1/workspaces/{foreign}/knowledge/collections");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var read = await alice.GetAsync($"/api/v1/workspaces/{foreign}/knowledge/pages/{item.Id}");
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await read.Content.ReadAsStringAsync()).Should().NotContain("nao vaza");
    }

    [Fact]
    public async Task Member_cannot_delete_or_bulk_export()
    {
        using var alice = Client("alice");
        var created = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/knowledge/collections",
            new CreateKnowledgeCollectionRequest($"m-{Guid.NewGuid():N}", "Membros", "workspace", null, null));
        created.EnsureSuccessStatusCode();
        var collection = (await created.Content.ReadFromJsonAsync<CollectionDto>(JsonOptions))!;
        var pageResponse = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/knowledge/pages",
            new CreateKnowledgePageRequest($"mp-{Guid.NewGuid():N}", collection.Id, "Nota", "texto"));
        pageResponse.EnsureSuccessStatusCode();
        var page = (await pageResponse.Content.ReadFromJsonAsync<PageDto>(JsonOptions))!;

        (await alice.DeleteAsync($"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/knowledge/pages/{page.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync($"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/knowledge/export"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record CollectionDto(Guid Id);

    private sealed record PageDto(Guid Id);
}
