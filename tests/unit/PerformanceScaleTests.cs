using System.Text.Json;
using FluentAssertions;
using VibeChat.Administration;

namespace VibeChat.UnitTests;

public sealed class PerformanceScaleTests
{
    [Fact]
    public void Hot_paths_use_the_b146_baseline_and_confirmed_triggers()
    {
        PerformanceScale.TriggersConfirmed().Should().BeTrue();
        PerformanceScale.OutboxBatchSize.Should().Be(20);
        PerformanceScale.HotPaths.Select(path => path.Id).Should().Equal(
            "send-message",
            "history-sync",
            "signalr-fanout",
            "outbox-worker",
            "fts-search",
            "upload-presign",
            "admin-export");
        PerformanceScale.HotPaths.Should().OnlyContain(path =>
            CapacitySloRules.Slis.Any(sli => sli.Id == path.BaselineSli)
            && path.Owner.Length > 0
            && path.Metric.Length > 0
            && path.Evidence.Length > 0);

        var echo = CapacitySloRules.Slis.Single(sli => sli.Id == "persistence");
        echo.Threshold.Should().Be(400m);
        PerformanceScale.Limits.Select(limit => limit.Id).Should().Equal(
            "api", "worker", "postgres", "redis", "minio", "noisy-neighbor", "backpressure");
        PerformanceScale.Bottlenecks.Should().OnlyContain(item =>
            item.Status == "deferred"
            && item.Reason.Length > 40
            && PerformanceScale.HotPaths.Any(path => path.Id == item.HotPathId));
        PerformanceScale.RegressionChecks.Should().Contain("task load:smoke").And.Contain("task test:security");
    }

    [Fact]
    public void A_short_lab_sample_does_not_close_an_adr_trigger()
    {
        var profile = new ProfileReading(
            "small", 2, 20, 40m, 80m, 100m, null, 2m, 2, 0m, 1, 1, 10, null, 30m, null);
        foreach (var trigger in CapacitySloRules.Triggers)
        {
            PerformanceScale.TriggerClosed(trigger, profile).Should().BeFalse();
        }

        var sustained = profile with { DurationSeconds = 15 * 60, WorkerReplicas = 2, OutboxLagMs = 30001m };
        PerformanceScale.TriggerClosed(
            CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-015-outbox"),
            sustained).Should().BeTrue();
        PerformanceScale.BurnState("api-latency", null).Should().Be("unmeasured");
        PerformanceScale.BurnState("api-latency", 500m).Should().Be("within");
        PerformanceScale.BurnState("outbox-lag", 5001m).Should().Be("burning");
    }

    [Fact]
    public void Lab_campaign_matches_the_script_and_rejects_pii()
    {
        var root = RepoRoot();
        var json = File.ReadAllText(Path.Combine(root, "tests", "load", "performance-baseline.json"));
        using (var published = JsonDocument.Parse(json))
        {
            published.RootElement.GetProperty("isSla").GetBoolean().Should().BeFalse();
        }

        PerformanceScale.TryReadCampaign(json, out var campaign, out var error).Should().BeTrue(error);
        campaign.Workloads.Should().Equal(CapacitySloRules.Workloads);
        campaign.Profiles.Select(profile => profile.Id).Should().Equal("small", "standard");
        campaign.Profiles.Should().OnlyContain(profile =>
            profile.DurationSeconds < 15 * 60
            && profile.WorkerReplicas == 1
            && profile.ApiReplicas == 1
            && profile.ConnectionsPerNode == null
            && profile.SearchCpu == null
            && profile.SearchIndexLagMs == null
            && profile.HttpFailRate < 0.05m
            && profile.P99Ms + 0.05m >= profile.P95Ms);

        foreach (var profile in campaign.Profiles)
        {
            foreach (var trigger in CapacitySloRules.Triggers)
            {
                PerformanceScale.TriggerClosed(trigger, profile).Should().BeFalse();
            }

            PerformanceScale.BurnState("api-latency", profile.P95Ms).Should().NotBe("unmeasured");
            PerformanceScale.BurnState("outbox-lag", profile.OutboxLagMs).Should().NotBe("unmeasured");
        }

        var script = File.ReadAllText(Path.Combine(root, "tests", "load", "performance.js"));
        foreach (var workload in CapacitySloRules.Workloads)
        {
            script.Should().Contain($"{workload}(");
        }

        script.Should().Contain("B-170");
        script.Should().NotContain("@");

        var doc = File.ReadAllText(Path.Combine(root, "docs", "architecture", "performance-escalabilidade.md"));
        foreach (var path in PerformanceScale.HotPaths)
        {
            doc.Should().Contain(path.Id);
        }

        doc.Should().Contain("ADR-015").And.Contain("ADR-016").And.Contain("ADR-017");
        PerformanceScale.TryReadCampaign(json.Replace("\"isSla\": false", "\"isSla\": true", StringComparison.Ordinal), out _, out _)
            .Should().BeFalse();
        PerformanceScale.TryReadCampaign(json.Replace("\"environment\":", "\"email\":", StringComparison.Ordinal), out _, out _)
            .Should().BeFalse();
    }

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
