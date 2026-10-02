using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-186 — roster paginado, add/remove/leave em canal privado.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChannelMembershipIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid DemoWorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid DemoChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Private_channel_roster_add_remove_leave_and_pagination()
    {
        using var alice = CreateClient("alice");
        using var bob = CreateClient("bob");
        using var demo = CreateClient("demo");

        var channelId = await CreatePrivateChannelAsync(alice);
        var beforeId = Guid.NewGuid();
        var sent = await alice.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages",
            new { messageId = beforeId, idempotencyKey = $"idem-{beforeId:N}", body = $"before-add-{beforeId:N}" });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var stranger = await bob.GetAsync(Roster(channelId));
        stranger.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var bobAdd = await bob.PostAsJsonAsync(Roster(channelId), new { userId = SeedData.DemoUserId.Value });
        bobAdd.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var alone = await alice.DeleteAsync($"{Roster(channelId)}/me");
        alone.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(alone)).Should().Be("LastChannelManager");

        var addBob = await alice.PostAsJsonAsync(Roster(channelId), new { userId = SeedData.BobUserId.Value });
        addBob.StatusCode.Should().Be(HttpStatusCode.OK);
        var duplicate = await alice.PostAsJsonAsync(Roster(channelId), new { userId = SeedData.BobUserId.Value });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(duplicate)).Should().Be("AlreadyChannelMember");

        var bobChannels = await bob.GetFromJsonAsync<ChannelDto[]>(
            $"/api/v1/workspaces/{DemoWorkspaceId}/channels",
            JsonOptions);
        bobChannels.Should().Contain(channel => channel.Id == channelId);
        var bobHistory = await bob.GetFromJsonAsync<HistoryDto>($"/api/v1/channels/{channelId}/messages", JsonOptions);
        bobHistory!.Messages.Should().Contain(message => message.Body.Contains($"before-add-{beforeId:N}", StringComparison.Ordinal));
        bobHistory.Messages.Should().Contain(message => message.Body.Contains($"<system:member-add:{SeedData.BobUserId.Value:D}>", StringComparison.Ordinal));

        var addDemo = await alice.PostAsJsonAsync(Roster(channelId), new { userId = SeedData.DemoUserId.Value });
        addDemo.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await alice.GetFromJsonAsync<PageDto>($"{Roster(channelId)}?limit=1", JsonOptions);
        page!.Total.Should().Be(3);
        page.CanManage.Should().BeTrue();
        page.Items.Should().HaveCount(1);
        page.NextCursor.Should().NotBeNullOrWhiteSpace();
        var seen = new HashSet<Guid>(page.Items.Select(item => item.UserId));
        var cursor = page.NextCursor;
        while (!string.IsNullOrEmpty(cursor))
        {
            var next = await alice.GetFromJsonAsync<PageDto>(
                $"{Roster(channelId)}?limit=1&cursor={Uri.EscapeDataString(cursor)}",
                JsonOptions);
            next!.Items.Should().HaveCount(1);
            seen.Add(next.Items[0].UserId).Should().BeTrue();
            cursor = next.NextCursor;
        }

        seen.Should().BeEquivalentTo([
            SeedData.AliceUserId.Value,
            SeedData.BobUserId.Value,
            SeedData.DemoUserId.Value
        ]);

        var bobLeaves = await bob.DeleteAsync($"{Roster(channelId)}/me");
        bobLeaves.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await bob.GetAsync($"/api/v1/channels/{channelId}/messages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var afterLeave = await bob.GetFromJsonAsync<ChannelDto[]>(
            $"/api/v1/workspaces/{DemoWorkspaceId}/channels",
            JsonOptions);
        afterLeave.Should().NotContain(channel => channel.Id == channelId);

        var readd = await alice.PostAsJsonAsync(Roster(channelId), new { userId = SeedData.BobUserId.Value });
        readd.StatusCode.Should().Be(HttpStatusCode.OK);
        var removed = await alice.DeleteAsync($"{Roster(channelId)}/{SeedData.BobUserId.Value}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await bob.GetAsync($"/api/v1/channels/{channelId}/messages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var removeDemo = await alice.DeleteAsync($"{Roster(channelId)}/{SeedData.DemoUserId.Value}");
        removeDemo.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var aliceLeaves = await alice.DeleteAsync($"{Roster(channelId)}/me");
        aliceLeaves.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = factory.CreateMigratorDbContext();
        var actions = await db.AuditEvents.AsNoTracking()
            .Where(x => x.TenantId == SeedData.DemoTenantId && x.EntityId == channelId.ToString())
            .Select(x => x.Action)
            .ToListAsync();
        actions.Should().Contain(AuditActions.ChannelMemberAdd);
        actions.Should().Contain(AuditActions.ChannelMemberRemove);
        actions.Should().Contain(AuditActions.ChannelMemberLeave);
    }

    [Fact]
    public async Task Public_roster_lists_workspace_and_rejects_individual_membership()
    {
        using var alice = CreateClient("alice");
        var page = await alice.GetFromJsonAsync<PageDto>(
            $"/api/v1/workspaces/{DemoWorkspaceId}/channels/{DemoChannelId}/members?limit=50",
            JsonOptions);
        page!.CanManage.Should().BeFalse();
        page.Total.Should().BeGreaterThanOrEqualTo(3);
        page.Items.Select(item => item.UserId).Should().Contain(SeedData.BobUserId.Value);
        page.Items.Should().OnlyContain(item => item.JoinedAt != null);

        var autocomplete = await alice.GetFromJsonAsync<MemberDto[]>(
            $"/api/v1/workspaces/{DemoWorkspaceId}/channels/{DemoChannelId}/members?query=bo",
            JsonOptions);
        autocomplete.Should().NotBeNull();
        autocomplete!.Length.Should().BeLessThanOrEqualTo(8);
        autocomplete.Select(item => item.UserId).Should().Contain(SeedData.BobUserId.Value);

        var add = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{DemoWorkspaceId}/channels/{DemoChannelId}/members",
            new { userId = SeedData.BobUserId.Value });
        add.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(add)).Should().Be("ChannelMembershipNotPrivate");
    }

    private async Task<Guid> CreatePrivateChannelAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{DemoWorkspaceId}/channels",
            new { name = $"priv-{Guid.NewGuid():N}"[..28], type = "Private" });
        create.EnsureSuccessStatusCode();
        var channel = await create.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions);
        return channel!.Id;
    }

    private static string Roster(Guid channelId) =>
        $"/api/v1/workspaces/{DemoWorkspaceId}/channels/{channelId}/members";

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ErrorDto>(JsonOptions);
        return body?.Error ?? string.Empty;
    }

    private HttpClient CreateClient(string devUser)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", devUser);
        return client;
    }

    private sealed record ChannelDto(Guid Id, string Name, string Type);
    private sealed record MemberDto(Guid UserId, string DisplayName, string Email, DateTimeOffset? JoinedAt);
    private sealed record PageDto(MemberDto[] Items, string? NextCursor, int Total, bool CanManage);
    private sealed record HistoryDto(HistoryMessageDto[] Messages);
    private sealed record HistoryMessageDto(string Body);
    private sealed record ErrorDto(string Error);
}
