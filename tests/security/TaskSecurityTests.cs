using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class TaskSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Assignee_without_channel_access_is_rejected_and_other_tenants_are_forbidden()
    {
        using var alice = Client("alice");
        var messageId = await SendAsync(alice, SeedData.DemoChannelId.Value, $"task-sec-{Guid.NewGuid():N}");
        var rejected = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/tasks",
            new CreateTaskRequest(
                $"bad-{Guid.NewGuid():N}",
                messageId,
                "Fora",
                null,
                "normal",
                [Guid.NewGuid()],
                null,
                "channel",
                []));
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = factory.CreateMigratorDbContext();
        var foreignWorkspace = await db.Workspaces.IgnoreQueryFilters()
            .Where(workspace => workspace.TenantId != SeedData.DemoTenantId)
            .Select(workspace => workspace.Id.Value)
            .FirstOrDefaultAsync();
        if (foreignWorkspace != Guid.Empty)
        {
            var foreign = await alice.GetAsync($"/api/v1/workspaces/{foreignWorkspace}/tasks");
            foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Leaving_a_private_channel_unassigns_and_hides_the_task()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var createdChannel = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels",
            new CreateChannelRequest($"tasks-{Guid.NewGuid():N}"[..18], "Private", SeedData.DemoSpaceGeralId));
        createdChannel.StatusCode.Should().Be(HttpStatusCode.Created);
        var channel = (await createdChannel.Content.ReadFromJsonAsync<ChannelDto>(JsonOptions))!;

        var added = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{channel.Id}/members",
            new AddChannelMemberRequest(SeedData.BobUserId.Value));
        added.StatusCode.Should().Be(HttpStatusCode.OK);

        var messageId = await SendAsync(alice, channel.Id, $"private-{Guid.NewGuid():N}");
        var created = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/tasks",
            new CreateTaskRequest(
                $"leave-{Guid.NewGuid():N}",
                messageId,
                "Só o canal",
                null,
                "normal",
                [SeedData.BobUserId.Value],
                null,
                "channel",
                []));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var task = (await created.Content.ReadFromJsonAsync<TaskDto>(JsonOptions))!;

        var visible = await bob.GetAsync($"/api/v1/workspaces/{WorkspaceId}/tasks/{task.Id}");
        visible.StatusCode.Should().Be(HttpStatusCode.OK);

        var removed = await alice.DeleteAsync(
            $"/api/v1/workspaces/{WorkspaceId}/channels/{channel.Id}/members/{SeedData.BobUserId.Value}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var hidden = await bob.GetAsync($"/api/v1/workspaces/{WorkspaceId}/tasks/{task.Id}");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = await bob.GetFromJsonAsync<PageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/tasks?view=workspace&state=all", JsonOptions);
        list!.Items.Should().NotContain(item => item.Id == task.Id);

        await using var db = factory.CreateMigratorDbContext();
        var stillAssigned = await db.MessageTaskAssignees.IgnoreQueryFilters()
            .AnyAsync(row => row.TaskId == task.Id && row.UserId == SeedData.BobUserId);
        stillAssigned.Should().BeFalse();
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static async Task<Guid> SendAsync(HttpClient client, Guid channelId, string body)
    {
        var messageId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return messageId;
    }

    private sealed record ChannelDto(Guid Id);

    private sealed record TaskDto(Guid Id, string Title);

    private sealed record PageDto(TaskDto[] Items);
}
