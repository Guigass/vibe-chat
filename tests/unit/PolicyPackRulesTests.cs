using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class PolicyPackRulesTests
{
    [Fact]
    public void Packs_are_a_technical_baseline_and_stay_off_the_admin_role()
    {
        new PolicyPackOptions().Enabled.Should().BeFalse();
        RolePermissionCatalog.For(Role.Admin).Should().NotContain(Permissions.Compliance.Policy);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().NotContain(Permissions.Compliance.Policy);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Compliance.Policy);
        ComplianceCapabilities.Known("policy").Should().BeTrue();
        PolicyPackRules.Catalog().Select(x => x.Id).Should().Equal(
            PolicyPackIds.SmallTeam,
            PolicyPackIds.EnterpriseBaseline,
            PolicyPackIds.Regulated);
    }

    [Fact]
    public void Golden_manifests_cover_the_closed_control_set_and_lock_audit()
    {
        foreach (var pack in PolicyPackRules.Catalog())
        {
            PolicyPackRules.TryResolve(pack.Id, pack.Version, default, 10, out var canonical, out var overrides, out var error)
                .Should().BeTrue(error);
            overrides.Should().Be("{}");
            using var document = JsonDocument.Parse(canonical);
            document.RootElement.GetProperty("schema").GetString().Should().Be(PolicyPackSchema.Name);
            document.RootElement.GetProperty("baseline").GetString().Should().Be("technical");
            document.RootElement.GetProperty("certification").GetBoolean().Should().BeFalse();
            document.RootElement.GetProperty("label").GetString().Should().Be(pack.Label);
            var controls = PolicyPackRules.ReadControls(canonical);
            controls.Select(x => x.Key).Should().Equal(PolicyPackRules.Specs.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));
            controls.Single(x => x.Key == PolicyPackControls.AuditContentEnabled).Flag.Should().BeTrue();
            controls.Single(x => x.Key == PolicyPackControls.AuditContentEnabled).Locked.Should().BeTrue();
            canonical.Should().NotContain("secret").And.NotContain("tenantId");
        }
    }

    [Fact]
    public void Preview_is_stable_and_a_weaker_locked_override_fails_closed()
    {
        using var stricter = JsonDocument.Parse("""{"uploads.maxMb":4}""");
        PolicyPackRules.TryResolve(PolicyPackIds.Regulated, 1, stricter.RootElement, 10, out var first, out _, out _)
            .Should().BeTrue();
        PolicyPackRules.TryResolve(PolicyPackIds.Regulated, 1, stricter.RootElement, 10, out var second, out _, out _)
            .Should().BeTrue();
        second.Should().Be(first);
        PolicyPackRules.ReadControls(first).Single(x => x.Key == PolicyPackControls.UploadsMaxMb).Number.Should().Be(4);

        using var weaker = JsonDocument.Parse("""{"guests.enabled":true}""");
        PolicyPackRules.TryResolve(PolicyPackIds.EnterpriseBaseline, 1, weaker.RootElement, 10, out _, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(PolicyPackErrors.OverrideForbidden);

        using var auditOff = JsonDocument.Parse("""{"audit.contentEnabled":false}""");
        PolicyPackRules.TryResolve(PolicyPackIds.SmallTeam, 1, auditOff.RootElement, 10, out _, out _, out var auditError)
            .Should().BeFalse();
        auditError.Should().Be(PolicyPackErrors.OverrideForbidden);
    }

    [Fact]
    public void Import_rejects_tenant_ids_secrets_and_code_and_export_redacts_them()
    {
        using var malicious = JsonDocument.Parse("""
            {
              "schema": "policy-pack.v1",
              "packId": "small-team",
              "version": 1,
              "tenantId": "11111111-1111-1111-1111-111111111111",
              "secret": "super-secret-value",
              "script": "eval(atob('a'))"
            }
            """);
        PolicyPackRules.TryReadImport(malicious.RootElement, out _, out _, out _, out var error).Should().BeFalse();
        error.Should().Be(PolicyPackErrors.ImportRejected);

        var node = JsonNode.Parse("""{"schema":"policy-pack.v1","secret":"super-secret-value","packId":"small-team"}""")!;
        var redacted = PolicyPackRules.Redact(node);
        redacted.Should().NotContain("super-secret-value").And.NotContain("secret");
        redacted.Should().Contain("small-team");
    }

    [Fact]
    public void Version_upgrade_is_explicit_and_guests_off_blocks()
    {
        PolicyPackRules.UpgradeAvailable(1, 1).Should().BeFalse();
        PolicyPackRules.UpgradeAvailable(1, 2).Should().BeTrue();
        PolicyPackRules.TryResolve(PolicyPackIds.SmallTeam, 2, default, 10, out _, out _, out var error).Should().BeFalse();
        error.Should().Be(PolicyPackErrors.VersionUnknown);

        PolicyPackRules.TryResolve(PolicyPackIds.EnterpriseBaseline, 1, default, 10, out var canonical, out _, out _).Should().BeTrue();
        PolicyPackRules.BlocksGuests(canonical).Should().BeTrue();
        PolicyPackRules.TryResolve(PolicyPackIds.SmallTeam, 1, default, 10, out var open, out _, out _).Should().BeTrue();
        PolicyPackRules.BlocksGuests(open).Should().BeFalse();
    }
}
