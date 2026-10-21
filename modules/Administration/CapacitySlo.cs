using System.Globalization;
using System.Text.Json;

namespace VibeChat.Administration;

/// <summary>B-146. Off by default. Objectives are not a commercial SLA.</summary>
public sealed class CapacitySloOptions
{
    public const string SectionName = "Features:CapacitySlo";

    public bool Enabled { get; set; }
}

public static class CapacitySloErrors
{
    public const string Disabled = "CapacitySloDisabled";
    public const string EnvelopeRejected = "CapacitySloEnvelopeRejected";
    public const string SlaRejected = "CapacitySloSlaRejected";

    public static int Status(string error) => error == Disabled ? 404 : 400;
}

public static class CapacitySloRules
{
    public const int SchemaVersion = 1;
    public const string ResultFormat = "vibechat.capacity-result.v1";
    public const string SampleUnmeasured = "unmeasured";
    public const string SampleMeasured = "measured";

    public static readonly string[] Workloads = ["send", "history", "search", "upload", "reconnect", "admin", "isolation"];

    public static readonly LoadProfile[] Profiles =
    [
        new("small", 50, 10, 50, 2, 50, 50, 20, 90, 1),
        new("standard", 500, 100, 500, 20, 200, 200, 200, 365, 5),
        new("large", 5000, 1000, 2000, 100, 1000, 1000, 2000, 365, 25)
    ];

    public static readonly RecoveryProfile[] Recovery =
    [
        new("basic", null, 1440, 240, true, true, ""),
        new("standard", 0.999m, 60, 240, false, true, ""),
        new("ha", 0.9995m, 5, 30, false, false, "B-144")
    ];

    public static readonly SliDefinition[] Slis =
    [
        new("api-availability", "ratio", 0.999m, 0.999m, "ratio"),
        new("api-latency", "latency", 500m, 500m, "ms"),
        new("message-echo", "latency", 1000m, 1000m, "ms"),
        new("outbox-lag", "latency", 5000m, 5000m, "ms"),
        new("search-lag", "latency", 10000m, 10000m, "ms"),
        new("persistence", "latency", 400m, 400m, "ms"),
        new("realtime-delivery", "latency", 1000m, 1000m, "ms"),
        new("reconnect", "latency", 3000m, 3000m, "ms"),
        new("sync", "latency", 800m, 800m, "ms"),
        new("revocation", "latency", 30000m, 30000m, "ms"),
        new("attachments", "latency", 5000m, 5000m, "ms"),
        new("restore-verified", "flag", 1m, 1m, "flag")
    ];

    public static readonly AdrTrigger[] Triggers =
    [
        new("adr-015-outbox", "ADR-015", "outbox-lag", 30000m, 15, "gt", 2, null, false),
        new("adr-016-search-query", "ADR-016", "search-query-p95", 500m, 15, "gt", null, 5_000_000, false),
        new("adr-016-search-cpu", "ADR-016", "search-primary-cpu", 0.40m, 15, "gt", null, null, false),
        new("adr-017-replicas", "ADR-017", "api-replicas", 3m, 15, "gt", null, null, false),
        new("adr-017-connections", "ADR-017", "connections-per-node", 2000m, 15, "gt", null, null, true)
    ];

    public static readonly HardwareBaseline Hardware = new(
        "compose-single-node",
        4,
        8,
        "local-ssd",
        "planning-envelope",
        "2026-10-21",
        "not-measured");

    public static CapacityDashboard Dashboard() => new(
        SchemaVersion,
        false,
        SampleUnmeasured,
        ResultFormat,
        Workloads,
        Hardware,
        Profiles,
        Recovery,
        Slis.Select(sli => Reading(sli, null)).ToArray(),
        Triggers.Select(trigger => new TriggerReading(
            trigger.Id,
            trigger.Adr,
            trigger.Metric,
            trigger.Threshold,
            trigger.WindowMinutes,
            trigger.Comparator,
            trigger.MinWorkerReplicas,
            trigger.MinIndexedMessages,
            trigger.RequiresRollingRestartGap,
            false,
            false)).ToArray());

    public static bool TryEvaluate(string? json, out CapacityEvaluation evaluation, out string error)
    {
        evaluation = null!;
        error = "";
        if (string.IsNullOrWhiteSpace(json))
        {
            error = CapacitySloErrors.EnvelopeRejected;
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = CapacitySloErrors.EnvelopeRejected;
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object || ContainsRejected(document.RootElement))
            {
                error = CapacitySloErrors.EnvelopeRejected;
                return false;
            }

            var root = document.RootElement;
            if (!HasOnly(root, AllowedRoot))
            {
                error = CapacitySloErrors.EnvelopeRejected;
                return false;
            }

            if (!root.TryGetProperty("isSla", out var sla) || sla.ValueKind != JsonValueKind.False)
            {
                error = sla.ValueKind == JsonValueKind.True
                    ? CapacitySloErrors.SlaRejected
                    : CapacitySloErrors.EnvelopeRejected;
                return false;
            }

            if (!root.TryGetProperty("schemaVersion", out var version)
                || !version.TryGetInt32(out var schemaVersion)
                || schemaVersion != SchemaVersion)
            {
                error = CapacitySloErrors.EnvelopeRejected;
                return false;
            }

            if (!TryString(root, "profile", out var profile) || Profiles.All(item => item.Id != profile)
                || !TryString(root, "workload", out var workload) || !Workloads.Contains(workload, StringComparer.Ordinal)
                || !TryString(root, "sampleKind", out var sampleKind) || sampleKind != SampleMeasured
                || !TryString(root, "capturedAt", out var capturedAt) || !DateTimeOffset.TryParse(capturedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)
                || !TryString(root, "commit", out var commit) || !IsCommit(commit)
                || !TryString(root, "environment", out var environment) || environment.Length is < 1 or > 120
                || !TryString(root, "errorMargin", out var errorMargin) || errorMargin.Length is < 1 or > 80
                || !root.TryGetProperty("observations", out var observations)
                || observations.ValueKind != JsonValueKind.Object)
            {
                error = CapacitySloErrors.EnvelopeRejected;
                return false;
            }

            var observed = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var property in observations.EnumerateObject())
            {
                if (Slis.All(sli => sli.Id != property.Name) || !property.Value.TryGetDecimal(out var observedValue))
                {
                    error = CapacitySloErrors.EnvelopeRejected;
                    return false;
                }

                observed[property.Name] = observedValue;
            }

            var triggerReadings = new List<TriggerReading>();
            JsonElement triggers = default;
            var hasTriggers = root.TryGetProperty("triggers", out triggers);
            if (hasTriggers && triggers.ValueKind != JsonValueKind.Object)
            {
                error = CapacitySloErrors.EnvelopeRejected;
                return false;
            }

            foreach (var trigger in Triggers)
            {
                if (!hasTriggers || !triggers.TryGetProperty(trigger.Id, out var sample))
                {
                    triggerReadings.Add(Unmeasured(trigger));
                    continue;
                }

                if (sample.ValueKind != JsonValueKind.Object || !HasOnly(sample, AllowedTrigger))
                {
                    error = CapacitySloErrors.EnvelopeRejected;
                    return false;
                }

                if (!sample.TryGetProperty("value", out var valueNode) || !valueNode.TryGetDecimal(out var triggerValue)
                    || !sample.TryGetProperty("sustainedMinutes", out var minutesNode) || !minutesNode.TryGetInt32(out var sustainedMinutes))
                {
                    error = CapacitySloErrors.EnvelopeRejected;
                    return false;
                }

                var replicas = OptionalInt(sample, "workerReplicas");
                var indexed = OptionalLong(sample, "indexedMessages");
                var restart = OptionalBool(sample, "rollingRestartExceedsCompose");
                if (replicas < 0 || indexed < 0)
                {
                    error = CapacitySloErrors.EnvelopeRejected;
                    return false;
                }

                var met = IsMet(trigger, triggerValue, sustainedMinutes, replicas, indexed, restart);
                triggerReadings.Add(new TriggerReading(
                    trigger.Id,
                    trigger.Adr,
                    trigger.Metric,
                    trigger.Threshold,
                    trigger.WindowMinutes,
                    trigger.Comparator,
                    trigger.MinWorkerReplicas,
                    trigger.MinIndexedMessages,
                    trigger.RequiresRollingRestartGap,
                    met,
                    true));
            }

            evaluation = new CapacityEvaluation(
                SchemaVersion,
                false,
                ResultFormat,
                profile,
                workload,
                capturedAt,
                commit,
                environment,
                errorMargin,
                Slis.Select(sli => Reading(sli, observed.TryGetValue(sli.Id, out var value) ? value : null)).ToArray(),
                triggerReadings);
            return true;
        }
    }

    public static bool IsMet(
        AdrTrigger trigger,
        decimal value,
        int sustainedMinutes,
        int workerReplicas,
        long indexedMessages,
        bool rollingRestartExceedsCompose)
    {
        if (sustainedMinutes < trigger.WindowMinutes)
        {
            return false;
        }

        var over = trigger.Comparator == "gt" ? value > trigger.Threshold : value >= trigger.Threshold;
        if (!over)
        {
            return false;
        }

        if (trigger.MinWorkerReplicas is int replicas && workerReplicas < replicas)
        {
            return false;
        }

        if (trigger.MinIndexedMessages is long messages && indexedMessages < messages)
        {
            return false;
        }

        return !trigger.RequiresRollingRestartGap || rollingRestartExceedsCompose;
    }

    public static SliReading Reading(SliDefinition sli, decimal? observed)
    {
        decimal? burn = observed is null ? null : Burn(sli, observed.Value);
        return new SliReading(sli.Id, sli.Kind, sli.Objective, sli.Threshold, sli.Unit, observed, burn, BurnState(burn));
    }

    public static decimal Burn(SliDefinition sli, decimal observed) => sli.Kind switch
    {
        "ratio" => RatioBurn(observed, sli.Objective),
        "flag" => observed >= sli.Threshold ? 0m : 2m,
        _ => sli.Threshold == 0 ? 0m : decimal.Round(observed / sli.Threshold, 4, MidpointRounding.AwayFromZero)
    };

    public static string BurnState(decimal? burn) => burn switch
    {
        null => "unmeasured",
        <= 1m => "within",
        _ => "burning"
    };

    private static decimal RatioBurn(decimal observed, decimal objective)
    {
        var budget = 1m - objective;
        if (budget <= 0)
        {
            return observed >= 1m ? 0m : 2m;
        }

        var bad = observed >= 1m ? 0m : 1m - observed;
        return decimal.Round(bad / budget, 4, MidpointRounding.AwayFromZero);
    }

    private static TriggerReading Unmeasured(AdrTrigger trigger) => new(
        trigger.Id,
        trigger.Adr,
        trigger.Metric,
        trigger.Threshold,
        trigger.WindowMinutes,
        trigger.Comparator,
        trigger.MinWorkerReplicas,
        trigger.MinIndexedMessages,
        trigger.RequiresRollingRestartGap,
        false,
        false);

    private static bool ContainsRejected(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (Forbidden.Contains(property.Name) || ContainsRejected(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsRejected(item))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.String:
                var text = element.GetString() ?? "";
                return text.Contains('@', StringComparison.Ordinal) || text.Length > 120;
            default:
                return false;
        }
    }

    private static bool HasOnly(JsonElement element, IReadOnlySet<string> allowed)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return value.Length > 0;
    }

    private static int OptionalInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return 0;
        }

        return property.ValueKind == JsonValueKind.Number ? property.GetInt32() : -1;
    }

    private static long OptionalLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return 0;
        }

        return property.ValueKind == JsonValueKind.Number ? property.GetInt64() : -1;
    }

    private static bool OptionalBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static bool IsCommit(string commit) =>
        commit.Length is >= 7 and <= 40 && commit.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "displayname", "name", "body", "token", "password", "secret",
        "authorization", "phone", "subject", "userid", "username"
    };

    private static readonly HashSet<string> AllowedRoot = new(StringComparer.Ordinal)
    {
        "schemaVersion", "profile", "workload", "sampleKind", "capturedAt", "commit",
        "environment", "errorMargin", "isSla", "observations", "triggers"
    };

    private static readonly HashSet<string> AllowedTrigger = new(StringComparer.Ordinal)
    {
        "value", "sustainedMinutes", "workerReplicas", "indexedMessages", "rollingRestartExceedsCompose"
    };
}

public sealed record LoadProfile(
    string Id,
    int Users,
    int Concurrency,
    int ConnectionsPerNode,
    int MessagesPerSecond,
    int LargestChannel,
    int FanOut,
    int StorageGb,
    int RetentionDays,
    int Tenants);

public sealed record RecoveryProfile(
    string Id,
    decimal? Availability,
    int RpoMinutes,
    int RtoMinutes,
    bool BestEffort,
    bool Current,
    string AvailableAfter);

public sealed record SliDefinition(string Id, string Kind, decimal Objective, decimal Threshold, string Unit);

public sealed record AdrTrigger(
    string Id,
    string Adr,
    string Metric,
    decimal Threshold,
    int WindowMinutes,
    string Comparator,
    int? MinWorkerReplicas,
    long? MinIndexedMessages,
    bool RequiresRollingRestartGap);

public sealed record HardwareBaseline(
    string NodeClass,
    int Vcpu,
    int MemoryGb,
    string Disk,
    string Kind,
    string CapturedAt,
    string ErrorMargin);

public sealed record CapacityDashboard(
    int SchemaVersion,
    bool IsSla,
    string SampleKind,
    string ResultFormat,
    IReadOnlyList<string> Workloads,
    HardwareBaseline Hardware,
    IReadOnlyList<LoadProfile> Profiles,
    IReadOnlyList<RecoveryProfile> Recovery,
    IReadOnlyList<SliReading> Slis,
    IReadOnlyList<TriggerReading> Triggers);

public sealed record CapacityEvaluation(
    int SchemaVersion,
    bool IsSla,
    string ResultFormat,
    string Profile,
    string Workload,
    string CapturedAt,
    string Commit,
    string Environment,
    string ErrorMargin,
    IReadOnlyList<SliReading> Slis,
    IReadOnlyList<TriggerReading> Triggers);

public sealed record SliReading(
    string Id,
    string Kind,
    decimal? Objective,
    decimal Threshold,
    string Unit,
    decimal? Observed,
    decimal? BurnRate,
    string BurnState);

public sealed record TriggerReading(
    string Id,
    string Adr,
    string Metric,
    decimal Threshold,
    int WindowMinutes,
    string Comparator,
    int? MinWorkerReplicas,
    long? MinIndexedMessages,
    bool RequiresRollingRestartGap,
    bool Met,
    bool Measured);

public sealed record CapacitySloResult(int StatusCode, string? Error, object? Payload)
{
    public static CapacitySloResult Ok(object payload) => new(200, null, payload);

    public static CapacitySloResult Fail(string error) => new(CapacitySloErrors.Status(error), error, null);
}
