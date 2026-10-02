using System.Text.Json;
using FluentAssertions;
using VibeChat.Directory;
using VibeChat.Messaging;

namespace VibeChat.UnitTests;

public sealed class WorkspaceTemplateTests
{
    [Fact]
    public void Builtins_cover_team_project_community_and_incidents()
    {
        WorkspaceTemplateRules.Builtins.Select(x => x.Id).Should().Equal(
            WorkspaceTemplateRules.TeamId,
            WorkspaceTemplateRules.ProjectId,
            WorkspaceTemplateRules.CommunityId,
            WorkspaceTemplateRules.IncidentsId);

        var team = WorkspaceTemplateRules.Builtins[0];
        team.Schema.Should().Be(WorkspaceTemplateRules.SchemaV1);
        team.Spaces.Should().ContainSingle(x => x.Name == "Time");
        team.Spaces[0].Channels.Select(x => x.Name).Should().Equal("geral", "avisos");
        team.Spaces[0].Channels[1].Type.Should().Be("Announcement");
        team.Policy!.DeleteWindowMinutes.Should().Be(1440);
        team.Checklist.Should().Equal(OnboardingRules.DefaultChecklist);
    }

    [Fact]
    public void Implicit_policy_matches_messaging_default()
    {
        var policy = WorkspaceTemplateRules.ImplicitPolicy;
        var fallback = MessageLifecyclePolicyRules.Default;
        policy.EditEnabled.Should().Be(fallback.EditEnabled);
        policy.EditWindowMinutes.Should().Be(fallback.EditWindowMinutes);
        policy.EditRolesRestricted.Should().Be(fallback.EditRolesRestricted);
        policy.EditRoles.Should().Equal(fallback.EditRoles);
        policy.EditAllowModeratorOverride.Should().Be(fallback.EditAllowModeratorOverride);
        policy.DeleteEnabled.Should().Be(fallback.DeleteEnabled);
        policy.DeleteWindowMinutes.Should().Be(fallback.DeleteWindowMinutes);
        policy.DeleteRolesRestricted.Should().Be(fallback.DeleteRolesRestricted);
        policy.DeleteRoles.Should().Equal(fallback.DeleteRoles);
        policy.DeleteAllowModeratorOverride.Should().Be(fallback.DeleteAllowModeratorOverride);
    }

    [Theory]
    [InlineData("members")]
    [InlineData("tenantId")]
    [InlineData("secrets")]
    [InlineData("email")]
    public void Import_rejects_unknown_fields(string field)
    {
        var json = $$"""
            {"schema":"{{WorkspaceTemplateRules.SchemaV1}}","id":"squad","version":1,"{{field}}":[],"spaces":[{"key":"squad","name":"Squad","channels":[{"key":"geral","name":"geral","type":"Public"}]}]}
            """;

        var ok = WorkspaceTemplateRules.TryParse(json, out var document, out var error, out var path);

        ok.Should().BeFalse();
        document.Should().BeNull();
        error.Should().Be(WorkspaceTemplateRules.UnknownField);
        path.Should().Be("$." + field);
    }

    [Fact]
    public void Import_rejects_unknown_schema_version()
    {
        const string json = """
            {"schema":"vibechat.workspace-template.v2","id":"squad","version":1,"spaces":[]}
            """;

        var ok = WorkspaceTemplateRules.TryParse(json, out _, out var error, out var path);

        ok.Should().BeFalse();
        error.Should().Be(WorkspaceTemplateRules.UnknownSchema);
        path.Should().Be("$.schema");
    }

    [Fact]
    public void Import_rejects_direct_channels()
    {
        const string json = """
            {"schema":"vibechat.workspace-template.v1","id":"squad","version":1,"spaces":[{"key":"squad","name":"Squad","channels":[{"key":"dm","name":"dm","type":"Direct"}]}]}
            """;

        var ok = WorkspaceTemplateRules.TryParse(json, out _, out var error, out var path);

        ok.Should().BeFalse();
        error.Should().Be(WorkspaceTemplateRules.Invalid);
        path.Should().Be("$.spaces[0].channels[0].type");
    }

    [Fact]
    public void Plan_reuses_matching_structure_and_conflicts_on_type()
    {
        var team = WorkspaceTemplateRules.Builtins.Single(x => x.Id == WorkspaceTemplateRules.TeamId);
        var empty = WorkspaceTemplateRules.Plan(team, [], [], null);
        empty.Should().Contain(x => x.Kind == TemplatePlanKinds.Space && x.Action == TemplatePlanActions.Create && x.Name == "Time");
        empty.Should().Contain(x => x.Kind == TemplatePlanKinds.Channel && x.Action == TemplatePlanActions.Create && x.Name == "avisos");
        WorkspaceTemplateRules.HasConflicts(empty).Should().BeFalse();

        var spaceId = Guid.NewGuid();
        var again = WorkspaceTemplateRules.Plan(
            team,
            [new TemplateExistingSpace(spaceId, "Time")],
            [
                new TemplateExistingChannel(Guid.NewGuid(), spaceId, "geral", "Public", "Conversas do time"),
                new TemplateExistingChannel(Guid.NewGuid(), spaceId, "avisos", "Announcement", "Avisos do time")
            ],
            new TemplateExistingPolicy(true, null, false, [], false, true, 1440, false, [], true));
        again.Should().OnlyContain(x => x.Action == TemplatePlanActions.Reuse);

        var conflict = WorkspaceTemplateRules.Plan(
            team,
            [new TemplateExistingSpace(spaceId, "Outro")],
            [new TemplateExistingChannel(Guid.NewGuid(), spaceId, "geral", "Private", null)],
            null);
        conflict.Should().Contain(x => x.Name == "geral" && x.Action == TemplatePlanActions.Conflict && x.Detail == TemplateConflictDetails.Type);
        WorkspaceTemplateRules.HasConflicts(conflict).Should().BeTrue();
    }

    [Fact]
    public void Export_omits_members_secrets_and_tenant_and_roundtrips()
    {
        var spaceId = Guid.NewGuid();
        var ok = WorkspaceTemplateRules.TryExport(
            [new TemplateExistingSpace(spaceId, "Time")],
            [
                new TemplateExistingChannel(Guid.NewGuid(), spaceId, "geral", "Public", "Conversas do time"),
                new TemplateExistingChannel(Guid.NewGuid(), null, "alice", "Direct", null)
            ],
            null,
            out var json,
            out var error);

        ok.Should().BeTrue(error);
        json.Should().NotBeNull();
        var names = WorkspaceTemplateRules.PropertyNames(json!);
        names.Should().NotContain(["tenantId", "TenantId", "members", "secrets", "email", "token", "userId"]);
        json.Should().NotContain("Direct");
        json.Should().Contain("Conversas do time");

        WorkspaceTemplateRules.TryParse(json, out var document, out var parseError, out _).Should().BeTrue(parseError);
        document!.Id.Should().Be(WorkspaceTemplateRules.ExportId);
        document.Spaces.Should().ContainSingle();
        document.Spaces[0].Channels.Should().ContainSingle(x => x.Name == "geral");
    }

    [Fact]
    public void Canonical_builtin_has_no_personal_fields()
    {
        foreach (var builtin in WorkspaceTemplateRules.Builtins)
        {
            using var document = JsonDocument.Parse(builtin.CanonicalJson);
            document.RootElement.TryGetProperty("tenantId", out _).Should().BeFalse();
            WorkspaceTemplateRules.PropertyNames(builtin.CanonicalJson)
                .Should().NotContain(["members", "secrets", "email", "token", "tenantId"]);
        }
    }
}
