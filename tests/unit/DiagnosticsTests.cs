using System.Text.Json.Nodes;
using FluentAssertions;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Feature_off_is_skipped_and_a_failed_database_requires_action()
    {
        var now = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        var checks = DiagnosticEvaluator.Evaluate(new DiagnosticFacts
        {
            DatabaseOk = false,
            RedisConfigured = false,
            EmailEnabled = false,
            PushEnabled = false,
            StorageConfigured = false,
            OidcConfigured = false,
            ProxyEnabled = false,
            MigrationsCurrent = true,
            MigrationId = "20261007000000_AddSupportDiagnostics"
        }, now, "trace-1");

        checks.Single(x => x.Code == "email.configured").Status.Should().Be(DiagnosticStatus.Skipped);
        checks.Single(x => x.Code == "email.configured").Summary.Should().Be("feature.off");
        checks.Single(x => x.Code == "redis.ready").Status.Should().Be(DiagnosticStatus.Skipped);
        checks.Single(x => x.Code == "webpush.configured").Status.Should().Be(DiagnosticStatus.Skipped);
        checks.Single(x => x.Code == "database.ready").Status.Should().Be(DiagnosticStatus.Fail);
        DiagnosticEvaluator.Verdict(checks).Should().Be(DiagnosticVerdict.ActionRequired);
        checks.Should().OnlyContain(x => x.Evidence.Endpoint == null);
    }

    [Fact]
    public void Operator_sees_migration_id_and_workspace_audience_does_not()
    {
        var now = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        var facts = new DiagnosticFacts
        {
            DatabaseOk = true,
            MigrationsCurrent = true,
            MigrationId = "20261007000000_AddSupportDiagnostics",
            Operator = true
        };
        var masked = DiagnosticEvaluator.Evaluate(facts with { Operator = false }, now, "trace-1");
        masked.Single(x => x.Component == "migrations").Evidence.Migration.Should().BeNull();
        var open = DiagnosticEvaluator.Evaluate(facts, now, "trace-1");
        open.Single(x => x.Component == "migrations").Evidence.Migration.Should().Be("20261007000000_AddSupportDiagnostics");
        DiagnosticEvaluator.Verdict(open).Should().Be(DiagnosticVerdict.Ready);
    }

    [Fact]
    public void Redactor_strips_token_connection_email_and_body()
    {
        var node = JsonNode.Parse(
            """
            {
              "body": "Bem-vindo ao VibeChat Demo.",
              "note": "Password=smtp-test-password42 Host=db;Password=vibechat_app_password",
              "mail": "alice@vibechat.local",
              "auth": "Bearer abc.def.ghi",
              "uri": "postgres://app:secret@localhost/vibechat",
              "key": "sk-test-secret-key99"
            }
            """)!;
        SupportRedactor.Scrub(node);
        var text = node.ToJsonString();
        text.Should().NotContain("Bem-vindo");
        text.Should().NotContain("smtp-test-password42");
        text.Should().NotContain("vibechat_app_password");
        text.Should().NotContain("alice@vibechat.local");
        text.Should().NotContain("Bearer");
        text.Should().NotContain("postgres://");
        text.Should().NotContain("sk-test-secret-key99");
        text.Should().Contain("[redacted]");
    }

    [Fact]
    public void Bundle_checksum_is_stable_and_rejects_a_tampered_body()
    {
        var document = Sample();
        var (json, checksum, _) = SupportBundleComposer.Compose(document);
        SupportBundleComposer.TryValidate(json, out var error).Should().BeTrue(error);
        json.Should().Contain(checksum);
        json.Should().NotContain("Password=");
        json.Should().NotContain("alice@");
        var again = SupportBundleComposer.Compose(Sample());
        again.Checksum.Should().Be(checksum);

        var tampered = json.Replace("ready", "degraded", StringComparison.Ordinal);
        SupportBundleComposer.TryValidate(tampered, out _).Should().BeFalse();
    }

    [Fact]
    public void Repair_rejects_unknown_actions_and_permissions_stay_off_the_member_path()
    {
        RepairActions.IsAllowed("search.reindex").Should().BeTrue();
        RepairActions.IsAllowed("membership.reconcile").Should().BeTrue();
        RepairActions.IsAllowed("shell").Should().BeFalse();
        RepairActions.IsAllowed("search.reindex;drop").Should().BeFalse();
        ProbeKinds.IsAllowed("email").Should().BeTrue();
        ProbeKinds.IsAllowed("dns").Should().BeFalse();

        RolePermissionCatalog.For(Role.PlatformOwner).Should().Contain(Permissions.Support.Bundle);
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Support.Repair);
        RolePermissionCatalog.For(Role.Auditor).Should().Contain(Permissions.Support.Read);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Support.Bundle);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Support.Read);
        RolePermissionCatalog.For(Role.Moderator).Should().NotContain(Permissions.Support.Repair);

        var json = File.ReadAllText(FindAppSettings());
        json.Should().Contain("\"SupportBundle\"");
        json.Should().Contain("\"Enabled\": false");
    }

    private static SupportBundleDocument Sample() => new()
    {
        GeneratedAt = DateTimeOffset.Parse("2026-10-07T12:00:00Z"),
        ExpiresAt = DateTimeOffset.Parse("2026-10-07T12:15:00Z"),
        WindowMinutes = 60,
        TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        CorrelationId = "trace-1",
        Audience = "workspace",
        Verdict = DiagnosticVerdict.Ready,
        Environment = "Test",
        Migration = "20261007000000_AddSupportDiagnostics",
        Flags = new Dictionary<string, bool> { ["email"] = false, ["supportBundle"] = true },
        Metrics = new SupportBundleMetrics { OutboxPending = 0, Checks = 1 },
        Checks =
        [
            new SupportBundleCheck
            {
                Code = "email.configured",
                Component = "email",
                Status = DiagnosticStatus.Skipped,
                Severity = DiagnosticSeverity.Info,
                Summary = "Password=smtp-test-password42 alice@vibechat.local",
                Runbook = "email"
            }
        ]
    };

    private static string FindAppSettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "apps", "api", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("appsettings.json");
    }
}
