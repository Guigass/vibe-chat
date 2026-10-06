using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.SecurityTests;

[Collection(SecurityCollection.Name)]
public sealed class MemberProfileSecurityTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Outsider_cannot_read_another_tenants_card()
    {
        using var alice = Client("alice");
        (await alice.GetAsync("/health")).EnsureSuccessStatusCode();
        var foreignWorkspaceId = await SeedForeignCardAsync("cargo-secreto-de-outro-tenant");

        var foreign = await alice.GetAsync(
            $"/api/v1/workspaces/{foreignWorkspaceId}/members/{SeedData.DemoUserId.Value}/profile");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var home = await alice.GetStringAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members");
        home.Should().NotContain("cargo-secreto-de-outro-tenant");
    }

    [Fact]
    public async Task Caller_cannot_change_a_peers_identity_by_writing_their_own_card()
    {
        using var bob = Client("bob");
        var before = await bob.GetFromJsonAsync<CardDto>(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.BobUserId.Value}/profile");

        using var alice = Client("alice");
        (await alice.PutAsJsonAsync("/api/v1/me/profile", new
        {
            displayName = "Alice",
            jobTitle = "só a alice"
        })).EnsureSuccessStatusCode();

        var after = await bob.GetFromJsonAsync<CardDto>(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.BobUserId.Value}/profile");
        after!.DisplayName.Should().Be(before!.DisplayName);
        after.JobTitle.Should().Be(before.JobTitle);
        after.JobTitle.Should().NotBe("só a alice");
    }

    [Fact]
    public async Task User_without_workspace_membership_cannot_read_or_write_a_card()
    {
        using var guest = factory.CreateClient();
        guest.DefaultRequestHeaders.Add("X-Dev-User", "profile-outsider");
        guest.DefaultRequestHeaders.Add("X-Dev-Email", "profile-outsider@vibechat.local");

        var read = await guest.GetAsync(
            $"/api/v1/workspaces/{SeedData.DemoWorkspaceId.Value}/members/{SeedData.AliceUserId.Value}/profile");
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var write = await guest.PutAsJsonAsync("/api/v1/me/profile", new { displayName = "Intruso" });
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        return client;
    }

    private async Task<Guid> SeedForeignCardAsync(string jobTitle)
    {
        await using var db = factory.CreateMigratorDbContext();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = WorkspaceId.New();
        var tenantId = new TenantId(workspaceId.Value);
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Profile {workspaceId.Value:N}",
            Slug = $"pf-{workspaceId.Value:N}",
            AiEnabled = false,
            CreatedAt = now
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            UserId = SeedData.DemoUserId,
            Role = Role.WorkspaceOwner,
            JoinedAt = now
        });
        db.MemberPublicProfiles.Add(new MemberPublicProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = SeedData.DemoUserId,
            JobTitle = jobTitle,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return workspaceId.Value;
    }

    private sealed record CardDto(Guid UserId, string DisplayName, string? JobTitle);
}
