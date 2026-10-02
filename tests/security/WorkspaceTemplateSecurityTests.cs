using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Directory;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-115 — only workspace.admin, and a template does not cross tenants.</summary>
[Collection(SecurityCollection.Name)]
public sealed class WorkspaceTemplateSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid DemoWorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_cannot_list_preview_apply_export_or_read_onboarding()
    {
        using var bob = Client("bob");
        var list = await bob.GetAsync($"/api/v1/admin/workspaces/{DemoWorkspaceId}/templates");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var preview = await bob.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{DemoWorkspaceId}/templates/preview",
            new { templateId = WorkspaceTemplateRules.TeamId });
        preview.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var apply = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/workspaces/{DemoWorkspaceId}/templates/apply");
        apply.Headers.TryAddWithoutValidation("Idempotency-Key", $"bob-{Guid.NewGuid():N}");
        apply.Content = JsonContent.Create(new { templateId = WorkspaceTemplateRules.TeamId });
        (await bob.SendAsync(apply)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await bob.GetAsync($"/api/v1/admin/workspaces/{DemoWorkspaceId}/templates/export")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await bob.GetAsync($"/api/v1/admin/workspaces/{DemoWorkspaceId}/onboarding")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Custom_template_is_not_visible_in_another_workspace()
    {
        var home = await SeedWorkspaceAsync(includeDemo: true);
        var other = await SeedWorkspaceAsync(includeDemo: true);
        var stranger = await SeedWorkspaceAsync(includeDemo: false);
        using var admin = Client("demo");

        var imported = await admin.PostAsJsonAsync(
            $"/api/v1/admin/workspaces/{home}/templates/import",
            JsonDocument.Parse(
                """
                {"schema":"vibechat.workspace-template.v1","id":"squad","version":1,"spaces":[{"key":"squad","name":"Squad","channels":[{"key":"geral","name":"squad-geral","type":"Public"}]}]}
                """).RootElement);
        imported.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await admin.GetFromJsonAsync<CatalogDto>($"/api/v1/admin/workspaces/{other}/templates", Json);
        listed!.Custom.Should().BeEmpty();
        listed.Builtins.Select(x => x.Id).Should().Contain(WorkspaceTemplateRules.TeamId);

        var missing = await admin.GetAsync($"/api/v1/admin/workspaces/{other}/templates/squad/export");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var denied = await admin.GetAsync($"/api/v1/admin/workspaces/{stranger}/templates");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private async Task<Guid> SeedWorkspaceAsync(bool includeDemo)
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
            Name = "Templates sec",
            Slug = $"sec-{workspaceId.Value:N}"[..32],
            AiEnabled = false,
            CreatedAt = now
        });
        if (includeDemo)
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                TenantId = new TenantId(workspaceId.Value),
                WorkspaceId = workspaceId,
                UserId = SeedData.DemoUserId,
                Role = Role.WorkspaceOwner,
                JoinedAt = now
            });
        }

        await db.SaveChangesAsync();
        return workspaceId.Value;
    }

    private sealed record CatalogDto(SummaryDto[] Builtins, SummaryDto[] Custom);
    private sealed record SummaryDto(string Id);
}
