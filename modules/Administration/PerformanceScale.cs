using System.Globalization;
using System.Text.Json;

namespace VibeChat.Administration;

/// <summary>
/// B-170. Inventory of the current hot paths. No flag, no endpoint, no new service.
/// Numbers that close ADR-015/016/017 stay in <see cref="CapacitySloRules"/>.
/// </summary>
public static class PerformanceScale
{
    public const string ReviewId = "B-170";
    public const int SchemaVersion = 1;
    public const int OutboxBatchSize = 20;

    public static readonly string[] LatencyWorkloads = ["send", "history", "search", "upload", "reconnect", "admin"];

    public static readonly HotPath[] HotPaths =
    [
        new(
            "send-message",
            "Messaging",
            "vibechat.messages.sent",
            "persistence",
            "MessageWriter commits message, seq and outbox in one transaction. POST /api/v1/channels/{id}/messages."),
        new(
            "history-sync",
            "Messaging",
            "http.history",
            "sync",
            "GET /api/v1/channels/{id}/messages pages by seq. Indexes (ConversationId, Sequence) and (TenantId, ConversationId, CreatedAt)."),
        new(
            "signalr-fanout",
            "Realtime",
            "vibechat.realtime.connections",
            "realtime-delivery",
            "The worker publishes after the outbox. Extra API replicas use the Redis backplane. The gauge is vibechat.realtime.connections."),
        new(
            "outbox-worker",
            "Infrastructure",
            "building_blocks.outbox_messages",
            "outbox-lag",
            "OutboxProcessor takes 20 pending rows ordered by OccurredAt. An in-process gate covers one worker. Index (ProcessedAt, OccurredAt)."),
        new(
            "fts-search",
            "Search",
            "http.search",
            "search-lag",
            "PostgreSQL FTS. GET /api/v1/search/messages. OpenSearch stays behind ADR-016."),
        new(
            "upload-presign",
            "Files",
            "http.upload",
            "attachments",
            "POST /api/v1/channels/{id}/attachments returns a presigned URL. Object bytes do not ride SendMessage."),
        new(
            "admin-export",
            "Administration",
            "http.admin-export",
            "api-latency",
            "GET /api/v1/admin/workspaces/{id}/export requires workspace.admin and stays off the send path.")
    ];

    public static readonly ScaleLimit[] Limits =
    [
        new("api", "API", "Stateless replicas behind the proxy.", "Add a replica. SignalR uses the Redis backplane."),
        new("worker", "Worker", "One replica. Batch of 20.", "A second replica needs SKIP LOCKED. A bus waits for ADR-015."),
        new("postgres", "PostgreSQL", "One primary with FORCE RLS.", "Scale the node. A read replica comes before OpenSearch."),
        new("redis", "Redis", "Presence, typing, rate limit and the backplane.", "Keys stay tenant-prefixed. Loss degrades realtime, not history."),
        new("minio", "MinIO", "Objects and presigned URLs.", "Lifecycle and CDN stay in B-145."),
        new("noisy-neighbor", "Tenancy", "RLS on every tenant table.", "The k6 isolation workload expects 403 or 404. Quotas stay behind their own flag."),
        new("backpressure", "API", "HTTP 429 and the outbox batch cap.", "Do not drop seq or bypass RLS to shed load.")
    ];

    public static readonly Bottleneck[] Bottlenecks =
    [
        new(
            "outbox-skip-locked",
            "outbox-worker",
            "deferred",
            "One worker. The in-process gate stops a double batch in that process. SKIP LOCKED is the lever when a second worker starts, and only after lag is measured. ADR-015 needs p95 lag above 30s for 15 minutes with at least two workers."),
        new(
            "outbox-batch",
            "outbox-worker",
            "deferred",
            "The batch is 20 and handlers run in order. A larger batch without a measured lag breach lengthens the transaction."),
        new(
            "search-on-primary",
            "fts-search",
            "deferred",
            "ADR-016 needs search query p95 above 500ms with at least 5 million indexed messages, or search CPU above 40 percent on the primary, for 15 minutes. A short lab sample is not that volume."),
        new(
            "compose-connections",
            "signalr-fanout",
            "deferred",
            "ADR-017 needs more than 3 API replicas, or more than 2000 connections on a node when a rolling restart no longer fits Compose. The HTTP lab does not open that many sockets.")
    ];

    public static readonly string[] RegressionChecks =
    [
        "task load:smoke",
        "task test:security",
        "k6 isolation expects 403 or 404",
        "FORCE RLS unchanged",
        "Redis keys stay tenant-prefixed"
    ];

    public static bool TriggersConfirmed()
    {
        var bus = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-015-outbox");
        var search = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-016-search-query");
        var cpu = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-016-search-cpu");
        var replicas = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-017-replicas");
        var connections = CapacitySloRules.Triggers.Single(trigger => trigger.Id == "adr-017-connections");
        return bus.Threshold == 30000m && bus.WindowMinutes == 15 && bus.MinWorkerReplicas == 2
            && search.Threshold == 500m && search.MinIndexedMessages == 5_000_000
            && cpu.Threshold == 0.40m
            && replicas.Threshold == 3m
            && connections.Threshold == 2000m && connections.RequiresRollingRestartGap;
    }

    public static bool TriggerClosed(AdrTrigger trigger, ProfileReading profile)
    {
        var minutes = profile.DurationSeconds / 60;
        return trigger.Id switch
        {
            "adr-015-outbox" => CapacitySloRules.IsMet(
                trigger, profile.OutboxLagMs, minutes, profile.WorkerReplicas, profile.IndexedMessages, false),
            "adr-016-search-query" => CapacitySloRules.IsMet(
                trigger, profile.SearchQueryP95Ms, minutes, profile.WorkerReplicas, profile.IndexedMessages, false),
            "adr-016-search-cpu" => profile.SearchCpu is decimal cpu && CapacitySloRules.IsMet(
                trigger, cpu, minutes, 0, 0, false),
            "adr-017-replicas" => CapacitySloRules.IsMet(
                trigger, profile.ApiReplicas, minutes, 0, 0, false),
            "adr-017-connections" => profile.ConnectionsPerNode is decimal connections && CapacitySloRules.IsMet(
                trigger, connections, minutes, 0, 0, true),
            _ => false
        };
    }

    public static string BurnState(string sliId, decimal? observed)
    {
        if (observed is null)
        {
            return CapacitySloRules.BurnState(null);
        }

        var sli = CapacitySloRules.Slis.Single(item => item.Id == sliId);
        return CapacitySloRules.BurnState(CapacitySloRules.Burn(sli, observed.Value));
    }

    public static bool TryReadCampaign(string? json, out PerformanceCampaign campaign, out string error)
    {
        campaign = null!;
        error = "";
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = "json";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || ContainsRejected(root) || !HasOnly(root, AllowedRoot))
            {
                error = "envelope";
                return false;
            }

            if (!root.TryGetProperty("isSla", out var sla) || sla.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("schemaVersion", out var version) || !version.TryGetInt32(out var schema) || schema != SchemaVersion
                || !TryString(root, "review", out var review) || review != ReviewId
                || !TryString(root, "capturedAt", out var capturedAt)
                || !DateTimeOffset.TryParse(capturedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var captured)
                || !TryString(root, "commit", out var commit) || !IsCommit(commit)
                || !TryString(root, "environment", out var environment) || environment.Length is < 1 or > 120 || environment.Contains('@')
                || !TryString(root, "errorMargin", out var margin) || margin.Length is < 1 or > 160)
            {
                error = "envelope";
                return false;
            }

            if (!root.TryGetProperty("hardware", out var hardware) || !TryHardware(hardware, out var hardwareReading)
                || !root.TryGetProperty("workloads", out var workloads) || !TryWorkloads(workloads, out var workloadNames)
                || !root.TryGetProperty("samples", out var samples) || !TrySamples(samples, out var sampleList)
                || !root.TryGetProperty("profiles", out var profiles) || !TryProfiles(profiles, sampleList, out var profileList))
            {
                error = "envelope";
                return false;
            }

            campaign = new PerformanceCampaign(
                captured, commit, environment, margin, hardwareReading, workloadNames, profileList, sampleList);
            return true;
        }
    }

    private static bool TryHardware(JsonElement hardware, out HardwareReading reading)
    {
        reading = null!;
        if (hardware.ValueKind != JsonValueKind.Object || !HasOnly(hardware, AllowedHardware)
            || !TryString(hardware, "nodeClass", out var nodeClass) || nodeClass.Length is < 1 or > 80
            || !TryString(hardware, "disk", out var disk) || disk.Length is < 1 or > 80
            || !hardware.TryGetProperty("vcpu", out var vcpu) || !vcpu.TryGetInt32(out var cores) || cores < 1
            || !hardware.TryGetProperty("memoryGb", out var memory) || !memory.TryGetInt32(out var memoryGb) || memoryGb < 1)
        {
            return false;
        }

        reading = new HardwareReading(nodeClass, cores, memoryGb, disk);
        return true;
    }

    private static bool TryWorkloads(JsonElement workloads, out string[] names)
    {
        names = [];
        if (workloads.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<string>();
        foreach (var item in workloads.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            list.Add(item.GetString() ?? "");
        }

        names = list.ToArray();
        return names.SequenceEqual(CapacitySloRules.Workloads);
    }

    private static bool TrySamples(JsonElement samples, out WorkloadSample[] list)
    {
        list = [];
        if (samples.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<WorkloadSample>();
        foreach (var sample in samples.EnumerateArray())
        {
            if (sample.ValueKind != JsonValueKind.Object || !HasOnly(sample, AllowedSample)
                || !TryString(sample, "profile", out var profile) || profile is not ("small" or "standard")
                || !TryString(sample, "workload", out var workload) || !CapacitySloRules.Workloads.Contains(workload)
                || !TryDecimal(sample, "p95Ms", out var p95) || p95 < 0
                || !TryDecimal(sample, "p99Ms", out var p99) || p99 + 0.05m < p95
                || !TryDecimal(sample, "failRate", out var failRate) || failRate is < 0 or > 1)
            {
                return false;
            }

            parsed.Add(new WorkloadSample(profile, workload, p95, p99, failRate));
        }

        list = parsed.ToArray();
        var expected = CapacitySloRules.Profiles
            .Where(profile => profile.Id is "small" or "standard")
            .SelectMany(profile => CapacitySloRules.Workloads.Select(workload => (profile.Id, workload)))
            .OrderBy(pair => pair.Id, StringComparer.Ordinal)
            .ThenBy(pair => pair.workload, StringComparer.Ordinal);
        var actual = list
            .Select(sample => (sample.Profile, sample.Workload))
            .OrderBy(pair => pair.Profile, StringComparer.Ordinal)
            .ThenBy(pair => pair.Workload, StringComparer.Ordinal);
        return expected.SequenceEqual(actual);
    }

    private static bool TryProfiles(JsonElement profiles, IReadOnlyList<WorkloadSample> samples, out ProfileReading[] list)
    {
        list = [];
        if (profiles.ValueKind != JsonValueKind.Object || !HasOnly(profiles, AllowedProfiles))
        {
            return false;
        }

        var parsed = new List<ProfileReading>();
        foreach (var id in new[] { "small", "standard" })
        {
            if (!profiles.TryGetProperty(id, out var profile) || !TryProfile(id, profile, samples, out var reading))
            {
                return false;
            }

            parsed.Add(reading);
        }

        list = parsed.ToArray();
        return true;
    }

    private static bool TryProfile(string id, JsonElement profile, IReadOnlyList<WorkloadSample> samples, out ProfileReading reading)
    {
        reading = null!;
        if (profile.ValueKind != JsonValueKind.Object || !HasOnly(profile, AllowedProfile)
            || !profile.TryGetProperty("vus", out var vusNode) || !vusNode.TryGetInt32(out var vus) || vus < 1
            || !profile.TryGetProperty("durationSeconds", out var durationNode) || !durationNode.TryGetInt32(out var duration) || duration < 1
            || !TryDecimal(profile, "p95Ms", out var p95) || p95 < 0
            || !TryDecimal(profile, "p99Ms", out var p99) || p99 + 0.05m < p95
            || !TryDecimal(profile, "outboxLagMs", out var outboxLag) || outboxLag < 0
            || !TryOptionalDecimal(profile, "connectionsPerNode", out var connections)
            || !TryDecimal(profile, "messagesPerSecond", out var messagesPerSecond) || messagesPerSecond < 0
            || !profile.TryGetProperty("largestChannel", out var channelNode) || !channelNode.TryGetInt32(out var largestChannel) || largestChannel < 1
            || !TryDecimal(profile, "httpFailRate", out var failRate) || failRate is < 0 or > 1
            || !profile.TryGetProperty("workerReplicas", out var workerNode) || !workerNode.TryGetInt32(out var workers) || workers < 1
            || !profile.TryGetProperty("apiReplicas", out var apiNode) || !apiNode.TryGetInt32(out var apiReplicas) || apiReplicas < 1
            || !profile.TryGetProperty("indexedMessages", out var indexedNode) || !indexedNode.TryGetInt64(out var indexed) || indexed < 0
            || !TryOptionalDecimal(profile, "searchCpu", out var searchCpu)
            || !TryDecimal(profile, "searchQueryP95Ms", out var searchQuery) || searchQuery < 0
            || !TryOptionalDecimal(profile, "searchIndexLagMs", out var searchIndex))
        {
            return false;
        }

        var latency = samples.Where(sample => sample.Profile == id && LatencyWorkloads.Contains(sample.Workload)).ToArray();
        var search = samples.SingleOrDefault(sample => sample.Profile == id && sample.Workload == "search");
        if (latency.Length != LatencyWorkloads.Length || search is null
            || Math.Abs(latency.Max(sample => sample.P95Ms) - p95) > 0.05m
            || Math.Abs(latency.Max(sample => sample.P99Ms) - p99) > 0.05m
            || Math.Abs(search.P95Ms - searchQuery) > 0.05m
            || Math.Abs(latency.Max(sample => sample.FailRate) - failRate) > 0.0001m)
        {
            return false;
        }

        reading = new ProfileReading(
            id, vus, duration, p95, p99, outboxLag, connections, messagesPerSecond, largestChannel,
            failRate, workers, apiReplicas, indexed, searchCpu, searchQuery, searchIndex);
        return true;
    }

    private static bool TryOptionalDecimal(JsonElement element, string name, out decimal? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (!property.TryGetDecimal(out var parsed) || parsed < 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryDecimal(JsonElement element, string name, out decimal value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.TryGetDecimal(out value);
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

    private static bool HasOnly(JsonElement element, HashSet<string> allowed)
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

    private static bool ContainsRejected(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (Forbidden.Contains(property.Name) || ContainsRejected(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsRejected(item))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString() ?? "";
            if (text.Contains('@', StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCommit(string commit) =>
        commit.Length is >= 7 and <= 40 && commit.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "displayname", "name", "body", "token", "password", "secret",
        "authorization", "phone", "subject", "userid", "username"
    };

    private static readonly HashSet<string> AllowedRoot = new(StringComparer.Ordinal)
    {
        "schemaVersion", "review", "isSla", "capturedAt", "commit", "environment",
        "errorMargin", "hardware", "workloads", "profiles", "samples"
    };

    private static readonly HashSet<string> AllowedHardware = new(StringComparer.Ordinal)
    {
        "nodeClass", "vcpu", "memoryGb", "disk"
    };

    private static readonly HashSet<string> AllowedProfiles = new(StringComparer.Ordinal) { "small", "standard" };

    private static readonly HashSet<string> AllowedProfile = new(StringComparer.Ordinal)
    {
        "vus", "durationSeconds", "p95Ms", "p99Ms", "outboxLagMs", "connectionsPerNode",
        "messagesPerSecond", "largestChannel", "httpFailRate", "workerReplicas", "apiReplicas",
        "indexedMessages", "searchCpu", "searchQueryP95Ms", "searchIndexLagMs"
    };

    private static readonly HashSet<string> AllowedSample = new(StringComparer.Ordinal)
    {
        "profile", "workload", "p95Ms", "p99Ms", "failRate"
    };
}

public sealed record HotPath(string Id, string Owner, string Metric, string BaselineSli, string Evidence);

public sealed record ScaleLimit(string Id, string Component, string Limit, string Lever);

public sealed record Bottleneck(string Id, string HotPathId, string Status, string Reason);

public sealed record HardwareReading(string NodeClass, int Vcpu, int MemoryGb, string Disk);

public sealed record WorkloadSample(string Profile, string Workload, decimal P95Ms, decimal P99Ms, decimal FailRate);

public sealed record ProfileReading(
    string Id,
    int Vus,
    int DurationSeconds,
    decimal P95Ms,
    decimal P99Ms,
    decimal OutboxLagMs,
    decimal? ConnectionsPerNode,
    decimal MessagesPerSecond,
    int LargestChannel,
    decimal HttpFailRate,
    int WorkerReplicas,
    int ApiReplicas,
    long IndexedMessages,
    decimal? SearchCpu,
    decimal SearchQueryP95Ms,
    decimal? SearchIndexLagMs);

public sealed record PerformanceCampaign(
    DateTimeOffset CapturedAt,
    string Commit,
    string Environment,
    string ErrorMargin,
    HardwareReading Hardware,
    IReadOnlyList<string> Workloads,
    IReadOnlyList<ProfileReading> Profiles,
    IReadOnlyList<WorkloadSample> Samples);
