using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Messaging;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class FormIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Published_schema_stays_immutable_retry_is_safe_and_actor_cannot_be_spoofed()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var title = $"Form {Guid.NewGuid():N}"[..20];
        var created = await CreateAsync(alice, title, ["single", "1", "message"], [SeedData.BobUserId.Value]);
        var published = await PublishAsync(alice, created.Id);
        published.PublishedVersion.Should().Be(1);

        var hidden = await alice.PostAsJsonAsync(SubmitPath(created.Id), new
        {
            idempotencyKey = $"hid-{Guid.NewGuid():N}",
            answers = new { reason = "ok", note = "secret" },
        });
        hidden.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await hidden.Content.ReadAsStringAsync()).Should().Contain("HiddenField");

        var key = $"sub-{Guid.NewGuid():N}";
        var submitted = await SubmitAsync(alice, created.Id, key, "pedido");
        var replay = await SubmitAsync(alice, created.Id, key, "outro");
        replay.Id.Should().Be(submitted.Id);
        submitted.SchemaVersion.Should().Be(1);

        var spoof = await alice.PostAsJsonAsync(DecidePath(created.Id, submitted.Id), new
        {
            idempotencyKey = $"spoof-{Guid.NewGuid():N}",
            decision = "approve",
            actorUserId = SeedData.BobUserId.Value,
        });
        spoof.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await spoof.Content.ReadAsStringAsync()).Should().Contain("ActorNotAllowed");

        var decisionKey = $"ok-{Guid.NewGuid():N}";
        var approved = await DecideAsync(bob, created.Id, submitted.Id, decisionKey);
        var again = await DecideAsync(bob, created.Id, submitted.Id, decisionKey);
        approved.State.Should().Be("approved");
        again.Id.Should().Be(approved.Id);
        approved.ResultMessageId.Should().NotBeNull();
        again.ResultMessageId.Should().Be(approved.ResultMessageId);

        await using var db = factory.CreateMigratorDbContext();
        var messages = await db.Messages.IgnoreQueryFilters()
            .CountAsync(row => row.Id == new MessageId(approved.ResultMessageId!.Value));
        messages.Should().Be(1);
        var outbox = await db.OutboxMessages.IgnoreQueryFilters()
            .Where(row => row.Type == FormPolicies.Approved && row.Payload.Contains(submitted.Id.ToString()))
            .ToListAsync();
        outbox.Should().ContainSingle();
        outbox[0].Payload.Should().NotContain("pedido");

        await UpdateAsync(alice, created.Id, title, "amount", "number");
        var republished = await PublishAsync(alice, created.Id);
        republished.PublishedVersion.Should().Be(2);
        var stored = await db.FormSubmissions.IgnoreQueryFilters().SingleAsync(row => row.Id == submitted.Id);
        stored.SchemaVersion.Should().Be(1);
        stored.AnswersJson.Should().Contain("pedido");
        var versionOne = await db.FormSchemaVersions.IgnoreQueryFilters()
            .SingleAsync(row => row.FormId == created.Id && row.Version == 1);
        versionOne.SchemaJson.Should().NotContain("amount");
    }

    [Fact]
    public async Task Quorum_creates_one_task_and_archive_blocks_submit()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var title = $"Quorum {Guid.NewGuid():N}"[..18];
        var created = await CreateAsync(
            alice,
            title,
            ["n_of_m", "2", "task"],
            [SeedData.AliceUserId.Value, SeedData.BobUserId.Value]);
        await PublishAsync(alice, created.Id);
        var submitted = await SubmitAsync(alice, created.Id, $"q-{Guid.NewGuid():N}", "dois");

        var first = await DecideAsync(bob, created.Id, submitted.Id, $"b-{Guid.NewGuid():N}");
        first.State.Should().Be("pending");
        first.ResultTaskId.Should().BeNull();

        var aliceKey = $"a-{Guid.NewGuid():N}";
        var second = await DecideAsync(alice, created.Id, submitted.Id, aliceKey);
        second.State.Should().Be("approved");
        second.ResultTaskId.Should().NotBeNull();
        var replay = await DecideAsync(alice, created.Id, submitted.Id, aliceKey);
        replay.ResultTaskId.Should().Be(second.ResultTaskId);

        await using var db = factory.CreateMigratorDbContext();
        var tasks = await db.MessageTasks.IgnoreQueryFilters().CountAsync(row => row.Id == second.ResultTaskId);
        tasks.Should().Be(1);
        (await db.MessageTasks.IgnoreQueryFilters().SingleAsync(row => row.Id == second.ResultTaskId)).Description.Should().BeNull();

        var archived = await alice.PostAsync(ArchivePath(created.Id), null);
        archived.StatusCode.Should().Be(HttpStatusCode.OK);
        var blocked = await alice.PostAsJsonAsync(SubmitPath(created.Id), new
        {
            idempotencyKey = $"late-{Guid.NewGuid():N}",
            answers = new { reason = "tarde" },
        });
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("FormArchived");
    }

    [Fact]
    public async Task Disabled_flag_does_not_write_and_retention_clears_answers()
    {
        using var disabledHost = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Forms:Enabled", "false"));
        using var disabled = disabledHost.CreateClient();
        disabled.DefaultRequestHeaders.Add("X-Dev-User", "alice");
        var off = await disabled.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/forms", new
        {
            idempotencyKey = $"off-{Guid.NewGuid():N}",
            channelId = ChannelId,
            title = "Nada",
            schema = Schema(["single", "1", "none"], [SeedData.BobUserId.Value], "reason", "text"),
        });
        off.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await off.Content.ReadAsStringAsync()).Should().Contain("FormDisabled");

        using var alice = Client("alice");
        var created = await CreateAsync(alice, $"Ret {Guid.NewGuid():N}"[..12], ["single", "1", "none"], [SeedData.AliceUserId.Value]);
        await PublishAsync(alice, created.Id);
        var submitted = await SubmitAsync(alice, created.Id, $"ret-{Guid.NewGuid():N}", "sai");
        await using var db = factory.CreateMigratorDbContext();
        var row = await db.FormSubmissions.IgnoreQueryFilters().SingleAsync(item => item.Id == submitted.Id);
        row.RetainUntil = DateTimeOffset.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
        var purged = await FormRetention.PurgeExpiredAsync(db, SeedData.DemoTenantId, DateTimeOffset.UtcNow, 20, CancellationToken.None);
        purged.Should().BeGreaterThan(0);
        await db.SaveChangesAsync();
        var export = await alice.GetFromJsonAsync<ExportDto>($"/api/v1/workspaces/{WorkspaceId}/forms/{created.Id}/export", JsonOptions);
        export!.Items.Single(item => item.Id == submitted.Id).AnswersPurged.Should().BeTrue();
        (await db.FormSubmissions.IgnoreQueryFilters().SingleAsync(item => item.Id == submitted.Id)).AnswersJson.Should().Be("{}");
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private async Task<FormDto> CreateAsync(HttpClient client, string title, string[] approval, Guid[] reviewers)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/forms", new
        {
            idempotencyKey = $"form-{Guid.NewGuid():N}",
            channelId = ChannelId,
            title,
            schema = Schema(approval, reviewers, "reason", "text"),
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FormDto>(JsonOptions))!;
    }

    private async Task UpdateAsync(HttpClient client, Guid formId, string title, string key, string type)
    {
        var response = await client.PatchAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/forms/{formId}", new
        {
            title,
            schema = Schema(["single", "1", "message"], [SeedData.BobUserId.Value], key, type),
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<FormDto> PublishAsync(HttpClient client, Guid formId)
    {
        var response = await client.PostAsync($"/api/v1/workspaces/{WorkspaceId}/forms/{formId}/publish", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FormDto>(JsonOptions))!;
    }

    private async Task<SubmissionDto> SubmitAsync(HttpClient client, Guid formId, string key, string reason)
    {
        var response = await client.PostAsJsonAsync(SubmitPath(formId), new
        {
            idempotencyKey = key,
            answers = new { reason },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SubmissionDto>(JsonOptions))!;
    }

    private async Task<SubmissionDto> DecideAsync(HttpClient client, Guid formId, Guid submissionId, string key)
    {
        var response = await client.PostAsJsonAsync(DecidePath(formId, submissionId), new
        {
            idempotencyKey = key,
            decision = "approve",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SubmissionDto>(JsonOptions))!;
    }

    private static object Schema(string[] approval, Guid[] reviewers, string key, string type) => new
    {
        contractVersion = "form.schema.v1",
        fields = new object[]
        {
            new { key, type, label = "Motivo", required = true, hidden = false, options = Array.Empty<string>() },
            new { key = "note", type = "text", label = "Interno", required = false, hidden = true, options = Array.Empty<string>() },
        },
        approval = new { mode = approval[0], required = int.Parse(approval[1]), reviewerUserIds = reviewers },
        onApproved = approval[2],
    };

    private static string SubmitPath(Guid formId) => $"/api/v1/workspaces/{WorkspaceId}/forms/{formId}/submissions";

    private static string DecidePath(Guid formId, Guid submissionId) =>
        $"/api/v1/workspaces/{WorkspaceId}/forms/{formId}/submissions/{submissionId}/decisions";

    private static string ArchivePath(Guid formId) => $"/api/v1/workspaces/{WorkspaceId}/forms/{formId}/archive";

    private sealed record FormDto(Guid Id, string Title, string Status, int PublishedVersion);

    private sealed record DecisionDto(Guid ActorUserId, string Decision);

    private sealed record SubmissionDto(
        Guid Id,
        int SchemaVersion,
        string State,
        bool AnswersPurged,
        Guid? ResultMessageId,
        Guid? ResultTaskId,
        DecisionDto[] Decisions);

    private sealed record ExportDto(FormSubmissionResponse[] Items);

    private sealed record FormSubmissionResponse(Guid Id, bool AnswersPurged);
}
