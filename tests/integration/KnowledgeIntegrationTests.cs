using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class KnowledgeIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Conflict_does_not_overwrite_and_restore_appends_a_version()
    {
        using var alice = Client("alice");
        using var demo = Client("demo");
        var collection = await CreateCollectionAsync(alice, $"runbook-{Guid.NewGuid():N}"[..24]);
        var page = await CreatePageAsync(alice, collection.Id, "Como publicar", "passo 1");

        var stale = await alice.PatchAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}",
            new UpdateKnowledgePageRequest(page.Version, "Como publicar", "passo 2"));
        stale.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await stale.Content.ReadFromJsonAsync<PageDto>(JsonOptions))!;
        updated.Version.Should().Be(2);
        updated.Body.Should().Be("passo 2");

        var lost = await alice.PatchAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}",
            new UpdateKnowledgePageRequest(1, "Como publicar", "sobrescreveu"));
        lost.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await lost.Content.ReadAsStringAsync()).Should().Contain("KnowledgeVersionConflict");

        var current = await alice.GetFromJsonAsync<PageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}", JsonOptions);
        current!.Body.Should().Be("passo 2");
        current.Version.Should().Be(2);

        var restored = await demo.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}/restore",
            new RestoreKnowledgePageRequest(2, 1));
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        var after = (await restored.Content.ReadFromJsonAsync<PageDto>(JsonOptions))!;
        after.Body.Should().Be("passo 1");
        after.Version.Should().Be(3);

        var history = await alice.GetFromJsonAsync<HistoryDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}/history", JsonOptions);
        history!.Items.Select(item => item.Version).Should().Equal(3, 2, 1);
        history.Items.Should().Contain(item => item.Version == 1 && item.Body == "passo 1");

        var exported = await alice.GetFromJsonAsync<ExportDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}/export", JsonOptions);
        exported!.Versions.Should().HaveCount(3);
        exported.Page.Body.Should().Be("passo 1");
    }

    [Fact]
    public async Task Concurrent_updates_conflict_instead_of_failing_the_request()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var collection = await CreateCollectionAsync(alice, $"race-{Guid.NewGuid():N}"[..20]);
        var page = await CreatePageAsync(alice, collection.Id, "Corrida", "base");
        var url = $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}";
        var bodies = new[] { "alice-0", "bob-1", "alice-2", "bob-3" };

        var results = await Task.WhenAll(bodies.Select((body, index) =>
            (index % 2 == 0 ? alice : bob).PatchAsJsonAsync(
                url,
                new UpdateKnowledgePageRequest(page.Version, "Corrida", body))));

        var unexpected = new List<string>();
        foreach (var response in results)
        {
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict)
            {
                continue;
            }

            unexpected.Add($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        unexpected.Should().BeEmpty();
        results.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        foreach (var lost in results.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            (await lost.Content.ReadAsStringAsync()).Should().Contain("KnowledgeVersionConflict");
        }

        var current = await alice.GetFromJsonAsync<PageDto>(url, JsonOptions);
        current!.Version.Should().Be(page.Version + 1);
        bodies.Should().Contain(current.Body);

        var history = await alice.GetFromJsonAsync<HistoryDto>($"{url}/history", JsonOptions);
        history!.Items.Select(item => item.Version).Should().OnlyHaveUniqueItems();
        history.Items.Max(item => item.Version).Should().Be(page.Version + 1);
    }

    [Fact]
    public async Task Search_hides_channel_pages_and_revoked_references_are_redacted()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var token = $"kbsecret{Guid.NewGuid():N}";
        var channelId = await CreatePrivateChannelAsync(alice);
        var collection = await CreateCollectionAsync(alice, "Segredo", "channel", channelId);
        var page = await CreatePageAsync(alice, collection.Id, "Interno", token);

        var aliceSearch = await alice.GetFromJsonAsync<SearchDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/search?q={token}", JsonOptions);
        aliceSearch!.Items.Should().Contain(item => item.Id == page.Id);

        var bobSearch = await bob.GetFromJsonAsync<SearchDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/search?q={token}", JsonOptions);
        bobSearch!.Items.Should().NotContain(item => item.Id == page.Id);
        (await bob.GetAsync($"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var messageId = Guid.NewGuid();
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", $"origem-{Guid.NewGuid():N}", null, null));
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var decision = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/commitments",
            new CreateCommitmentRequest(
                $"k-{Guid.NewGuid():N}", messageId, "decision", "Só Alice", null, SeedData.AliceUserId.Value, null, "restricted"));
        decision.StatusCode.Should().Be(HttpStatusCode.OK);
        var commitment = (await decision.Content.ReadFromJsonAsync<CommitmentDto>(JsonOptions))!;

        var linked = await CreatePageAsync(
            alice,
            (await CreateCollectionAsync(alice, $"links-{Guid.NewGuid():N}"[..20])).Id,
            "Links",
            $"ver [[message:{messageId}]] e [[decision:{commitment.Id}]] e [[task:{Guid.NewGuid()}]]");
        var bobPage = await bob.GetFromJsonAsync<PageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{linked.Id}", JsonOptions);
        bobPage!.References.Should().Contain(item => item.Kind == "message" && item.State == "visible" && item.Label == null);
        bobPage.References.Should().Contain(item => item.Kind == "decision" && item.State == "redacted" && item.Label == null);
        bobPage.References.Should().Contain(item => item.Kind == "task" && item.State == "redacted");
        JsonSerializer.Serialize(bobPage).Should().NotContain("Só Alice");

        var deleted = await alice.DeleteAsync($"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages/{messageId}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterDelete = await alice.GetFromJsonAsync<PageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{linked.Id}", JsonOptions);
        afterDelete!.References.Should().Contain(item => item.Kind == "message" && item.State == "redacted");
    }

    [Fact]
    public async Task Disabled_flag_does_not_write()
    {
        using var disabled = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Knowledge:Pages:Enabled", "false"));
        using var alice = disabled.CreateClient();
        alice.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var response = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/collections",
            new CreateKnowledgeCollectionRequest($"off-{Guid.NewGuid():N}", "Nada", "workspace", null, null));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("KnowledgeDisabled");
    }

    [Fact]
    public async Task Retention_purges_deleted_pages_and_versions_together()
    {
        using var demo = Client("demo");
        var collection = await CreateCollectionAsync(demo, $"purge-{Guid.NewGuid():N}"[..20]);
        var page = await CreatePageAsync(demo, collection.Id, "Velha", "texto que sai");
        var deleted = await demo.DeleteAsync($"/api/v1/workspaces/{WorkspaceId}/knowledge/pages/{page.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = factory.CreateMigratorDbContext();
        var removed = await KnowledgeRetention.PurgeDeletedAsync(
            db, SeedData.DemoTenantId, DateTimeOffset.UtcNow.AddMinutes(1), 50, DateTimeOffset.UtcNow, CancellationToken.None);
        removed.Should().BeGreaterThan(0);
        await db.SaveChangesAsync();
        (await db.KnowledgePages.IgnoreQueryFilters().AnyAsync(row => row.Id == page.Id)).Should().BeFalse();
        (await db.KnowledgePageVersions.IgnoreQueryFilters().AnyAsync(row => row.PageId == page.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Unsafe_body_is_rejected()
    {
        using var alice = Client("alice");
        var collection = await CreateCollectionAsync(alice, $"safe-{Guid.NewGuid():N}"[..18]);
        var response = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages",
            new CreateKnowledgePageRequest($"bad-{Guid.NewGuid():N}", collection.Id, "Html", "<script>alert(1)</script>"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("UnsafeKnowledgeBody");
    }

    private async Task<Guid> CreatePrivateChannelAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name = $"priv-{Guid.NewGuid():N}"[..18], type = "Private" });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var channel = (await response.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions))!;
        return channel.Id;
    }

    private static async Task<CollectionDto> CreateCollectionAsync(
        HttpClient client,
        string title,
        string scope = "workspace",
        Guid? scopeId = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/collections",
            new CreateKnowledgeCollectionRequest($"col-{Guid.NewGuid():N}", title, scope, scopeId, null));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CollectionDto>(JsonOptions))!;
    }

    private static async Task<PageDto> CreatePageAsync(HttpClient client, Guid collectionId, string title, string body)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/knowledge/pages",
            new CreateKnowledgePageRequest($"page-{Guid.NewGuid():N}", collectionId, title, body));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PageDto>(JsonOptions))!;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record CollectionDto(Guid Id, string Title, string Scope);

    private sealed record ReferenceDto(string Kind, string State, string? Label);

    private sealed record PageDto(Guid Id, string Title, string Body, int Version, ReferenceDto[] References);

    private sealed record VersionDto(int Version, string Body);

    private sealed record HistoryDto(VersionDto[] Items);

    private sealed record ExportDto(PageDto Page, VersionDto[] Versions);

    private sealed record SearchItemDto(Guid Id, string Title);

    private sealed record SearchDto(SearchItemDto[] Items);

    private sealed record ChannelDto(Guid Id);

    private sealed record CommitmentDto(Guid Id);
}
