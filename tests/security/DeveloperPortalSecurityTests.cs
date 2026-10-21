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

/// <summary>B-135 — portal reads stay inside integration.read and do not cross tenants.</summary>
[Collection(SecurityCollection.Name)]
public sealed class DeveloperPortalSecurityTests(VibeChatApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;
    private static readonly Guid ChannelId = SeedData.DemoChannelId.Value;

    [Fact]
    public async Task Member_cannot_read_the_portal_and_usage_does_not_cross_tenants()
    {
        using var admin = Client("demo");
        var created = await CreateAsync(admin, WorkspaceId, "Home Credential");

        using var bob = Client("bob");
        var denied = await bob.GetAsync($"/api/v1/workspaces/{WorkspaceId}/developer/catalog");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("VibeChat Integration API").And.NotContain(created.Token);

        var usageDenied = await bob.GetAsync($"/api/v1/workspaces/{WorkspaceId}/developer/usage");
        usageDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await usageDenied.Content.ReadAsStringAsync()).Should().NotContain(created.Last4);

        var exploreDenied = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/explore",
            new { credentialId = created.Id, operation = "messages.send", channelId = ChannelId });
        exploreDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreignWorkspaceId = await SeedForeignWorkspaceAsync();
        var foreign = await CreateAsync(admin, foreignWorkspaceId, "Foreign Credential", []);
        await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{foreignWorkspaceId}/developer/explore",
            new { credentialId = foreign.Id, operation = "messages.dm" });

        var home = await admin.GetStringAsync($"/api/v1/workspaces/{WorkspaceId}/developer/usage");
        home.Should().Contain(created.Id.ToString()).And.NotContain(foreign.Id.ToString()).And.NotContain(foreign.Token);

        var away = await admin.GetStringAsync($"/api/v1/workspaces/{foreignWorkspaceId}/developer/usage");
        away.Should().Contain(foreign.Id.ToString()).And.NotContain(created.Id.ToString());

        await using var db = factory.CreateMigratorDbContext();
        var audits = await db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.Action.StartsWith("developer."))
            .Select(x => x.MetadataJson)
            .ToListAsync();
        audits.Should().NotContain(x =>
            x.Contains(created.Token, StringComparison.Ordinal)
            || x.Contains(foreign.Token, StringComparison.Ordinal));
    }

    private async Task<CreatedCredential> CreateAsync(HttpClient admin, Guid workspaceId, string name, Guid[]? channelIds = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workspaces/{workspaceId}/developer/credentials")
        {
            Content = JsonContent.Create(new { name, channelIds = channelIds ?? [ChannelId], allowDms = true })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"s-{Guid.NewGuid():N}");
        var response = await admin.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<CreatedCredential>(Json))!;
        created.Token.Should().StartWith("vc_int_");
        return created;
    }

    private async Task<Guid> SeedForeignWorkspaceAsync()
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = VibeChat.SharedKernel.WorkspaceId.New();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = new TenantId(workspaceId.Value),
            Name = "Foreign portal",
            Slug = $"foreign-portal-{workspaceId.Value:N}",
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

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private sealed record CreatedCredential(Guid Id, string Token, string Last4);
}
