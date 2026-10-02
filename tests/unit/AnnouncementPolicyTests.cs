using FluentAssertions;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class AnnouncementPolicyTests
{
    [Fact]
    public void Publish_is_limited_to_admin_and_moderator()
    {
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Announcement.Publish);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Announcement.Publish);
        RolePermissionCatalog.For(Role.Moderator).Should().Contain(Permissions.Announcement.Publish);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Announcement.Publish);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Announcement.Publish);
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Announcement.Publish);
        RolePermissionCatalog.For(Role.Bot).Should().NotContain(Permissions.Announcement.Publish);
    }

    [Fact]
    public void Readers_can_acknowledge_without_publishing()
    {
        foreach (var role in new[] { Role.Member, Role.Guest, Role.Auditor, Role.Moderator, Role.Admin })
        {
            RolePermissionCatalog.For(role).Should().Contain(Permissions.Announcement.Acknowledge);
        }

        RolePermissionCatalog.For(Role.Bot).Should().NotContain(Permissions.Announcement.Acknowledge);
    }

    [Fact]
    public void Open_window_respects_close_and_deadline()
    {
        var now = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        AnnouncementPolicies.IsOpen(null, null, now).Should().BeTrue();
        AnnouncementPolicies.IsOpen(null, now.AddMinutes(1), now).Should().BeTrue();
        AnnouncementPolicies.IsOpen(null, now, now).Should().BeFalse();
        AnnouncementPolicies.IsOpen(now, now.AddHours(1), now).Should().BeFalse();
    }

    [Fact]
    public void Request_validation_rejects_deadline_outside_announcement_channels()
    {
        var now = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var act = () => AnnouncementPolicies.ValidateRequest(true, now.AddHours(1), announcementChannel: false, now);
        act.Should().Throw<ArgumentException>().WithMessage("InvalidAnnouncementChannel");
    }

    [Fact]
    public void Cursor_roundtrip_is_stable()
    {
        var at = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var cursor = AnnouncementCursors.Encode(at, id);
        AnnouncementCursors.TryDecode(cursor, out var decodedAt, out var decodedId).Should().BeTrue();
        decodedAt.Should().Be(at);
        decodedId.Should().Be(id);
        AnnouncementCursors.TryDecode("nope", out _, out _).Should().BeFalse();
    }
}
