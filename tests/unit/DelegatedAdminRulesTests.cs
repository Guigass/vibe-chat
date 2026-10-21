using FluentAssertions;
using VibeChat.Administration;

namespace VibeChat.UnitTests;

public sealed class DelegatedAdminRulesTests
{
    private static readonly Guid SpaceA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SpaceB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Flag_is_off_by_default()
    {
        new DelegatedAdminOptions().Enabled.Should().BeFalse();
    }

    [Fact]
    public void Space_admin_cannot_manage_or_grant_another_space()
    {
        var held = new HeldScope[] { new(DelegatedScopes.Space, SpaceA) };
        DelegatedAdminRules.CanManageSpace(false, held, SpaceA).Should().BeTrue();
        DelegatedAdminRules.CanManageSpace(false, held, SpaceB).Should().BeFalse();
        DelegatedAdminRules.CanGrant(false, held, DelegatedScopes.Space, SpaceA, self: false).Should().BeTrue();
        DelegatedAdminRules.CanGrant(false, held, DelegatedScopes.Space, SpaceB, self: false).Should().BeFalse();
        DelegatedAdminRules.CanGrant(false, held, DelegatedScopes.Workspace, Guid.Empty, self: false).Should().BeFalse();
        DelegatedAdminRules.CanGrant(false, held, DelegatedScopes.Tenant, Guid.Empty, self: false).Should().BeFalse();
    }

    [Fact]
    public void Grantor_cannot_exceed_their_scope_or_grant_themselves()
    {
        var workspace = new HeldScope[] { new(DelegatedScopes.Workspace, Guid.Empty) };
        DelegatedAdminRules.CanGrant(false, workspace, DelegatedScopes.BillingRead, Guid.Empty, self: false).Should().BeTrue();
        DelegatedAdminRules.CanGrant(false, workspace, DelegatedScopes.Compliance, Guid.Empty, self: false).Should().BeFalse();
        DelegatedAdminRules.CanGrant(true, [], DelegatedScopes.Tenant, Guid.Empty, self: true).Should().BeFalse();
        DelegatedAdminRules.CanGrant(false, [new(DelegatedScopes.BillingRead, Guid.Empty)], DelegatedScopes.BillingRead, Guid.Empty, self: false).Should().BeFalse();
    }

    [Fact]
    public void Break_glass_expiry_is_short_and_quota_limits_are_ordered()
    {
        var now = DateTimeOffset.Parse("2026-10-21T12:00:00Z");
        DelegatedAdminRules.TryExpiry(now, now.AddHours(4), breakGlass: true).Should().BeTrue();
        DelegatedAdminRules.TryExpiry(now, now.AddHours(5), breakGlass: true).Should().BeFalse();
        DelegatedAdminRules.TryExpiry(now, null, breakGlass: true).Should().BeFalse();
        DelegatedAdminRules.TryExpiry(now, now.AddDays(1), breakGlass: false).Should().BeTrue();
        DelegatedAdminRules.TryLimits(1, 2).Should().BeTrue();
        DelegatedAdminRules.TryLimits(3, 2).Should().BeFalse();
        DelegatedAdminRules.Action(true, 2, 1, 2).Should().Be("blocked");
        DelegatedAdminRules.Action(true, 1, 1, 2).Should().Be("warning");
        DelegatedAdminRules.Trend(2, 1).Should().Be("up");
    }
}
