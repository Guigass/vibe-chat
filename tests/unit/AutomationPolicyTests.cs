using System.Text.Json;
using FluentAssertions;
using VibeChat.BuildingBlocks;
using VibeChat.Integrations;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class AutomationPolicyTests
{
    [Fact]
    public void Definition_rejects_secret_values_unknown_fields_and_capability_mismatch()
    {
        AutomationPolicies.TryParse(Definition("token=vc_auto_should_not_live_here"), out _, out var secret).Should().BeFalse();
        secret.Should().Be("SecretValueNotAllowed");

        var trimmed = ValidJson().Trim();
        var unknown = trimmed[..^1] + ", \"script\": \"eval\" }";
        AutomationPolicies.TryParse(unknown, out _, out var extra).Should().BeFalse();
        extra.Should().Be("InvalidDefinition");

        var mismatch = ValidJson().Replace("\"capability\": \"message.send\"", "\"capability\": \"workspace.admin\"", StringComparison.Ordinal);
        AutomationPolicies.TryParse(mismatch, out _, out var capability).Should().BeFalse();
        capability.Should().Be("CapabilityMismatch");
    }

    [Fact]
    public void Published_json_keeps_only_the_allowlist_and_redacts_logs()
    {
        AutomationPolicies.TryParse(ValidJson(), out var model, out var error).Should().BeTrue(error);
        var stored = model!.ToJson();
        stored.Should().Contain("automation.definition.v1");
        stored.Should().NotContain("password");
        stored.Should().NotContain("vc_auto_");

        var log = AutomationPolicies.StepLog("post_message", "succeeded", null, Guid.NewGuid());
        log.Should().NotContain("vc_auto_");
        AutomationPolicies.Redact("token=vc_auto_abc123 Bearer raw").Should().NotContain("vc_auto_abc123");
        AutomationPolicies.Redact("password=hunter2").Should().Contain("[redacted]").And.NotContain("hunter2");
    }

    [Fact]
    public void Retry_key_is_stable_and_admission_stops_loops_rate_and_open_circuit()
    {
        var id = Guid.NewGuid();
        AutomationPolicies.StepKey(id, 2, "evt", 1).Should().Be(AutomationPolicies.StepKey(id, 2, "evt", 1));
        AutomationPolicies.StepKey(id, 2, "evt", 1).Should().NotBe(AutomationPolicies.StepKey(id, 2, "evt", 2));

        var now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
        AutomationPolicies.TryAdmit(true, false, null, 0, 0, now, out var disabled).Should().BeFalse();
        disabled.Should().Be("AutomationDisabled");
        AutomationPolicies.TryAdmit(true, true, now.AddMinutes(1), 0, 0, now, out var circuit).Should().BeFalse();
        circuit.Should().Be("CircuitOpen");
        AutomationPolicies.TryAdmit(true, true, null, AutomationPolicies.MaxRunsPerHour, 0, now, out var rate).Should().BeFalse();
        rate.Should().Be("RateLimited");
        AutomationPolicies.TryAdmit(true, true, null, 0, AutomationPolicies.MaxDepth, now, out var loop).Should().BeFalse();
        loop.Should().Be("LoopStopped");
        AutomationPolicies.TryAdmit(true, true, null, 0, AutomationPolicies.MaxDepth - 1, now, out _).Should().BeTrue();
    }

    [Fact]
    public void Conditions_and_owner_intersection_fail_closed()
    {
        AutomationPolicies.TryParse(ValidJson(), out var model, out _).Should().BeTrue();
        var signal = new AutomationSignal("message", "created", "evt", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null, null, null, "created", 0, null);
        AutomationPolicies.Matches(model!, signal).Should().BeTrue();
        AutomationPolicies.ConditionsPass(model!, signal).Should().BeTrue();
        AutomationPolicies.ConditionsPass(model!, signal with { State = "other" }).Should().BeFalse();

        var owner = RolePermissionCatalog.For(Role.Member);
        var requested = model!.Capabilities();
        requested.Should().Contain(Permissions.Message.Send);
        AutomationPolicies.Intersect(owner, requested).Should().NotContain(Permissions.Automation.Notify);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Automation.Manage);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Automation.Manage);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Automation.Manage);
    }

    [Fact]
    public void Outbox_signal_drops_body_and_secret_fields()
    {
        var messageId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            messageId,
            channelId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            body = "token=vc_auto_supersecret",
            secret = "vc_auto_supersecret",
            authorId = Guid.NewGuid(),
        });
        AutomationPolicies.TryReadSignal("MessageCreatedEvent", payload, out var signal).Should().BeTrue();
        var stored = signal!.ToJson();
        stored.Should().NotContain("supersecret");
        stored.Should().NotContain("vc_auto_");
        stored.Should().Contain(messageId.ToString());
    }

    private static string ValidJson() => Definition("aviso");

    private static string Definition(string body) =>
        $$"""
        {
          "contractVersion": "automation.definition.v1",
          "trigger": { "type": "message", "channelIds": [], "formIds": [], "event": "created", "intervalMinutes": 0 },
          "conditions": [ { "field": "state", "op": "eq", "value": "created" } ],
          "actions": [ {
            "type": "post_message",
            "capability": "message.send",
            "channelId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "body": "{{body}}",
            "title": "",
            "taskId": null,
            "state": "",
            "userIds": [],
            "connectorId": null
          } ],
          "channelIds": [ "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" ],
          "secretRefs": [ "smtp" ]
        }
        """;
}
