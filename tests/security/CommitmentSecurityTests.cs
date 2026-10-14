using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class CommitmentSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Restricted_item_is_hidden_from_other_channel_members_and_other_tenants()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var body = $"restricted-{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid();
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{SeedData.DemoChannelId.Value}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null));
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/commitments",
            new CreateCommitmentRequest(
                $"sec-{Guid.NewGuid():N}",
                messageId,
                "decision",
                "Só a Alice",
                null,
                SeedData.AliceUserId.Value,
                null,
                "restricted"));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = await created.Content.ReadFromJsonAsync<CommitmentDto>(JsonOptions);

        var bobList = await bob.GetFromJsonAsync<PageDto>(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/commitments?state=all", JsonOptions);
        bobList!.Items.Should().NotContain(row => row.Id == item!.Id);
        var bobGet = await bob.GetAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/commitments/{item!.Id}");
        bobGet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = factory.CreateMigratorDbContext();
        var foreignWorkspace = await db.Workspaces.IgnoreQueryFilters()
            .Where(workspace => workspace.TenantId != SeedData.DemoTenantId)
            .Select(workspace => workspace.Id.Value)
            .FirstOrDefaultAsync();
        if (foreignWorkspace != Guid.Empty)
        {
            var foreign = await alice.GetAsync($"/api/v1/workspaces/{foreignWorkspace}/commitments");
            foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record CommitmentDto(Guid Id, string Title);

    private sealed record PageDto(CommitmentDto[] Items);
}
