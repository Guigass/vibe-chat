using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

/// <summary>B-136 — SDK reads stay inside integration.read and do not cross tenants.</summary>
[Collection(SecurityCollection.Name)]
public sealed class PluginSdkSecurityTests(VibeChatApiFactory factory)
{
    private static readonly Guid WorkspaceId = SeedData.DemoWorkspaceId.Value;

    [Fact]
    public async Task Member_cannot_read_the_sdk_and_a_foreign_workspace_stays_hidden()
    {
        using var bob = Client("bob");
        var denied = await bob.GetAsync($"/api/v1/workspaces/{WorkspaceId}/developer/sdk");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("sdkVersion").And.NotContain("vc_int_");

        var verifyDenied = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/sdk/verify",
            new { body = "{}", signature = "sha256=00", deliveryId = "x", deliveredAt = DateTimeOffset.UtcNow });
        verifyDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var contractDenied = await bob.PostAsync($"/api/v1/workspaces/{WorkspaceId}/developer/sdk/contract-tests", null);
        contractDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var sandboxDenied = await bob.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/sdk/sandbox",
            new { operation = "messages.send", idempotencyKey = "quickstart-key", tenantId = Guid.NewGuid() });
        sandboxDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreignWorkspaceId = await SeedForeignWorkspaceAsync();
        using var admin = Client("demo");
        var foreign = await admin.GetAsync($"/api/v1/workspaces/{foreignWorkspaceId}/developer/sdk");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreign.Content.ReadAsStringAsync()).Should().NotContain(PluginSdkCatalog.Version).And.NotContain("vc_int_");

        var home = await admin.PostAsJsonAsync(
            $"/api/v1/workspaces/{WorkspaceId}/developer/sdk/sandbox",
            new
            {
                operation = "messages.send",
                idempotencyKey = "quickstart-key",
                tenantId = foreignWorkspaceId,
                body = "foreign-workspace-marker"
            });
        home.StatusCode.Should().Be(HttpStatusCode.OK);
        var homeBody = await home.Content.ReadAsStringAsync();
        homeBody.Should().NotContain(foreignWorkspaceId.ToString()).And.NotContain("foreign-workspace-marker").And.Contain("\"delivered\":false");
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
            Name = "Foreign sdk",
            Slug = $"foreign-sdk-{workspaceId.Value:N}",
            AiEnabled = false,
            CreatedAt = now
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
}
