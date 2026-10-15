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
public sealed class DigestSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Revoked_membership_and_other_users_cannot_see_the_source()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var secret = $"revogado-{Guid.NewGuid():N}";
        var name = $"sec-{Guid.NewGuid():N}"[..18];
        var created = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new { name, type = "Private" });
        created.EnsureSuccessStatusCode();
        var channelId = (await created.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;
        (await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{channelId}/members",
            new { userId = SeedData.BobUserId.Value })).StatusCode.Should().Be(HttpStatusCode.OK);
        var messageId = Guid.NewGuid();
        (await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages",
            new { messageId, idempotencyKey = $"idem-{messageId:N}", body = secret }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var idempotency = $"sec-{Guid.NewGuid():N}";
        var digest = await CatchUpAsync(bob, idempotency);
        var raw = JsonSerializer.Serialize(digest);
        raw.Should().Contain(messageId.ToString());
        raw.Should().NotContain(secret);

        (await alice.DeleteAsync($"/api/v1/workspaces/{WorkspaceId}/channels/{channelId}/members/{SeedData.BobUserId.Value}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reread = await bob.GetAsync($"/api/v1/workspaces/{WorkspaceId}/digests/{digest.Id}");
        reread.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await reread.Content.ReadAsStringAsync();
        after.Should().NotContain(messageId.ToString());
        after.Should().NotContain(secret);

        var stolen = await alice.GetAsync($"/api/v1/workspaces/{WorkspaceId}/digests/{digest.Id}");
        stolen.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = factory.CreateMigratorDbContext();
        var foreignId = VibeChat.SharedKernel.WorkspaceId.New();
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
        var cross = await bob.GetAsync($"/api/v1/workspaces/{foreignId.Value}/digests/{digest.Id}");
        cross.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cross.Content.ReadAsStringAsync()).Should().NotContain(secret);
    }

    private async Task<DigestDto> CatchUpAsync(HttpClient client, string idempotency)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{WorkspaceId}/digests/catch-up");
        request.Headers.Add("Idempotency-Key", idempotency);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DigestDto>(JsonOptions))!;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record IdDto(Guid Id);

    private sealed record DigestDto(Guid Id, string AiStatus, JsonElement Sections);
}
