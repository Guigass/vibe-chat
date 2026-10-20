using System.Text.Json.Nodes;
using FluentAssertions;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Integrations;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class SiemRulesTests
{
    [Fact]
    public void Export_permission_is_not_on_a_role()
    {
        RolePermissionCatalog.For(Role.Admin).Should().NotContain(Permissions.Security.AuditExport);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().NotContain(Permissions.Security.AuditExport);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Security.AuditExport);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Security.AuditExport);
    }

    [Fact]
    public void Flag_defaults_off()
    {
        new SiemOptions().Enabled.Should().BeFalse();
        var json = File.ReadAllText(FindRepoFile(Path.Combine("apps", "api", "appsettings.json")));
        json.Should().Contain("\"Siem\"");
        json.Should().Contain("\"Enabled\": false");
    }

    [Fact]
    public void Redaction_drops_body_and_secret_and_the_catalog_rejects_a_body_property()
    {
        var metadata = """{"body":"hello secret","token":"abc","note":"kept","nested":{"password":"p"}}""";
        var redacted = SiemRules.RedactMetadata(metadata, minimize: false, secret: "abc");
        redacted.Should().NotBeNull();
        redacted!.ToJsonString().Should().NotContain("hello").And.NotContain("password").And.NotContain("abc");
        redacted["note"]!.GetValue<string>().Should().Be("kept");
        SiemRules.RedactMetadata(metadata, minimize: true, secret: null).Should().BeNull();

        var envelope = new SiemEnvelope(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DateTimeOffset.Parse("2026-10-20T12:00:00.0000000+00:00"),
            "message",
            SiemSeverities.Info,
            "message.send",
            false,
            null,
            null,
            null,
            redacted);
        var json = SiemRules.BuildJson(envelope);
        json.Should().NotContain("hello").And.NotContain("body");
        PublicEventCatalog.Accept(SiemLimits.EventType, json).Accepted.Should().BeTrue();

        var withBody = JsonNode.Parse(json)!.AsObject();
        withBody["body"] = "hello";
        PublicEventCatalog.Accept(SiemLimits.EventType, withBody.ToJsonString()).Accepted.Should().BeFalse();

        var cef = SiemRules.BuildCef(envelope);
        cef.Should().StartWith("CEF:0|VibeChat|Audit|1|").And.NotContain("hello").And.Contain(envelope.EventId.ToString("D"));
    }

    [Fact]
    public void Filter_backfill_and_circuit_follow_the_policy()
    {
        SiemRules.Allows(["message"], SiemSeverities.High, "message", SiemSeverities.Info).Should().BeFalse();
        SiemRules.Allows(["message"], SiemSeverities.High, "message", SiemSeverities.High).Should().BeTrue();
        SiemRules.Allows([], SiemSeverities.Info, "member", SiemSeverities.Low).Should().BeTrue();

        var now = DateTimeOffset.Parse("2026-10-20T12:00:00Z");
        SiemRules.TryBackfill(now.AddDays(-3), 2, now, out var start, out var take, out var error).Should().BeTrue();
        error.Should().BeEmpty();
        take.Should().Be(2);
        start.Should().Be(now.AddHours(-SiemLimits.MaxBackfillHours));
        SiemRules.TryBackfill(now, 0, now, out _, out _, out error).Should().BeFalse();
        error.Should().Be(SiemErrors.BackfillInvalid);

        var invalid = SiemRules.Record(404, 0, now);
        invalid.Circuit.Should().Be(SiemCircuits.Open);
        var transient = SiemRules.Record(503, 0, now);
        transient.Circuit.Should().Be(SiemCircuits.Closed);
        transient.Failures.Should().Be(1);
        SiemRules.Record(503, 2, now).Circuit.Should().Be(SiemCircuits.Open);
        SiemRules.Record(204, 2, now).Circuit.Should().Be(SiemCircuits.Closed);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
