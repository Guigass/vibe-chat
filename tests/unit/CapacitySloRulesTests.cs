using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Infrastructure;

namespace VibeChat.UnitTests;

public sealed class CapacitySloRulesTests
{
    [Fact]
    public void Flag_is_off_and_the_dashboard_stays_closed()
    {
        new CapacitySloOptions().Enabled.Should().BeFalse();
        var service = new CapacitySloService(Options.Create(new CapacitySloOptions()));
        var closed = service.Dashboard();
        closed.Error.Should().Be(CapacitySloErrors.Disabled);
        closed.StatusCode.Should().Be(404);
        service.Evaluate("{}").Error.Should().Be(CapacitySloErrors.Disabled);
    }

    [Fact]
    public void Profiles_recovery_and_slos_match_d25_and_d28_without_becoming_an_sla()
    {
        var dashboard = CapacitySloRules.Dashboard();
        dashboard.IsSla.Should().BeFalse();
        dashboard.SampleKind.Should().Be(CapacitySloRules.SampleUnmeasured);
        dashboard.ResultFormat.Should().Be(CapacitySloRules.ResultFormat);
        dashboard.Profiles.Select(profile => profile.Id).Should().Equal("small", "standard", "large");
        dashboard.Profiles.Should().OnlyContain(profile =>
            profile.Users > 0
            && profile.Concurrency > 0
            && profile.ConnectionsPerNode > 0
            && profile.MessagesPerSecond > 0
            && profile.LargestChannel > 0
            && profile.FanOut > 0
            && profile.StorageGb > 0
            && profile.RetentionDays > 0
            && profile.Tenants > 0);

        var standard = dashboard.Recovery.Single(profile => profile.Id == "standard");
        standard.Availability.Should().Be(0.999m);
        standard.RpoMinutes.Should().Be(60);
        standard.RtoMinutes.Should().Be(240);
        standard.Current.Should().BeTrue();

        var basic = dashboard.Recovery.Single(profile => profile.Id == "basic");
        basic.Availability.Should().BeNull();
        basic.RpoMinutes.Should().Be(1440);
        basic.RtoMinutes.Should().Be(240);
        basic.BestEffort.Should().BeTrue();

        var ha = dashboard.Recovery.Single(profile => profile.Id == "ha");
        ha.Availability.Should().Be(0.9995m);
        ha.RpoMinutes.Should().Be(5);
        ha.RtoMinutes.Should().Be(30);
        ha.Current.Should().BeFalse();
        ha.AvailableAfter.Should().Be("B-144");

        dashboard.Slis.Single(sli => sli.Id == "api-availability").Objective.Should().Be(0.999m);
        dashboard.Slis.Single(sli => sli.Id == "outbox-lag").Threshold.Should().Be(5000m);
        dashboard.Slis.Single(sli => sli.Id == "search-lag").Threshold.Should().Be(10000m);
        dashboard.Slis.Single(sli => sli.Id == "reconnect").Threshold.Should().Be(3000m);
        dashboard.Slis.Should().OnlyContain(sli => sli.BurnState == "unmeasured" && sli.BurnRate == null);
        dashboard.Workloads.Should().Equal("send", "history", "search", "upload", "reconnect", "admin", "isolation");
    }

    [Fact]
    public void Burn_rate_uses_the_error_budget_and_adr_triggers_have_numbers()
    {
        var availability = CapacitySloRules.Slis.Single(sli => sli.Id == "api-availability");
        CapacitySloRules.Burn(availability, 0.999m).Should().Be(1m);
        CapacitySloRules.Burn(availability, 1m).Should().Be(0m);
        CapacitySloRules.Burn(availability, 0.99m).Should().Be(10m);
        CapacitySloRules.BurnState(1m).Should().Be("within");
        CapacitySloRules.BurnState(1.0001m).Should().Be("burning");
        CapacitySloRules.BurnState(null).Should().Be("unmeasured");

        var echo = CapacitySloRules.Slis.Single(sli => sli.Id == "message-echo");
        CapacitySloRules.Burn(echo, 1000m).Should().Be(1m);
        CapacitySloRules.Burn(echo, 2000m).Should().Be(2m);

        var bus = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-015-outbox");
        bus.Adr.Should().Be("ADR-015");
        bus.Threshold.Should().Be(30000m);
        bus.WindowMinutes.Should().Be(15);
        bus.MinWorkerReplicas.Should().Be(2);
        CapacitySloRules.IsMet(bus, 30001m, 15, 2, 0, false).Should().BeTrue();
        CapacitySloRules.IsMet(bus, 30001m, 14, 2, 0, false).Should().BeFalse();
        CapacitySloRules.IsMet(bus, 30001m, 15, 1, 0, false).Should().BeFalse();

        var search = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-016-search-query");
        search.Threshold.Should().Be(500m);
        search.MinIndexedMessages.Should().Be(5_000_000);
        CapacitySloRules.IsMet(search, 501m, 15, 0, 5_000_000, false).Should().BeTrue();
        CapacitySloRules.IsMet(search, 501m, 15, 0, 4_999_999, false).Should().BeFalse();

        var cpu = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-016-search-cpu");
        cpu.Threshold.Should().Be(0.40m);
        CapacitySloRules.IsMet(cpu, 0.41m, 15, 0, 0, false).Should().BeTrue();

        var replicas = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-017-replicas");
        replicas.Threshold.Should().Be(3m);
        CapacitySloRules.IsMet(replicas, 4m, 15, 0, 0, false).Should().BeTrue();
        CapacitySloRules.IsMet(replicas, 3m, 15, 0, 0, false).Should().BeFalse();

        var connections = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-017-connections");
        connections.Threshold.Should().Be(2000m);
        connections.RequiresRollingRestartGap.Should().BeTrue();
        CapacitySloRules.IsMet(connections, 2001m, 15, 0, 0, true).Should().BeTrue();
        CapacitySloRules.IsMet(connections, 2001m, 15, 0, 0, false).Should().BeFalse();
    }

    [Fact]
    public void Measured_envelope_rejects_sla_and_pii_and_computes_burn()
    {
        var ok = Evaluate(Sample());
        ok.IsSla.Should().BeFalse();
        ok.ResultFormat.Should().Be(CapacitySloRules.ResultFormat);
        ok.Slis.Single(sli => sli.Id == "api-availability").BurnRate.Should().Be(1m);
        ok.Slis.Single(sli => sli.Id == "api-availability").BurnState.Should().Be("within");
        ok.Slis.Single(sli => sli.Id == "message-echo").BurnState.Should().Be("unmeasured");
        ok.Triggers.Single(trigger => trigger.Id == "adr-015-outbox").Met.Should().BeTrue();
        ok.Triggers.Single(trigger => trigger.Id == "adr-017-connections").Measured.Should().BeFalse();

        CapacitySloRules.TryEvaluate(Sample().Replace("\"isSla\": false", "\"isSla\": true", StringComparison.Ordinal), out _, out var sla)
            .Should().BeFalse();
        sla.Should().Be(CapacitySloErrors.SlaRejected);

        CapacitySloRules.TryEvaluate(Sample().Replace("\"environment\": \"compose-lab\"", "\"email\": \"a@b.c\"", StringComparison.Ordinal), out _, out var pii)
            .Should().BeFalse();
        pii.Should().Be(CapacitySloErrors.EnvelopeRejected);

        CapacitySloRules.TryEvaluate(Sample().Replace("compose-lab", "owner@example.com", StringComparison.Ordinal), out _, out var address)
            .Should().BeFalse();
        address.Should().Be(CapacitySloErrors.EnvelopeRejected);
    }

    [Fact]
    public void K6_catalog_and_workloads_match_the_model()
    {
        var root = RepoRoot();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "load", "capacity-profiles.json")));
        var catalog = json.RootElement;
        catalog.GetProperty("schemaVersion").GetInt32().Should().Be(CapacitySloRules.SchemaVersion);
        catalog.GetProperty("isSla").GetBoolean().Should().BeFalse();

        foreach (var profile in CapacitySloRules.Profiles)
        {
            var node = catalog.GetProperty("profiles").GetProperty(profile.Id);
            node.GetProperty("users").GetInt32().Should().Be(profile.Users);
            node.GetProperty("concurrency").GetInt32().Should().Be(profile.Concurrency);
            node.GetProperty("connectionsPerNode").GetInt32().Should().Be(profile.ConnectionsPerNode);
            node.GetProperty("messagesPerSecond").GetInt32().Should().Be(profile.MessagesPerSecond);
            node.GetProperty("largestChannel").GetInt32().Should().Be(profile.LargestChannel);
            node.GetProperty("fanOut").GetInt32().Should().Be(profile.FanOut);
            node.GetProperty("storageGb").GetInt32().Should().Be(profile.StorageGb);
            node.GetProperty("retentionDays").GetInt32().Should().Be(profile.RetentionDays);
            node.GetProperty("tenants").GetInt32().Should().Be(profile.Tenants);
        }

        catalog.GetProperty("recovery").GetProperty("standard").GetProperty("availability").GetDecimal().Should().Be(0.999m);
        catalog.GetProperty("slis").GetProperty("outbox-lag").GetDecimal().Should().Be(5000m);
        catalog.GetProperty("slis").GetProperty("search-lag").GetDecimal().Should().Be(10000m);
        catalog.GetProperty("slis").GetProperty("reconnect").GetDecimal().Should().Be(3000m);
        catalog.GetProperty("triggers").GetProperty("adr-015-outbox").GetDecimal().Should().Be(30000m);
        catalog.GetProperty("triggers").GetProperty("adr-016-search-query").GetDecimal().Should().Be(500m);
        catalog.GetProperty("triggers").GetProperty("adr-017-replicas").GetDecimal().Should().Be(3m);

        var script = File.ReadAllText(Path.Combine(root, "tests", "load", "capacity.js"));
        foreach (var workload in CapacitySloRules.Workloads)
        {
            script.Should().Contain(workload);
        }

        script.Should().Contain("noisy-neighbor").And.Contain("cross-tenant");
    }

    private static CapacityEvaluation Evaluate(string json)
    {
        CapacitySloRules.TryEvaluate(json, out var evaluation, out var error).Should().BeTrue(error);
        return evaluation;
    }

    private static string Sample() => """
        {
          "schemaVersion": 1,
          "profile": "standard",
          "workload": "send",
          "sampleKind": "measured",
          "capturedAt": "2026-10-21T12:00:00Z",
          "commit": "57dfeb6",
          "environment": "compose-lab",
          "errorMargin": "planning",
          "isSla": false,
          "observations": { "api-availability": 0.999 },
          "triggers": {
            "adr-015-outbox": { "value": 30001, "sustainedMinutes": 15, "workerReplicas": 2 }
          }
        }
        """;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "VibeChat.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
