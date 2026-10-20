using FluentAssertions;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class DlpRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorkspaceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SpaceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ChannelId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid DetectorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid SubjectId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public void Flag_and_capability_stay_off_the_admin_role()
    {
        new DlpOptions().Enabled.Should().BeFalse();
        RolePermissionCatalog.For(Role.Admin).Should().NotContain(Permissions.Compliance.Dlp);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().NotContain(Permissions.Compliance.Dlp);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Compliance.Dlp);
        ComplianceCapabilities.Known("dlp").Should().BeTrue();
    }

    [Fact]
    public void Label_inherits_workspace_then_space_then_channel()
    {
        var assignments = new DlpAssignmentInput[]
        {
            new(DlpScopes.Workspace, WorkspaceId, DlpLabels.Internal),
            new(DlpScopes.Space, SpaceId, DlpLabels.Confidential),
            new(DlpScopes.Channel, ChannelId, DlpLabels.Restricted)
        };

        DlpRules.ResolveLabel(assignments, WorkspaceId, SpaceId, ChannelId).Should().Be(DlpLabels.Restricted);
        DlpRules.ResolveLabel(assignments, WorkspaceId, SpaceId, Guid.NewGuid()).Should().Be(DlpLabels.Confidential);
        DlpRules.ResolveLabel(assignments, WorkspaceId, Guid.NewGuid(), Guid.NewGuid()).Should().Be(DlpLabels.Internal);
        DlpRules.ResolveLabel([], WorkspaceId, null, null).Should().BeNull();
    }

    [Fact]
    public void Override_cannot_lower_the_parent_rank()
    {
        var assignments = new DlpAssignmentInput[]
        {
            new(DlpScopes.Workspace, WorkspaceId, DlpLabels.Confidential)
        };
        var ranks = new Dictionary<string, int>();
        var parent = DlpRules.ParentRank(assignments, ranks, DlpScopes.Channel, WorkspaceId, null);
        DlpRules.IsDowngrade(parent, 1).Should().BeTrue();
        DlpRules.IsDowngrade(parent, 3).Should().BeFalse();
        DlpRules.IsDowngrade(null, 0).Should().BeFalse();
    }

    [Theory]
    [InlineData(DlpActions.Share)]
    [InlineData(DlpActions.Guest)]
    [InlineData(DlpActions.Export)]
    [InlineData(DlpActions.Webhook)]
    [InlineData(DlpActions.Ai)]
    [InlineData(DlpActions.Download)]
    [InlineData(DlpActions.Federation)]
    public void Block_stops_the_action_unless_simulation_or_exception(string action)
    {
        var blocked = Evaluate(action, simulation: false, expiresAt: Now.AddMinutes(-1));
        blocked.Proceed.Should().BeFalse();
        blocked.Effect.Should().Be(DlpEffects.Block);
        blocked.ReasonCode.Should().Be(DlpReasons.Block);

        var simulated = Evaluate(action, simulation: true, expiresAt: Now.AddMinutes(-1));
        simulated.Proceed.Should().BeTrue();
        simulated.Simulation.Should().BeTrue();
        simulated.Effect.Should().Be(DlpEffects.Block);
        simulated.ReasonCode.Should().Be(DlpReasons.Simulated);
        simulated.Recorded.Should().BeTrue();

        var excused = Evaluate(action, simulation: false, expiresAt: Now.AddDays(1));
        excused.Proceed.Should().BeTrue();
        excused.ReasonCode.Should().Be(DlpReasons.Exception);
    }

    [Fact]
    public void Detector_counts_matches_without_keeping_the_text_and_false_positive_skips_it()
    {
        const string body = "card 4111 and 4222";
        DlpRules.CountMatches(@"\d{4}", body).Should().Be(2);
        DlpRules.TryCompile("(", out var error).Should().BeFalse();
        error.Should().Be(DlpErrors.PatternInvalid);

        var raised = Evaluate(DlpActions.Share, simulation: false, expiresAt: Now.AddMinutes(-1), body: body, rank: 3, label: DlpLabels.Public);
        raised.MatchCount.Should().Be(2);
        raised.Rank.Should().Be(3);
        raised.Proceed.Should().BeFalse();

        var reviewed = Evaluate(
            DlpActions.Share,
            simulation: false,
            expiresAt: Now.AddMinutes(-1),
            body: body,
            rank: 3,
            label: DlpLabels.Public,
            falsePositives: [new DlpFalsePositiveInput(DetectorId, SubjectId)]);
        reviewed.MatchCount.Should().Be(0);
        reviewed.Proceed.Should().BeTrue();
    }

    [Fact]
    public void E2ee_is_not_inspectable_and_does_not_raise_rank_from_the_body()
    {
        var outcome = Evaluate(
            DlpActions.Ai,
            simulation: false,
            expiresAt: Now.AddMinutes(-1),
            body: "4111",
            rank: 3,
            label: DlpLabels.Public,
            channelType: ContentAuditRules.WireType,
            contentAuditEnabled: true);
        outcome.Inspectable.Should().BeFalse();
        outcome.MatchCount.Should().Be(0);
        outcome.ReasonCode.Should().Be(DlpReasons.NotInspectable);
        outcome.Proceed.Should().BeTrue();
    }

    [Fact]
    public void Quarantine_does_not_proceed_outside_simulation()
    {
        var outcome = Evaluate(DlpActions.Download, simulation: false, expiresAt: Now.AddMinutes(-1), effect: DlpEffects.Quarantine);
        outcome.Proceed.Should().BeFalse();
        outcome.Effect.Should().Be(DlpEffects.Quarantine);
        outcome.ReasonCode.Should().Be(DlpReasons.Quarantine);
    }

    [Fact]
    public void Exception_must_be_bounded()
    {
        DlpRules.ValidExceptionWindow(Now, Now.AddDays(30)).Should().BeTrue();
        DlpRules.ValidExceptionWindow(Now, Now.AddDays(31)).Should().BeFalse();
        DlpRules.ValidExceptionWindow(Now, Now).Should().BeFalse();
        DlpRules.ValidCustomKey("card-pan").Should().BeTrue();
        DlpRules.ValidCustomKey("public").Should().BeFalse();
    }

    private static DlpOutcome Evaluate(
        string action,
        bool simulation,
        DateTimeOffset expiresAt,
        string? body = null,
        int rank = 3,
        string? channelType = null,
        bool contentAuditEnabled = true,
        string effect = DlpEffects.Block,
        string label = DlpLabels.Restricted,
        IReadOnlyList<DlpFalsePositiveInput>? falsePositives = null) =>
        DlpRules.Evaluate(
            action,
            simulation,
            contentAuditEnabled,
            channelType,
            WorkspaceId,
            SpaceId,
            ChannelId,
            body,
            SubjectId,
            [new DlpAssignmentInput(DlpScopes.Channel, ChannelId, label)],
            new Dictionary<string, int>(),
            [new DlpRuleInput(action, rank, effect)],
            [new DlpDetectorInput(DetectorId, @"\d{4}", 3, true)],
            [new DlpExceptionInput(action, DlpScopes.Channel, ChannelId, expiresAt, false)],
            falsePositives ?? [],
            Now);
}
