using FluentAssertions;
using VibeChat.BuildingBlocks;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class MessageLifecyclePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Default_snapshot_matches_current_edit_and_delete_behavior()
    {
        var created = Now.AddHours(-2);
        MessageLifecyclePolicyRules.EvaluateEdit(
            MessageLifecyclePolicyRules.Default, true, true, false, Role.Member, created, Now)
            .Should().BeNull();
        MessageLifecyclePolicyRules.EvaluateEdit(
            MessageLifecyclePolicyRules.Default, false, false, true, Role.Moderator, created, Now)
            .Should().Be(MessageLifecyclePolicyRules.Forbidden);
        MessageLifecyclePolicyRules.EvaluateDelete(
            MessageLifecyclePolicyRules.Default, false, false, true, Role.Moderator, created, Now)
            .Should().BeNull();
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Message.EditAny);
        RolePermissionCatalog.For(Role.Moderator).Should().Contain(Permissions.Message.EditAny);
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Message.EditAny);
    }

    [Fact]
    public void Edit_window_expires_after_the_limit_and_allows_the_boundary()
    {
        var policy = MessageLifecyclePolicyRules.Default with { EditWindowMinutes = 15 };
        MessageLifecyclePolicyRules.EvaluateEdit(
            policy, true, true, false, Role.Member, Now.AddMinutes(-16), Now)
            .Should().Be(MessageLifecyclePolicyRules.EditWindowExpired);
        MessageLifecyclePolicyRules.EvaluateEdit(
            policy, true, true, false, Role.Member, Now.AddMinutes(-15), Now)
            .Should().BeNull();
    }

    [Fact]
    public void Disabled_edit_blocks_the_author_unless_moderator_override_applies_to_others()
    {
        var off = MessageLifecyclePolicyRules.Default with { EditEnabled = false };
        MessageLifecyclePolicyRules.EvaluateEdit(
            off, true, true, false, Role.Member, Now, Now)
            .Should().Be(MessageLifecyclePolicyRules.EditDisabled);

        var overrideOn = off with { EditAllowModeratorOverride = true };
        MessageLifecyclePolicyRules.EvaluateEdit(
            overrideOn, false, false, true, Role.Moderator, Now.AddHours(-3), Now)
            .Should().BeNull();
        MessageLifecyclePolicyRules.EvaluateEdit(
            overrideOn, false, false, false, Role.Moderator, Now, Now)
            .Should().Be(MessageLifecyclePolicyRules.Forbidden);
    }

    [Fact]
    public void Role_allow_list_denies_member_and_keeps_owner_with_admin()
    {
        var policy = MessageLifecyclePolicyRules.Default with
        {
            EditRolesRestricted = true,
            EditRoles = ["Admin"]
        };
        MessageLifecyclePolicyRules.EvaluateEdit(
            policy, true, true, false, Role.Member, Now, Now)
            .Should().Be(MessageLifecyclePolicyRules.EditRoleDenied);
        MessageLifecyclePolicyRules.EvaluateEdit(
            policy, true, true, false, Role.WorkspaceOwner, Now, Now)
            .Should().BeNull();
    }

    [Fact]
    public void Delete_mirrors_window_roles_and_override()
    {
        var restricted = MessageLifecyclePolicyRules.Default with
        {
            DeleteRolesRestricted = true,
            DeleteRoles = ["Admin"],
            DeleteAllowModeratorOverride = false,
            DeleteWindowMinutes = 15
        };
        MessageLifecyclePolicyRules.EvaluateDelete(
            restricted, true, true, false, Role.Member, Now, Now)
            .Should().Be(MessageLifecyclePolicyRules.DeleteRoleDenied);
        MessageLifecyclePolicyRules.EvaluateDelete(
            restricted, true, true, false, Role.Admin, Now.AddMinutes(-16), Now)
            .Should().Be(MessageLifecyclePolicyRules.DeleteWindowExpired);
        MessageLifecyclePolicyRules.EvaluateDelete(
            MessageLifecyclePolicyRules.Default, false, false, true, Role.Admin, Now.AddDays(-1), Now)
            .Should().BeNull();
    }

    [Fact]
    public void Window_zero_and_unknown_role_are_rejected()
    {
        MessageLifecyclePolicyRules.NormalizeWindow(0).Should().Be(MessageLifecyclePolicyRules.Invalid);
        MessageLifecyclePolicyRules.NormalizeWindow(null).Should().BeNull();
        MessageLifecyclePolicyRules.NormalizeRoles(["nope"], out _).Should().Be(MessageLifecyclePolicyRules.Invalid);
        MessageLifecyclePolicyRules.NormalizeRoles(["member", "Admin"], out var roles).Should().BeNull();
        roles.Should().Equal("Member", "Admin");
    }

    [Fact]
    public void Missing_row_dto_keeps_edit_override_off()
    {
        var dto = MessageLifecyclePolicyRules.ToDto(null);
        dto.EditEnabled.Should().BeTrue();
        dto.EditWindowMinutes.Should().BeNull();
        dto.EditRolesRestricted.Should().BeFalse();
        dto.EditAllowModeratorOverride.Should().BeFalse();
        dto.DeleteAllowModeratorOverride.Should().BeTrue();
        dto.EditRoles.Should().Equal("Member", "Moderator", "Admin");
    }
}
