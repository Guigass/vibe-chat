using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

/// <summary>B-115 — apply, dry-run, retry, conflict rollback, import and export.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class WorkspaceTemplateIntegrationTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Builtin_apply_creates_structure_and_retry_does_not_duplicate()
    {
        var workspaceId = await SeedWorkspaceAsync();
        using var admin = Client("demo");
        var key = $"apply-{Guid.NewGuid():N}";

        var created = await ApplyAsync(admin, workspaceId, WorkspaceTemplateRules.TeamId, key);
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await created.Content.ReadFromJsonAsync<ApplyDto>(Json);
        first!.Idempotent.Should().BeFalse();
        first.Items.Should().Contain(x => x.Kind == "space" && x.Action == "create" && x.Name == "Time");
        first.Items.Should().Contain(x => x.Name == "avisos" && x.Action == "create");

        await using var db = factory.CreateMigratorDbContext();
        var tenantId = new TenantId(workspaceId);
        var workspace = new WorkspaceId(workspaceId);
        (await db.Spaces.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == workspace && x.Name == "Time")).Should().Be(1);
        var channels = await db.Channels.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspace).ToListAsync();
        channels.Select(x => x.Name).Should().BeEquivalentTo("geral", "avisos");
        channels.Single(x => x.Name == "avisos").Type.Should().Be(ChannelType.Announcement);
        channels.Single(x => x.Name == "avisos").Topic.Should().Be("Avisos do time");
        var policy = await db.MessageLifecyclePolicies.IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId);
        policy.DeleteWindowMinutes.Should().Be(1440);
        var audits = await db.AuditEvents.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.Action).ToListAsync();
        audits.Should().Contain(AuditActions.SpaceCreate);
        audits.Should().Contain(AuditActions.ChannelCreate);
        audits.Should().Contain(AuditActions.TemplateApply);
        audits.Should().NotContain(x => x.Contains("secret", StringComparison.OrdinalIgnoreCase));

        var replay = await ApplyAsync(admin, workspaceId, WorkspaceTemplateRules.TeamId, key);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<ApplyDto>(Json))!.Idempotent.Should().BeTrue();

        var again = await ApplyAsync(admin, workspaceId, WorkspaceTemplateRules.TeamId, $"again-{Guid.NewGuid():N}");
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<ApplyDto>(Json))!.Items.Should().OnlyContain(x => x.Action == "reuse");
        (await db.Spaces.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == workspace && x.Name == "Time")).Should().Be(1);
        (await db.Channels.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == workspace)).Should().Be(2);
        (await db.TemplateApplications.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == workspace)).Should().Be(2);
    }

    [Fact]
    public async Task Dry_run_and_preview_do_not_persist()
    {
        var workspaceId = await SeedWorkspaceAsync();
        using var admin = Client("demo");
        var before = await CountSpacesAsync(workspaceId);

        var preview = await admin.PostAsJsonAsync(Preview(workspaceId), new { templateId = WorkspaceTemplateRules.ProjectId });
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await preview.Content.ReadFromJsonAsync<ApplyDto>(Json);
        plan!.DryRun.Should().BeTrue();
        plan.Items.Should().Contain(x => x.Action == "create" && x.Name == "Projeto");

        var dry = await admin.PostAsJsonAsync(Apply(workspaceId), new { templateId = WorkspaceTemplateRules.ProjectId, dryRun = true });
        dry.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountSpacesAsync(workspaceId)).Should().Be(before);
        await using var db = factory.CreateMigratorDbContext();
        (await db.TemplateApplications.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == new WorkspaceId(workspaceId))).Should().Be(0);
        (await db.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.TenantId == new TenantId(workspaceId) && x.Action == AuditActions.TemplateApply)).Should().Be(0);
    }

    [Fact]
    public async Task Conflict_rolls_back_without_persisting()
    {
        var workspaceId = await SeedWorkspaceAsync();
        await using (var db = factory.CreateMigratorDbContext())
        {
            var spaceId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            db.Spaces.Add(new Space
            {
                Id = spaceId,
                TenantId = new TenantId(workspaceId),
                WorkspaceId = new WorkspaceId(workspaceId),
                Name = "Outro",
                Order = 0,
                CreatedAt = now
            });
            db.Channels.Add(new Channel
            {
                Id = ChannelId.New(),
                TenantId = new TenantId(workspaceId),
                WorkspaceId = new WorkspaceId(workspaceId),
                SpaceId = spaceId,
                Name = "geral",
                Type = ChannelType.Private,
                CreatedAt = now,
                CreatedBy = SeedData.DemoUserId
            });
            await db.SaveChangesAsync();
        }

        using var admin = Client("demo");
        var response = await ApplyAsync(admin, workspaceId, WorkspaceTemplateRules.TeamId, $"conflict-{Guid.NewGuid():N}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(WorkspaceTemplateRules.Conflict);

        await using var check = factory.CreateMigratorDbContext();
        var workspace = new WorkspaceId(workspaceId);
        (await check.Spaces.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == workspace && x.Name == "Time")).Should().Be(0);
        (await check.Channels.IgnoreQueryFilters().SingleAsync(x => x.WorkspaceId == workspace)).Type.Should().Be(ChannelType.Private);
        (await check.TemplateApplications.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == workspace)).Should().Be(0);
    }

    [Fact]
    public async Task Import_rejects_unknown_field_and_version_and_export_has_no_personal_data()
    {
        var workspaceId = await SeedWorkspaceAsync();
        using var admin = Client("demo");
        var validate = Validate(workspaceId);

        var members = await admin.PostAsJsonAsync(validate, JsonDocument.Parse(
            """
            {"schema":"vibechat.workspace-template.v1","id":"squad","version":1,"members":[{"email":"a@b.c"}],"spaces":[{"key":"squad","name":"Squad","channels":[{"key":"geral","name":"squad-geral","type":"Public"}]}]}
            """).RootElement);
        members.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var memberBody = await members.Content.ReadAsStringAsync();
        memberBody.Should().Contain(WorkspaceTemplateRules.UnknownField);
        memberBody.Should().Contain("$.members");

        var version = await admin.PostAsJsonAsync(validate, JsonDocument.Parse(
            """
            {"schema":"vibechat.workspace-template.v2","id":"squad","version":1,"spaces":[]}
            """).RootElement);
        version.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await version.Content.ReadAsStringAsync()).Should().Contain(WorkspaceTemplateRules.UnknownSchema);

        var imported = await admin.PostAsJsonAsync(Import(workspaceId), JsonDocument.Parse(
            """
            {"schema":"vibechat.workspace-template.v1","id":"squad","version":1,"spaces":[{"key":"squad","name":"Squad","channels":[{"key":"geral","name":"squad-geral","type":"Public","topic":"Squad"}]}],"checklist":[{"key":"invite-members"}]}
            """).RootElement);
        imported.StatusCode.Should().Be(HttpStatusCode.OK);

        var applied = await ApplyAsync(admin, workspaceId, "squad", $"squad-{Guid.NewGuid():N}");
        applied.StatusCode.Should().Be(HttpStatusCode.OK);

        var exported = await admin.GetAsync(Export(workspaceId));
        exported.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await exported.Content.ReadAsStringAsync();
        body.Should().Contain("squad-geral");
        body.Should().NotContain("@");
        body.Should().NotContain(SeedData.DemoUserId.Value.ToString());
        WorkspaceTemplateRules.PropertyNames(body).Should().NotContain(
            ["tenantId", "TenantId", "members", "secrets", "email", "token", "userId"]);
        WorkspaceTemplateRules.TryParse(body, out _, out var error, out _).Should().BeTrue(error);
    }

    [Fact]
    public async Task Onboarding_can_be_skipped_and_resumed()
    {
        var workspaceId = await SeedWorkspaceAsync();
        using var admin = Client("demo");
        var initial = await admin.GetFromJsonAsync<OnboardingDto>(Onboarding(workspaceId), Json);
        initial!.Status.Should().Be(OnboardingRules.Pending);
        initial.Items.Select(x => x.Key).Should().Equal(OnboardingRules.DefaultChecklist);

        var skipped = await admin.PutAsJsonAsync(Onboarding(workspaceId), new { status = OnboardingRules.Skipped });
        skipped.StatusCode.Should().Be(HttpStatusCode.OK);
        (await skipped.Content.ReadFromJsonAsync<OnboardingDto>(Json))!.Status.Should().Be(OnboardingRules.Skipped);

        var resumed = await admin.PutAsJsonAsync(Onboarding(workspaceId), new { status = OnboardingRules.InProgress });
        resumed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resumed.Content.ReadFromJsonAsync<OnboardingDto>(Json))!.Status.Should().Be(OnboardingRules.InProgress);
    }

    private static string Preview(Guid workspaceId) => $"/api/v1/admin/workspaces/{workspaceId}/templates/preview";
    private static string Apply(Guid workspaceId) => $"/api/v1/admin/workspaces/{workspaceId}/templates/apply";
    private static string Validate(Guid workspaceId) => $"/api/v1/admin/workspaces/{workspaceId}/templates/validate";
    private static string Import(Guid workspaceId) => $"/api/v1/admin/workspaces/{workspaceId}/templates/import";
    private static string Export(Guid workspaceId) => $"/api/v1/admin/workspaces/{workspaceId}/templates/export";
    private static string Onboarding(Guid workspaceId) => $"/api/v1/admin/workspaces/{workspaceId}/onboarding";

    private async Task<HttpResponseMessage> ApplyAsync(HttpClient client, Guid workspaceId, string templateId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Apply(workspaceId));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        request.Content = JsonContent.Create(new { templateId });
        return await client.SendAsync(request);
    }

    private async Task<int> CountSpacesAsync(Guid workspaceId)
    {
        await using var db = factory.CreateMigratorDbContext();
        return await db.Spaces.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == new WorkspaceId(workspaceId));
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private async Task<Guid> SeedWorkspaceAsync()
    {
        using var warmup = Client("demo");
        _ = warmup.BaseAddress;
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = new TenantId(workspaceId.Value),
            Name = "Templates",
            Slug = $"tpl-{workspaceId.Value:N}"[..32],
            AiEnabled = false,
            CreatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = new TenantId(workspaceId.Value),
            WorkspaceId = workspaceId,
            UserId = SeedData.DemoUserId,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });
        await db.SaveChangesAsync();
        return workspaceId.Value;
    }

    private sealed record ApplyDto(string TemplateId, bool DryRun, bool Idempotent, bool HasConflicts, PlanItemDto[] Items);
    private sealed record PlanItemDto(string Kind, string Action, string? Name);
    private sealed record OnboardingDto(string Status, OnboardingItemDto[] Items);
    private sealed record OnboardingItemDto(string Key, string State);
}
