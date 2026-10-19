using FluentAssertions;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class LegalHoldRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();

    [Fact]
    public void Legal_hold_is_off_by_default_and_separated_from_workspace_admin()
    {
        new LegalHoldOptions().Enabled.Should().BeFalse();
        RolePermissionCatalog.For(Role.Admin).Should().NotContain(Permissions.Compliance.Hold);
        RolePermissionCatalog.For(Role.Admin).Should().NotContain(Permissions.Compliance.Discovery);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().NotContain(Permissions.Compliance.Hold);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Compliance.Discovery);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Compliance.Hold);
    }

    [Fact]
    public void Active_channel_hold_preserves_only_that_channel()
    {
        var subject = new HoldSubject(WorkspaceId, ChannelId, ChannelId, AuthorId, Now.AddDays(-1));
        var other = subject with { ChannelId = Guid.NewGuid(), ConversationId = Guid.NewGuid() };

        LegalHoldRules.IsCovering(LegalHoldStatus.Active, null, WorkspaceId, LegalHoldScope.Channel, ChannelId, null, null, subject, Now)
            .Should().BeTrue();
        LegalHoldRules.IsCovering(LegalHoldStatus.Active, null, WorkspaceId, LegalHoldScope.Channel, ChannelId, null, null, other, Now)
            .Should().BeFalse();
    }

    [Fact]
    public void Released_or_expired_hold_does_not_block_purge()
    {
        var subject = new HoldSubject(WorkspaceId, ChannelId, ChannelId, AuthorId, Now);
        LegalHoldRules.IsCovering(LegalHoldStatus.Released, null, WorkspaceId, LegalHoldScope.Workspace, null, null, null, subject, Now)
            .Should().BeFalse();
        LegalHoldRules.IsCovering(LegalHoldStatus.Active, Now, WorkspaceId, LegalHoldScope.Workspace, null, null, null, subject, Now)
            .Should().BeFalse();
        LegalHoldRules.IsCovering(LegalHoldStatus.Active, Now.AddMinutes(1), WorkspaceId, LegalHoldScope.Workspace, null, null, null, subject, Now)
            .Should().BeTrue();
    }

    [Fact]
    public void Interval_user_and_conversation_scopes_match_their_subject()
    {
        var subject = new HoldSubject(WorkspaceId, ChannelId, Guid.NewGuid(), AuthorId, Now);
        LegalHoldRules.IsCovering(LegalHoldStatus.Active, null, WorkspaceId, LegalHoldScope.User, AuthorId, null, null, subject, Now)
            .Should().BeTrue();
        LegalHoldRules.IsCovering(LegalHoldStatus.Active, null, WorkspaceId, LegalHoldScope.User, Guid.NewGuid(), null, null, subject, Now)
            .Should().BeFalse();
        LegalHoldRules.IsCovering(LegalHoldStatus.Active, null, WorkspaceId, LegalHoldScope.Conversation, subject.ConversationId, null, null, subject, Now)
            .Should().BeTrue();
        LegalHoldRules.IsCovering(
                LegalHoldStatus.Active,
                null,
                WorkspaceId,
                LegalHoldScope.Interval,
                null,
                Now.AddHours(-1),
                Now.AddHours(1),
                subject,
                Now)
            .Should().BeTrue();
        LegalHoldRules.IsCovering(
                LegalHoldStatus.Active,
                null,
                WorkspaceId,
                LegalHoldScope.Interval,
                null,
                Now.AddHours(1),
                Now.AddHours(2),
                subject,
                Now)
            .Should().BeFalse();
    }

    [Fact]
    public void Unknown_active_scope_fails_closed_and_approval_cannot_be_the_same_person()
    {
        var subject = new HoldSubject(WorkspaceId, ChannelId, ChannelId, AuthorId, Now);
        LegalHoldRules.IsCovering(LegalHoldStatus.Active, null, WorkspaceId, "other", null, null, null, subject, Now)
            .Should().BeTrue();
        LegalHoldRules.CanApprove(AuthorId, AuthorId).Should().BeFalse();
        LegalHoldRules.CanApprove(AuthorId, Guid.NewGuid()).Should().BeTrue();
        LegalHoldRules.Confirmed(true, false).Should().BeFalse();
        LegalHoldRules.Confirmed(true, true).Should().BeTrue();
    }

    [Fact]
    public void Custody_hash_chains_from_the_previous_payload()
    {
        var first = CustodyChain.Payload(CustodyActions.HoldRequest, AuthorId, Guid.NewGuid(), null, null, "investigacao", Now);
        var firstHash = CustodyChain.Hash(string.Empty, first);
        var second = CustodyChain.Payload(CustodyActions.HoldApprove, Guid.NewGuid(), Guid.NewGuid(), null, null, "investigacao", Now.AddMinutes(1));
        var secondHash = CustodyChain.Hash(firstHash, second);

        secondHash.Should().NotBe(firstHash);
        secondHash.Should().HaveLength(64);
        CustodyChain.Sha256Hex("abc").Should().Be(CustodyChain.Sha256Hex("abc"));
    }
}
