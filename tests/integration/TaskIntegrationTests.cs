using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Messaging;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class TaskIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Create_keeps_origin_is_idempotent_and_preserves_checklist_order()
    {
        using var alice = Client("alice");
        var body = $"task-body-{Guid.NewGuid():N}";
        var messageId = await SendAsync(alice, body);
        var key = $"task-{Guid.NewGuid():N}";
        var created = await CreateAsync(alice, key, messageId, "Cobrar o relatório", ["segundo", "primeiro"]);
        var replay = await CreateAsync(alice, key, messageId, "Outro título", ["x"]);

        replay.Id.Should().Be(created.Id);
        replay.Title.Should().Be("Cobrar o relatório");
        created.SourceMessageId.Should().Be(messageId);
        created.SourceSequence.Should().BeGreaterThan(0);
        created.Checklist.Select(item => item.Text).Should().Equal("segundo", "primeiro");
        created.Version.Should().Be(1);
        JsonSerializer.Serialize(created).Should().NotContain(body);

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.MessageTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
        row.Title.Should().NotContain(body);
        (row.Description ?? string.Empty).Should().NotContain(body);
        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == TaskPolicies.Created && x.Payload.Contains(created.Id.ToString()))
            .ToListAsync();
        outbox.Should().ContainSingle();
        outbox[0].Payload.Should().NotContain(body);
    }

    [Fact]
    public async Task Stale_version_does_not_overwrite_and_complete_is_idempotent()
    {
        using var alice = Client("alice");
        var messageId = await SendAsync(alice, $"task-ver-{Guid.NewGuid():N}");
        var created = await CreateAsync(alice, $"ver-{Guid.NewGuid():N}", messageId, "Versão", []);

        var first = await alice.PatchAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/tasks/{created.Id}",
            new PatchTaskRequest(1, "Primeira", null, null, null, null, false, null, false, null, null, false));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await first.Content.ReadFromJsonAsync<TaskDto>(JsonOptions))!;
        updated.Version.Should().Be(2);
        updated.Title.Should().Be("Primeira");

        var stale = await alice.PatchAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/tasks/{created.Id}",
            new PatchTaskRequest(1, "Perdida", null, null, null, null, false, null, false, null, null, false));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var current = await alice.GetFromJsonAsync<TaskDto>(
            $"/api/v1/workspaces/{WorkspaceId}/tasks/{created.Id}", JsonOptions);
        current!.Title.Should().Be("Primeira");
        current.Version.Should().Be(2);

        var completed = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/tasks/{created.Id}/complete",
            new TaskVersionRequest(2));
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/tasks/{created.Id}/complete",
            new TaskVersionRequest(2));
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var done = (await again.Content.ReadFromJsonAsync<TaskDto>(JsonOptions))!;
        done.State.Should().Be("completed");
        done.Version.Should().Be(3);

        await using var db = factory.CreateMigratorDbContext();
        var events = await db.OutboxMessages.IgnoreQueryFilters()
            .CountAsync(x => x.Type == TaskPolicies.Completed && x.Payload.Contains(created.Id.ToString()));
        events.Should().Be(1);
    }

    [Fact]
    public async Task Assignee_inbox_follows_preferences_and_overdue_event_is_emitted_once()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        await bob.PutAsJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest(
            "All", false, false, null, null, 0, "Etc/UTC", false, null));

        var messageId = await SendAsync(alice, $"task-inbox-{Guid.NewGuid():N}");
        var created = await CreateAsync(
            alice,
            $"inbox-{Guid.NewGuid():N}",
            messageId,
            "Pagar o fornecedor",
            [],
            SeedData.BobUserId.Value,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        var inbox = await bob.GetFromJsonAsync<InboxPageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/inbox?type=task", JsonOptions);
        inbox!.Items.Should().Contain(item => item.ItemKey == $"task:{created.Id:D}" && item.Reason == "assigned");
        inbox.Items.Single(item => item.ItemKey == $"task:{created.Id:D}").BodyPreview.Should().Be("Pagar o fornecedor");

        await bob.PutAsJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest(
            "None", false, false, null, null, 0, "Etc/UTC", false, null));
        var muted = await bob.GetFromJsonAsync<InboxPageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/inbox?type=task", JsonOptions);
        muted!.Items.Should().NotContain(item => item.ItemKey == $"task:{created.Id:D}");
        await bob.PutAsJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest(
            "MentionsAndDms", false, false, null, null, 0, "Etc/UTC", false, null));

        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<TaskOverdueProcessor>();
        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.MessageTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
        row.OverdueNotifiedAt.Should().NotBeNull();
        var overdue = await db.OutboxMessages.IgnoreQueryFilters()
            .CountAsync(x => x.Type == TaskPolicies.Overdue && x.Payload.Contains(created.Id.ToString()));
        overdue.Should().Be(1);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static async Task<Guid> SendAsync(HttpClient client, string body)
    {
        var messageId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/channels/{ChannelId}/messages",
            new SendMessageRequest(messageId, $"idem-{messageId:N}", body, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return messageId;
    }

    private static async Task<TaskDto> CreateAsync(
        HttpClient client,
        string key,
        Guid messageId,
        string title,
        string[] checklist,
        Guid? assigneeId = null,
        DateTimeOffset? dueAt = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/tasks",
            new CreateTaskRequest(
                key,
                messageId,
                title,
                "nota curada",
                "high",
                assigneeId is Guid id ? [id] : [],
                dueAt,
                "channel",
                checklist.Select(text => new TaskChecklistInput(text, false)).ToArray()));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var dto = await response.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        dto.Should().NotBeNull();
        return dto!;
    }

    private sealed record TaskChecklistDto(Guid Id, int Position, string Text, bool Done);

    private sealed record TaskDto(
        Guid Id,
        string Title,
        Guid SourceMessageId,
        long SourceSequence,
        int Version,
        string State,
        TaskChecklistDto[] Checklist);

    private sealed record InboxItemDto(string ItemKey, string Kind, string Reason, string BodyPreview);

    private sealed record InboxPageDto(InboxItemDto[] Items);
}
