using System.Security.Claims;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class SsoLinkingIntegrationTests(VibeChatApiFactory factory)
{
    [Fact]
    public async Task Broker_login_with_the_same_email_does_not_take_an_existing_membership()
    {
        Warm();
        var adminId = UserId.New();
        var email = $"owner-{Guid.NewGuid():N}@corp.test";
        var issuer = $"https://idp.example/{Guid.NewGuid():N}";
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.UserProfiles.Add(Profile(adminId, "kc-admin-" + Guid.NewGuid().ToString("N"), email));
            db.WorkspaceMembers.Add(Member(adminId, Role.WorkspaceOwner));
            await db.SaveChangesAsync();
        }

        await using var linkDb = factory.CreateMigratorDbContext();
        var created = await SsoProfileLinker.TryLinkAsync(
            Principal("kc-attacker", email, issuer, "external-subject"),
            linkDb,
            new SystemClock(),
            new SsoLinkSettings(true),
            CancellationToken.None);

        created.Should().NotBeNull();
        created!.Id.Should().NotBe(adminId);
        (await linkDb.WorkspaceMembers.IgnoreQueryFilters().CountAsync(x => x.UserId == created.Id)).Should().Be(0);
        (await linkDb.WorkspaceMembers.IgnoreQueryFilters().CountAsync(
            x => x.UserId == adminId && x.Role == Role.WorkspaceOwner)).Should().Be(1);
        var link = await linkDb.ExternalIdentities.SingleAsync(x => x.Issuer == issuer);
        link.UserId.Should().Be(created.Id);
        link.Reason.Should().Be(SsoLinkReasons.FirstLogin);
    }

    [Fact]
    public async Task Pending_invite_links_the_external_pair_and_keeps_the_role()
    {
        Warm();
        var userId = UserId.New();
        var email = $"carol-{Guid.NewGuid():N}@corp.test";
        var issuer = $"https://idp.example/{Guid.NewGuid():N}";
        var keycloakSubject = "kc-carol-" + Guid.NewGuid().ToString("N");
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.UserProfiles.Add(Profile(userId, WorkspaceRolePolicies.PendingSubjectForEmail(email), email));
            db.WorkspaceMembers.Add(Member(userId, Role.Member));
            await db.SaveChangesAsync();
        }

        await using var linkDb = factory.CreateMigratorDbContext();
        var claimed = await SsoProfileLinker.TryLinkAsync(
            Principal(keycloakSubject, email, issuer, "carol-subject", "hub-oidc"),
            linkDb,
            new SystemClock(),
            new SsoLinkSettings(true),
            CancellationToken.None);

        claimed!.Id.Should().Be(userId);
        claimed.Subject.Should().Be(keycloakSubject);
        (await linkDb.WorkspaceMembers.IgnoreQueryFilters().SingleAsync(x => x.UserId == userId))
            .Role.Should().Be(Role.Member);
        (await linkDb.ExternalIdentities.SingleAsync(x => x.Issuer == issuer)).Reason
            .Should().Be(SsoLinkReasons.PendingInvite);
    }

    [Fact]
    public async Task Rebinding_an_external_pair_onto_another_user_is_denied()
    {
        Warm();
        var first = UserId.New();
        var second = UserId.New();
        var issuer = $"https://idp.example/{Guid.NewGuid():N}";
        var secondSubject = "kc-second-" + Guid.NewGuid().ToString("N");
        await using (var db = factory.CreateMigratorDbContext())
        {
            db.UserProfiles.Add(Profile(first, "kc-first-" + Guid.NewGuid().ToString("N"), $"first-{Guid.NewGuid():N}@corp.test"));
            db.UserProfiles.Add(Profile(second, secondSubject, $"second-{Guid.NewGuid():N}@corp.test"));
            db.WorkspaceMembers.Add(Member(first, Role.Admin));
            db.WorkspaceMembers.Add(Member(second, Role.Member));
            db.ExternalIdentities.Add(new ExternalIdentity
            {
                Id = Guid.NewGuid(),
                UserId = first,
                Issuer = issuer,
                Subject = "stable-subject",
                Reason = SsoLinkReasons.FirstLogin,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using var linkDb = factory.CreateMigratorDbContext();
        var act = () => SsoProfileLinker.TryLinkAsync(
            Principal(secondSubject, "second@corp.test", issuer, "stable-subject"),
            linkDb,
            new SystemClock(),
            new SsoLinkSettings(true),
            CancellationToken.None);

        await act.Should().ThrowAsync<SsoLinkConflictException>();
        (await linkDb.ExternalIdentities.SingleAsync(x => x.Issuer == issuer)).UserId.Should().Be(first);
        (await linkDb.WorkspaceMembers.IgnoreQueryFilters().SingleAsync(x => x.UserId == second))
            .Role.Should().Be(Role.Member);
    }

    [Fact]
    public async Task Disabled_flag_ignores_broker_claims()
    {
        Warm();
        var issuer = $"https://idp.example/{Guid.NewGuid():N}";
        await using var db = factory.CreateMigratorDbContext();
        var result = await SsoProfileLinker.TryLinkAsync(
            Principal("kc-off", $"off-{Guid.NewGuid():N}@corp.test", issuer, "subject"),
            db,
            new SystemClock(),
            SsoLinkSettings.Disabled,
            CancellationToken.None);

        result.Should().BeNull();
        (await db.ExternalIdentities.CountAsync(x => x.Issuer == issuer)).Should().Be(0);
    }

    private void Warm() => factory.CreateClient().Dispose();

    private static UserProfile Profile(UserId id, string subject, string email) => new()
    {
        Id = id,
        Subject = subject,
        Email = email,
        DisplayName = email,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static WorkspaceMember Member(UserId userId, Role role) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = SeedData.DemoTenantId,
        WorkspaceId = SeedData.DemoWorkspaceId,
        UserId = userId,
        Role = role,
        JoinedAt = DateTimeOffset.UtcNow
    };

    private static ClaimsPrincipal Principal(
        string subject,
        string email,
        string issuer,
        string brokerSubject,
        string? alias = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new("sub", subject),
            new(ClaimTypes.Email, email),
            new("email", email),
            new("name", "Broker User"),
            new(SsoClaimNames.Issuer, issuer),
            new(SsoClaimNames.Subject, brokerSubject)
        };
        if (alias is not null)
        {
            claims.Add(new Claim(SsoClaimNames.Alias, alias));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
