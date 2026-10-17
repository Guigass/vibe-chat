using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class IncidentSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid DemoWorkspace = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Outsider_does_not_see_title_or_severity_and_role_stays_member()
    {
        using var bob = Client("bob");
        var denied = await bob.PostAsJsonAsync($"/api/v1/workspaces/{DemoWorkspace}/incidents", new
        {
            idempotencyKey = $"sec-{Guid.NewGuid():N}"[..20],
            templateKey = "default",
            title = "Negado",
            severity = "sev1",
        });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var demo = Client("demo");
        var title = $"TituloSecreto{Guid.NewGuid():N}"[..24];
        var created = await demo.PostAsJsonAsync($"/api/v1/workspaces/{DemoWorkspace}/incidents", new
        {
            idempotencyKey = $"ok-{Guid.NewGuid():N}"[..20],
            templateKey = "default",
            title,
            severity = "sev1",
            responderIds = new[] { SeedData.AliceUserId.Value },
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var incident = (await created.Content.ReadFromJsonAsync<IncidentDto>(JsonOptions))!;

        using var alice = Client("alice");
        var visible = await alice.GetFromJsonAsync<IncidentDto>(
            $"/api/v1/workspaces/{DemoWorkspace}/incidents/{incident.Id}", JsonOptions);
        visible!.Title.Should().Be(title);
        visible.Severity.Should().Be("sev1");

        var hidden = await bob.GetAsync($"/api/v1/workspaces/{DemoWorkspace}/incidents/{incident.Id}");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var hiddenBody = await hidden.Content.ReadAsStringAsync();
        hiddenBody.Should().Contain("IncidentNotFound");
        hiddenBody.Should().NotContain(title);
        hiddenBody.Should().NotContain("sev1");

        var channels = await bob.GetStringAsync($"/api/v1/workspaces/{DemoWorkspace}/channels");
        channels.Should().NotContain(title);
        channels.Should().NotContain(title.ToLowerInvariant());

        var export = await alice.GetAsync($"/api/v1/workspaces/{DemoWorkspace}/incidents/{incident.Id}/postmortem");
        export.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await export.Content.ReadAsStringAsync()).Should().NotContain(title);

        await using var db = factory.CreateMigratorDbContext();
        var role = await db.WorkspaceMembers.IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == SeedData.DemoWorkspaceId && row.UserId == SeedData.AliceUserId)
            .Select(row => row.Role)
            .SingleAsync();
        role.Should().Be(Role.Member);

        var foreignId = VibeChat.SharedKernel.WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = foreignId,
            TenantId = new TenantId(foreignId.Value),
            Name = "Incident sec",
            Slug = $"inc-{foreignId.Value:N}"[..20],
            AiEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var cross = await demo.GetAsync($"/api/v1/workspaces/{foreignId.Value}/incidents/{incident.Id}");
        cross.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record IncidentDto(Guid Id, string Title, string Severity);
}
