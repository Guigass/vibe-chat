using FluentAssertions;
using VibeChat.BuildingBlocks;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class IncidentPolicyTests
{
    [Fact]
    public void Transitions_follow_declare_mitigate_resolve()
    {
        IncidentPolicies.TryTransition(IncidentPolicies.Declared, IncidentPolicies.Mitigating).Should().BeTrue();
        IncidentPolicies.TryTransition(IncidentPolicies.Mitigating, IncidentPolicies.Resolved).Should().BeTrue();
        IncidentPolicies.TryTransition(IncidentPolicies.Declared, IncidentPolicies.Resolved).Should().BeFalse();
        IncidentPolicies.TryTransition(IncidentPolicies.Resolved, IncidentPolicies.Mitigating).Should().BeFalse();
        IncidentPolicies.TryTransition(IncidentPolicies.Mitigating, IncidentPolicies.Declared).Should().BeFalse();
        IncidentPolicies.TryTransition(IncidentPolicies.Declared, IncidentPolicies.Declared).Should().BeFalse();
    }

    [Fact]
    public void Template_is_allowlisted_and_postmortem_keeps_references_only()
    {
        IncidentPolicies.TemplateSteps("default").Should().Equal(
            IncidentPolicies.StepAcknowledge,
            IncidentPolicies.StepMitigate,
            IncidentPolicies.StepCommunicate,
            IncidentPolicies.StepPostmortem);
        IncidentPolicies.TemplateSteps("script").Should().BeEmpty();

        var room = new IncidentRoom
        {
            Id = Guid.NewGuid(),
            ChannelId = ChannelId.New(),
            Title = "Sala",
            Severity = "sev2",
            Status = IncidentPolicies.Resolved,
            CorrelationId = "alert-1",
            DeclaredAt = DateTimeOffset.Parse("2026-10-17T12:00:00Z"),
            ResolvedAt = DateTimeOffset.Parse("2026-10-17T12:10:00Z"),
        };
        var source = Guid.NewGuid();
        var json = IncidentPolicies.BuildPostmortem(
            room,
            [new IncidentPlaybookStep { StepIndex = 0, Code = IncidentPolicies.StepAcknowledge, Status = IncidentPolicies.StepPending }],
            [new IncidentTimelineEntry { Kind = IncidentPolicies.KindMessage, SourceId = source, CreatedAt = room.DeclaredAt }]);
        json.Should().Contain(source.ToString());
        json.Should().Contain("Sala");
        json.Should().NotContain("Bem-vindo ao VibeChat");
    }

    [Fact]
    public void Incident_permissions_stay_off_member_and_do_not_replace_workspace_admin()
    {
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Incident.Manage);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Incident.Export);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Incident.Manage);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Incident.Export);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Incident.Export);
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Incident.Manage);
        Permissions.Incident.Manage.Should().NotBe(Permissions.Workspace.Admin);
    }
}
