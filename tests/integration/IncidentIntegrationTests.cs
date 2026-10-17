using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class IncidentIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid GeralMessageId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task Template_retry_rejects_invalid_transition_and_snapshot_references_evidence()
    {
        using var demo = Client("demo");
        var key = $"inc-{Guid.NewGuid():N}"[..24];
        var title = $"Sala {Guid.NewGuid():N}"[..16];
        var note = $"nota-{Guid.NewGuid():N}";
        var created = await CreateAsync(demo, key, title, Guid.NewGuid());
        var again = await CreateAsync(demo, key, title, Guid.NewGuid());
        again.Id.Should().Be(created.Id);
        again.ChannelId.Should().Be(created.ChannelId);
        again.Steps.Should().HaveCount(4);
        again.Timeline.Should().ContainSingle();
        again.Timeline[0].Kind.Should().Be(IncidentPolicies.KindAudit);

        await using var db = factory.CreateMigratorDbContext();
        (await db.IncidentRooms.IgnoreQueryFilters().CountAsync(row => row.IdempotencyKey == key)).Should().Be(1);
        (await db.Channels.IgnoreQueryFilters().CountAsync(row => row.Id == new VibeChat.SharedKernel.ChannelId(created.ChannelId))).Should().Be(1);

        var skipped = await demo.PostAsJsonAsync(Path(created.Id, "transitions"), new { status = "resolved" });
        skipped.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await skipped.Content.ReadAsStringAsync()).Should().Contain("InvalidIncidentTransition");

        var mitigating = await PostOk(demo, Path(created.Id, "transitions"), new { status = "mitigating" });
        mitigating.Status.Should().Be(IncidentPolicies.Mitigating);

        var step = mitigating.Steps.Single(item => item.Code == IncidentPolicies.StepMitigate);
        var failed = await PostOk(demo, Path(created.Id, $"steps/{step.Id}/run"), new { });
        failed.Status.Should().Be(IncidentPolicies.Mitigating);
        failed.Steps.Single(item => item.Id == step.Id).Status.Should().Be(IncidentPolicies.StepFailed);
        failed.Steps.Single(item => item.Id == step.Id).ErrorCode.Should().Be("AutomationFailed");

        var foreign = await demo.PostAsJsonAsync(Path(created.Id, "evidence"), new
        {
            idempotencyKey = $"ev-{Guid.NewGuid():N}"[..20],
            kind = "message",
            sourceId = GeralMessageId,
        });
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await foreign.Content.ReadAsStringAsync()).Should().NotContain("Bem-vindo");

        var updated = await PostOk(demo, Path(created.Id, "updates"), new
        {
            idempotencyKey = $"up-{Guid.NewGuid():N}"[..20],
            note,
        });
        updated.Timeline.Should().Contain(item => item.Kind == IncidentPolicies.KindAnnouncement && item.Note == note);

        var resolved = await PostOk(demo, Path(created.Id, "transitions"), new { status = "resolved" });
        resolved.Status.Should().Be(IncidentPolicies.Resolved);
        resolved.CanExport.Should().BeTrue();
        var export = await demo.GetAsync(Path(created.Id, "postmortem"));
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await export.Content.ReadAsStringAsync();
        body.Should().Contain(updated.Timeline[0].SourceId.ToString());
        body.Should().Contain(title);
        body.Should().NotContain(note);
        body.Should().NotContain("Bem-vindo");
    }

    [Fact]
    public async Task Disabled_flag_does_not_write()
    {
        using var disabledHost = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Incidents:Enabled", "false"));
        using var disabled = disabledHost.CreateClient();
        disabled.DefaultRequestHeaders.Add("X-Dev-User", "demo");
        var off = await disabled.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/incidents", new
        {
            idempotencyKey = $"off-{Guid.NewGuid():N}"[..20],
            templateKey = "default",
            title = "Nada",
            severity = "sev4",
        });
        off.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await off.Content.ReadAsStringAsync()).Should().Contain("IncidentDisabled");
        await using var db = factory.CreateMigratorDbContext();
        (await db.IncidentRooms.IgnoreQueryFilters().CountAsync(row => row.Title == "Nada")).Should().Be(0);
    }

    private async Task<IncidentDto> CreateAsync(HttpClient client, string key, string title, Guid automationId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{WorkspaceId}/incidents", new
        {
            idempotencyKey = key,
            templateKey = "default",
            title,
            severity = "sev2",
            correlationId = "alert-42",
            automationId,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IncidentDto>(JsonOptions))!;
    }

    private async Task<IncidentDto> PostOk(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IncidentDto>(JsonOptions))!;
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private static string Path(Guid id, string suffix) => $"/api/v1/workspaces/{WorkspaceId}/incidents/{id}/{suffix}";

    private sealed record IncidentDto(
        Guid Id,
        Guid ChannelId,
        string Title,
        string Status,
        bool CanExport,
        IncidentStepDto[] Steps,
        IncidentTimelineDto[] Timeline);

    private sealed record IncidentStepDto(Guid Id, string Code, string Status, string? ErrorCode);

    private sealed record IncidentTimelineDto(Guid SourceId, string Kind, string? Note);
}
