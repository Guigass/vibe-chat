using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class InitialWorkspaceBootstrapIntegrationTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Bootstrap_is_idempotent_and_creates_only_the_minimum_alpha_workspace()
    {
        using var client = factory.CreateClient();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:InitialAdminEmail"] = "owner@alpha.local",
                ["Bootstrap:WorkspaceName"] = "Alpha Teste",
                ["Bootstrap:WorkspaceSlug"] = "alpha-teste"
            })
            .Build();

        await using var db = factory.CreateMigratorDbContext();
        var bootstrap = new InitialWorkspaceBootstrap(
            db,
            new SystemClock(),
            NullLogger<InitialWorkspaceBootstrap>.Instance,
            configuration);

        await bootstrap.EnsureAsync(CancellationToken.None);
        await bootstrap.EnsureAsync(CancellationToken.None);

        var workspace = await db.Workspaces.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == InitialWorkspaceBootstrap.WorkspaceId);
        workspace.Name.Should().Be("Alpha Teste");
        workspace.Slug.Should().Be("alpha-teste");
        workspace.AiEnabled.Should().BeFalse();

        var admin = await db.UserProfiles
            .SingleAsync(item => item.Id == InitialWorkspaceBootstrap.AdminUserId);
        admin.Subject.Should().Be(WorkspaceRolePolicies.PendingSubjectForEmail("owner@alpha.local"));

        (await db.WorkspaceMembers.IgnoreQueryFilters().CountAsync(item =>
            item.WorkspaceId == InitialWorkspaceBootstrap.WorkspaceId && item.UserId == admin.Id))
            .Should().Be(1);
        (await db.ChannelMembers.IgnoreQueryFilters().CountAsync(item =>
            item.ChannelId == InitialWorkspaceBootstrap.GeneralChannelId && item.UserId == admin.Id))
            .Should().Be(1);
        (await db.Messages.IgnoreQueryFilters().CountAsync(item =>
            item.TenantId == InitialWorkspaceBootstrap.TenantId))
            .Should().Be(0);
    }
}
