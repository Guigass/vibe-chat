using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class CommitmentIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Create_is_idempotent_and_keeps_only_a_curated_reference()
    {
        using var alice = Client("alice");
        var body = $"commitment-body-{Guid.NewGuid():N}";
        var messageId = await SendAsync(alice, body);
        var key = $"commit-{Guid.NewGuid():N}";
        var created = await CreateAsync(alice, key, messageId, "decision", "Registrar a decisão", ownerId: SeedData.BobUserId.Value);
        var replay = await CreateAsync(alice, key, messageId, "decision", "Outro título", ownerId: null);

        replay.Id.Should().Be(created.Id);
        replay.Title.Should().Be("Registrar a decisão");
        created.ExcerptHash.Should().HaveLength(64);
        created.SourceMessageId.Should().Be(messageId);
        created.SourceSequence.Should().BeGreaterThan(0);
        JsonSerializer.Serialize(created).Should().NotContain(body);

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.Commitments.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
        row.Title.Should().NotContain(body);
        (row.Description ?? string.Empty).Should().NotContain(body);
        row.AuthzSnapshot.Should().NotContain(body);
        row.ExcerptHash.Should().NotBe(body);
        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(x => x.Type == "decision.created")
            .OrderByDescending(x => x.OccurredAt)
            .Take(10)
            .ToListAsync();
        outbox.Should().Contain(x => x.Payload.Contains(created.Id.ToString(), StringComparison.OrdinalIgnoreCase)
            && !x.Payload.Contains(body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Owner_inbox_follows_notification_preference_and_close_is_audited()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        await bob.PutAsJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest(
            "All", false, false, null, null, 0, "Etc/UTC", false, null));

        var messageId = await SendAsync(alice, $"inbox-src-{Guid.NewGuid():N}");
        var created = await CreateAsync(alice, $"inbox-{Guid.NewGuid():N}", messageId, "action_item", "Cobrar o follow-up", SeedData.BobUserId.Value);

        var inbox = await bob.GetFromJsonAsync<InboxPageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/inbox?type=commitment", JsonOptions);
        inbox!.Items.Should().Contain(item => item.ItemKey == $"commitment:{created.Id:D}" && item.Reason == "assigned");
        inbox.Items.Single(item => item.ItemKey == $"commitment:{created.Id:D}").BodyPreview.Should().Be("Cobrar o follow-up");

        await bob.PutAsJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest(
            "None", false, false, null, null, 0, "Etc/UTC", false, null));
        var muted = await bob.GetFromJsonAsync<InboxPageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/inbox?type=commitment", JsonOptions);
        muted!.Items.Should().NotContain(item => item.ItemKey == $"commitment:{created.Id:D}");
        await bob.PutAsJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest(
            "MentionsAndDms", false, false, null, null, 0, "Etc/UTC", false, null));

        var list = await bob.GetFromJsonAsync<CommitmentPageDto>(
            $"/api/v1/workspaces/{WorkspaceId}/commitments?ownerId={SeedData.BobUserId.Value}", JsonOptions);
        list!.Items.Should().Contain(item => item.Id == created.Id);

        var closed = await alice.PostAsync($"/api/v1/workspaces/{WorkspaceId}/commitments/{created.Id}/close", null);
        closed.StatusCode.Should().Be(HttpStatusCode.OK);
        var closedBody = await closed.Content.ReadFromJsonAsync<CommitmentDto>(JsonOptions);
        closedBody!.State.Should().Be("closed");

        await using var db = factory.CreateMigratorDbContext();
        var audit = await db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.EntityId == created.Id.ToString() && x.Action == AuditActions.CommitmentClose)
            .ToListAsync();
        audit.Should().ContainSingle();
        audit[0].MetadataJson.Should().NotContain("Cobrar o follow-up");
    }

    [Fact]
    public async Task Delete_redacts_the_origin_without_leaking_the_body()
    {
        using var alice = Client("alice");
        var body = $"redact-body-{Guid.NewGuid():N}";
        var messageId = await SendAsync(alice, body);
        var created = await CreateAsync(alice, $"redact-{Guid.NewGuid():N}", messageId, "decision", "Origem some", null);

        var deleted = await alice.DeleteAsync($"/api/v1/channels/{ChannelId}/messages/{messageId}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var fetched = await alice.GetFromJsonAsync<CommitmentDto>(
            $"/api/v1/workspaces/{WorkspaceId}/commitments/{created.Id}", JsonOptions);
        fetched!.SourceRedacted.Should().BeTrue();
        JsonSerializer.Serialize(fetched).Should().NotContain(body);

        await using var db = factory.CreateMigratorDbContext();
        var row = await db.Commitments.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Id);
        row.SourceRedacted.Should().BeTrue();
        row.Title.Should().Be("Origem some");
    }

    [Fact]
    public async Task Unknown_assignee_is_rejected()
    {
        using var alice = Client("alice");
        var messageId = await SendAsync(alice, $"assignee-{Guid.NewGuid():N}");
        var response = await alice.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/commitments",
            new CreateCommitmentRequest(
                $"bad-{Guid.NewGuid():N}",
                messageId,
                "action_item",
                "Sem dono válido",
                null,
                Guid.NewGuid(),
                null,
                "channel"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    private static async Task<CommitmentDto> CreateAsync(
        HttpClient client,
        string key,
        Guid messageId,
        string kind,
        string title,
        Guid? ownerId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/commitments",
            new CreateCommitmentRequest(key, messageId, kind, title, "nota curada", ownerId, null, "channel"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CommitmentDto>(JsonOptions);
        dto.Should().NotBeNull();
        return dto!;
    }

    private sealed record CommitmentDto(
        Guid Id,
        string Kind,
        string Title,
        string? Description,
        Guid SourceMessageId,
        long SourceSequence,
        bool SourceRedacted,
        string State,
        string ExcerptHash);

    private sealed record CommitmentPageDto(CommitmentDto[] Items, string? NextCursor);

    private sealed record InboxItemDto(string ItemKey, string Kind, string Reason, string BodyPreview);

    private sealed record InboxPageDto(InboxItemDto[] Items);
}
