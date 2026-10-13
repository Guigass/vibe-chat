using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VibeChat.Administration;

/// <summary>B-154. Off by default; Development and the test host opt in.</summary>
public sealed class SupportBundleOptions
{
    public const string SectionName = "Features:SupportBundle";

    public bool Enabled { get; set; }
}

public static class SupportBundleSchema
{
    public const string Format = "vibechat.support-bundle.v1";
    public const string Generator = "vibechat-api";
    public const int Version = 1;
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    public const int MaxDownloads = 3;
    public const int DefaultWindowMinutes = 60;
}

public static class DiagnosticStatus
{
    public const string Pass = "Pass";
    public const string Warn = "Warn";
    public const string Fail = "Fail";
    public const string Skipped = "Skipped";
}

public static class DiagnosticSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
}

public static class DiagnosticVerdict
{
    public const string Ready = "ready";
    public const string Degraded = "degraded";
    public const string ActionRequired = "action_required";
}

public static class RepairActions
{
    public const string SearchReindex = "search.reindex";
    public const string MembershipReconcile = "membership.reconcile";

    public static bool IsAllowed(string? action) =>
        action is SearchReindex or MembershipReconcile;
}

public static class RepairStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

public static class ProbeKinds
{
    public const string Email = "email";
    public const string Push = "push";
    public const string Storage = "storage";

    public static bool IsAllowed(string? kind) => kind is Email or Push or Storage;
}

public static class SupportErrors
{
    public const string Disabled = "SupportBundleDisabled";
    public const string IdempotencyRequired = "IdempotencyKeyRequired";
    public const string IdempotencyConflict = "SupportIdempotencyConflict";
    public const string NotFound = "SupportBundleNotFound";
    public const string Expired = "SupportBundleExpired";
    public const string ActionNotAllowed = "RepairActionNotAllowed";
    public const string ConfirmRequired = "RepairConfirmRequired";
    public const string State = "RepairState";
    public const string ProbeUnknown = "ProbeUnknown";
}

public sealed record DiagnosticEvidence(
    int? LatencyMs,
    int? Pending,
    int? Stale,
    bool? Configured,
    string? Migration,
    string? Endpoint);

public sealed record DiagnosticCheck(
    string Code,
    int Version,
    string Component,
    string Status,
    string Severity,
    string Summary,
    DiagnosticEvidence Evidence,
    string Runbook,
    DateTimeOffset ObservedAt,
    string CorrelationId);

public sealed record DiagnosticFacts
{
    public int ApiLatencyMs { get; init; }
    public bool DatabaseOk { get; init; }
    public int DatabaseLatencyMs { get; init; }
    public bool RedisConfigured { get; init; }
    public bool RedisOk { get; init; }
    public bool StorageConfigured { get; init; }
    public bool StorageOk { get; init; }
    public bool OidcConfigured { get; init; }
    public bool EmailEnabled { get; init; }
    public bool PushEnabled { get; init; }
    public bool ProxyEnabled { get; init; }
    public int OutboxPending { get; init; }
    public int OutboxStale { get; init; }
    public bool MigrationsCurrent { get; init; }
    public string MigrationId { get; init; } = "";
    public bool Operator { get; init; }
    public string? RedisEndpoint { get; init; }
    public string? StorageEndpoint { get; init; }
    public string? OidcEndpoint { get; init; }
}

public sealed class SupportBundleRecord
{
    public Guid Id { get; set; }
    public VibeChat.SharedKernel.TenantId TenantId { get; set; }
    public VibeChat.SharedKernel.WorkspaceId WorkspaceId { get; set; }
    public VibeChat.SharedKernel.UserId RequestedBy { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Status { get; set; } = "ready";
    public string SchemaVersion { get; set; } = SupportBundleSchema.Format;
    public string ManifestJson { get; set; } = "{}";
    public string Checksum { get; set; } = "";
    public int RedactedFields { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CorrelationId { get; set; } = "";
    public int DownloadCount { get; set; }
}

public sealed class SupportRepairRecord
{
    public Guid Id { get; set; }
    public VibeChat.SharedKernel.TenantId TenantId { get; set; }
    public VibeChat.SharedKernel.WorkspaceId WorkspaceId { get; set; }
    public VibeChat.SharedKernel.UserId RequestedBy { get; set; }
    public string ActionCode { get; set; } = "";
    public bool DryRun { get; set; }
    public string Status { get; set; } = RepairStatus.Running;
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string CheckpointJson { get; set; } = "{}";
    public int Estimated { get; set; }
    public int Written { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class DiagnosticEvaluator
{
    public static IReadOnlyList<DiagnosticCheck> Evaluate(DiagnosticFacts facts, DateTimeOffset now, string correlationId)
    {
        var id = SupportRedactor.Correlation(correlationId);
        return
        [
            Required("api.live", "api", true, facts.ApiLatencyMs, null, now, id),
            Required("database.ready", "database", facts.DatabaseOk, facts.DatabaseLatencyMs, null, now, id),
            Optional("redis.ready", "redis", facts.RedisConfigured, facts.RedisOk, facts.Operator ? facts.RedisEndpoint : null, now, id),
            Optional("storage.ready", "storage", facts.StorageConfigured, facts.StorageOk, facts.Operator ? facts.StorageEndpoint : null, now, id),
            Optional("oidc.configured", "oidc", facts.OidcConfigured, facts.OidcConfigured, facts.Operator ? facts.OidcEndpoint : null, now, id),
            Optional("email.configured", "email", facts.EmailEnabled, facts.EmailEnabled, null, now, id),
            Optional("webpush.configured", "webpush", facts.PushEnabled, facts.PushEnabled, null, now, id),
            Optional("proxy.configured", "proxy", facts.ProxyEnabled, facts.ProxyEnabled, null, now, id),
            Outbox(facts, now, id),
            Worker(facts, now, id),
            Migrations(facts, now, id)
        ];
    }

    public static string Verdict(IReadOnlyList<DiagnosticCheck> checks)
    {
        if (checks.Any(check => check.Status == DiagnosticStatus.Fail))
        {
            return DiagnosticVerdict.ActionRequired;
        }

        if (checks.Any(check => check.Status == DiagnosticStatus.Warn))
        {
            return DiagnosticVerdict.Degraded;
        }

        return DiagnosticVerdict.Ready;
    }

    private static DiagnosticCheck Required(string code, string component, bool ok, int latencyMs, string? endpoint, DateTimeOffset now, string correlationId) =>
        Build(code, component, ok ? DiagnosticStatus.Pass : DiagnosticStatus.Fail, latencyMs, null, null, null, endpoint, null, now, correlationId);

    private static DiagnosticCheck Optional(string code, string component, bool configured, bool ok, string? endpoint, DateTimeOffset now, string correlationId)
    {
        var status = !configured ? DiagnosticStatus.Skipped : ok ? DiagnosticStatus.Pass : DiagnosticStatus.Fail;
        return Build(code, component, status, null, null, null, configured, endpoint, null, now, correlationId);
    }

    private static DiagnosticCheck Outbox(DiagnosticFacts facts, DateTimeOffset now, string correlationId)
    {
        var status = facts.OutboxPending > 50 ? DiagnosticStatus.Warn : DiagnosticStatus.Pass;
        return Build("outbox.lag", "outbox", status, null, facts.OutboxPending, facts.OutboxStale, null, null, null, now, correlationId);
    }

    private static DiagnosticCheck Worker(DiagnosticFacts facts, DateTimeOffset now, string correlationId)
    {
        var status = facts.OutboxStale > 0 ? DiagnosticStatus.Warn : DiagnosticStatus.Pass;
        return Build("worker.heartbeat", "worker", status, null, facts.OutboxPending, facts.OutboxStale, null, null, null, now, correlationId);
    }

    private static DiagnosticCheck Migrations(DiagnosticFacts facts, DateTimeOffset now, string correlationId) =>
        Build(
            facts.MigrationsCurrent ? "migrations.current" : "migrations.pending",
            "migrations",
            facts.MigrationsCurrent ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
            null,
            null,
            null,
            null,
            null,
            facts.Operator ? facts.MigrationId : null,
            now,
            correlationId);

    private static DiagnosticCheck Build(
        string code,
        string component,
        string status,
        int? latencyMs,
        int? pending,
        int? stale,
        bool? configured,
        string? endpoint,
        string? migration,
        DateTimeOffset now,
        string correlationId)
    {
        var severity = status switch
        {
            DiagnosticStatus.Fail => DiagnosticSeverity.Error,
            DiagnosticStatus.Warn => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Info
        };
        var summary = status == DiagnosticStatus.Skipped ? "feature.off" : code;
        return new DiagnosticCheck(
            code,
            1,
            component,
            status,
            severity,
            summary,
            new DiagnosticEvidence(latencyMs, pending, stale, configured, migration, endpoint),
            component,
            now,
            correlationId);
    }
}

public static partial class SupportRedactor
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "body", "content", "message", "cookie", "authorization", "password", "secret", "token",
        "connectionstring", "connectionString", "apikey", "apiKey", "privatekey", "privateKey"
    };

    public static int Scrub(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            if (node is JsonArray array)
            {
                var hits = 0;
                foreach (var item in array)
                {
                    hits += Scrub(item);
                }

                return hits;
            }

            return 0;
        }

        var count = 0;
        foreach (var prop in obj.ToList())
        {
            if (Forbidden.Contains(prop.Key))
            {
                obj[prop.Key] = "[redacted]";
                count++;
                continue;
            }

            if (prop.Value is JsonValue value && value.TryGetValue<string>(out var text))
            {
                var (redacted, hits) = Redact(text ?? "");
                if (hits > 0)
                {
                    obj[prop.Key] = redacted;
                    count += hits;
                }
            }
            else
            {
                count += Scrub(prop.Value);
            }
        }

        return count;
    }

    public static (string Text, int Hits) Redact(string value)
    {
        var hits = 0;
        var text = value;
        foreach (var pattern in Patterns())
        {
            text = pattern.Replace(text, _ =>
            {
                hits++;
                return "[redacted]";
            });
        }

        return (text, hits);
    }

    public static string Correlation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || !CorrelationPattern().IsMatch(value))
        {
            return "none";
        }

        return value;
    }

    public static string? PublicEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120)
        {
            return null;
        }

        if (value.Contains('@', StringComparison.Ordinal)
            || value.Contains("password", StringComparison.OrdinalIgnoreCase)
            || value.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || value.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }

    public static string Sha256(string payload)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    [GeneratedRegex(@"^[A-Za-z0-9:._\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationPattern();

    private static IEnumerable<Regex> Patterns()
    {
        yield return EmailPattern();
        yield return UriSecretPattern();
        yield return AssignmentPattern();
        yield return TokenPattern();
        yield return ConnectionPattern();
    }

    [GeneratedRegex(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?i)\b(?:postgres|postgresql|redis|rediss|mongodb|mysql|amqp):\/\/\S+")]
    private static partial Regex UriSecretPattern();

    [GeneratedRegex(@"(?i)(password|pwd|secret|token|api[_-]?key|connectionstring)\s*[:=]\s*\S+")]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex(@"(?i)\b(?:sk-|eyJ|Bearer\s+)[A-Za-z0-9._\-]{6,}")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?i)Host=[^;\s]+;[^;]*Password=[^;\s]+")]
    private static partial Regex ConnectionPattern();
}

public sealed class SupportBundleCheck
{
    public string Code { get; set; } = "";
    public string Component { get; set; } = "";
    public string Status { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Runbook { get; set; } = "";
}

public sealed class SupportBundleMetrics
{
    public int OutboxPending { get; set; }
    public int Checks { get; set; }
}

public sealed class SupportRedactionReport
{
    public int Fields { get; set; }
}

public sealed class SupportBundleDocument
{
    public string Schema { get; set; } = SupportBundleSchema.Format;
    public string Generator { get; set; } = SupportBundleSchema.Generator;
    public int GeneratorVersion { get; set; } = SupportBundleSchema.Version;
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int WindowMinutes { get; set; }
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string CorrelationId { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Verdict { get; set; } = "";
    public string Environment { get; set; } = "";
    public string Migration { get; set; } = "";
    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.Ordinal);
    public SupportBundleMetrics Metrics { get; set; } = new();
    public List<SupportBundleCheck> Checks { get; set; } = [];
    public SupportRedactionReport Redaction { get; set; } = new();
    public string? Checksum { get; set; }
}

public static partial class SupportBundleComposer
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static (string Json, string Checksum, int Redacted) Compose(SupportBundleDocument document)
    {
        document.Checksum = null;
        document.Schema = SupportBundleSchema.Format;
        foreach (var check in document.Checks)
        {
            check.Summary = StableCode(check.Summary, check.Code);
        }

        var node = JsonSerializer.SerializeToNode(document, Json)!.AsObject();
        var redacted = SupportRedactor.Scrub(node);
        node["redaction"] = JsonSerializer.SerializeToNode(new SupportRedactionReport { Fields = redacted }, Json);
        node.Remove("checksum");
        var payload = node.ToJsonString(Json);
        var checksum = SupportRedactor.Sha256(payload);
        node["checksum"] = checksum;
        return (node.ToJsonString(Json), checksum, redacted);
    }

    public static bool TryValidate(string json, out string error)
    {
        error = "";
        JsonObject node;
        try
        {
            node = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("empty");
        }
        catch (JsonException)
        {
            error = "schema";
            return false;
        }

        if (node["schema"]?.GetValue<string>() != SupportBundleSchema.Format)
        {
            error = "schema";
            return false;
        }

        var checksum = node["checksum"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(checksum))
        {
            error = "checksum";
            return false;
        }

        node.Remove("checksum");
        if (!string.Equals(SupportRedactor.Sha256(node.ToJsonString(Json)), checksum, StringComparison.Ordinal))
        {
            error = "checksum";
            return false;
        }

        if (ContainsForbidden(node))
        {
            error = "redaction";
            return false;
        }

        return true;
    }

    private static bool ContainsForbidden(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var prop in obj)
            {
                if (prop.Key.Equals("body", StringComparison.OrdinalIgnoreCase)
                    || prop.Key.Equals("password", StringComparison.OrdinalIgnoreCase)
                    || prop.Key.Equals("secret", StringComparison.OrdinalIgnoreCase)
                    || prop.Key.Equals("token", StringComparison.OrdinalIgnoreCase)
                    || prop.Key.Equals("cookie", StringComparison.OrdinalIgnoreCase)
                    || prop.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase)
                    || prop.Key.Equals("connectionString", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = prop.Value?.ToJsonString() ?? "";
                    if (!raw.Contains("[redacted]", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                if (ContainsForbidden(prop.Value))
                {
                    return true;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (ContainsForbidden(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string StableCode(string summary, string code) =>
        StableCodePattern().IsMatch(summary) ? summary : code;

    [GeneratedRegex(@"^[a-z0-9._]+$", RegexOptions.CultureInvariant)]
    private static partial Regex StableCodePattern();
}
