using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class SemanticSearchSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Query_does_not_return_another_tenants_chunk()
    {
        using var demo = Client("demo");
        (await demo.PutAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/semantic/settings",
            new SettingsBody(true))).StatusCode.Should().Be(HttpStatusCode.OK);

        var foreign = WorkspaceId.New();
        var secret = $"vazaria-cross-{Guid.NewGuid():N}";
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.SemanticChunks.Add(new SemanticChunk
            {
                Id = Guid.NewGuid(),
                TenantId = new TenantId(foreign.Value),
                WorkspaceId = foreign,
                SourceKind = SemanticSourceKind.Message,
                SourceId = Guid.NewGuid(),
                ChunkIndex = 0,
                Text = secret,
                ContentHash = SemanticEmbedding.Hash(secret),
                Embedding = SemanticEmbedding.ToBase64(SemanticEmbedding.Embed(secret)),
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var alice = Client("alice");
        var response = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/semantic/query",
            new QueryBody(secret));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(secret);
        body.Should().NotContain(foreign.Value.ToString());
    }

    [Fact]
    public async Task Member_cannot_enable_semantic_search()
    {
        using var alice = Client("alice");
        var response = await alice.PutAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/semantic/settings",
            new SettingsBody(true));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record SettingsBody(bool Enabled);
    private sealed record QueryBody(string Q);
}
