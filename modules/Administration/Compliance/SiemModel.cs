using System.Text.Json;
using System.Text.Json.Nodes;
using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>B-132. Off by default. A destination never receives another tenant's events.</summary>
public sealed class SiemOptions
{
    public const string SectionName = "Features:Siem";

    public bool Enabled { get; set; }
}

public static class SiemTransports
{
    public const string WebhookJson = "webhook-json";
    public const string SyslogCef = "syslog-cef";

    public static bool Known(string? transport) => transport is WebhookJson or SyslogCef;
}

public static class SiemSeverities
{
    public const string Info = "info";
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";

    public static bool Known(string? severity) =>
        severity is Info or Low or Medium or High or Critical;

    public static int Rank(string? severity) => severity switch
    {
        Critical => 4,
        High => 3,
        Medium => 2,
        Low => 1,
        _ => 0
    };
}

public static class SiemStatuses
{
    public const string Active = "active";
    public const string Revoked = "revoked";
}

public static class SiemCircuits
{
    public const string Closed = "closed";
    public const string Open = "open";
}

public static class SiemDeliveryStatuses
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

public static class SiemErrors
{
    public const string Disabled = "SiemDisabled";
    public const string NotFound = "SiemNotFound";
    public const string Limit = "SiemDestinationLimit";
    public const string EgressBlocked = "SiemEgressBlocked";
    public const string CircuitOpen = "SiemCircuitOpen";
    public const string IdempotencyRequired = "SiemIdempotencyRequired";
    public const string IdempotencyConflict = "SiemIdempotencyConflict";
    public const string TransportInvalid = "SiemTransportInvalid";
    public const string UrlInvalid = "SiemUrlInvalid";
    public const string NameInvalid = "SiemNameInvalid";
    public const string SeverityInvalid = "SiemSeverityInvalid";
    public const string CategoryInvalid = "SiemCategoryInvalid";
    public const string BackfillInvalid = "SiemBackfillInvalid";
    public const string Revoked = "SiemRevoked";
}

public static class SiemLimits
{
    public const string JobRole = "siem";
    public const string EventType = "audit.event.recorded";
    public const int SchemaVersion = 1;
    public const int MaxDestinations = 3;
    public const int MaxNameLength = 80;
    public const int MaxUrlLength = 300;
    public const int MaxCategories = 8;
    public const int MaxCategoryLength = 32;
    public const int MaxIdempotencyKeyLength = 200;
    public const int DrainBatch = 20;
    public const int MaxBackfill = 100;
    public const int DefaultBackfill = 20;
    public const int MaxBackfillHours = 24;
    public const int MaxTransientFailures = 3;
    public static readonly TimeSpan OpenFor = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);
}

public readonly record struct SiemCircuitState(
    string Circuit,
    int Failures,
    DateTimeOffset? OpenUntil,
    DateTimeOffset? NextAttemptAt);

public readonly record struct SiemEnvelope(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string Category,
    string Severity,
    string Action,
    bool Synthetic,
    string? EntityType,
    string? EntityId,
    Guid? ActorId,
    JsonObject? Metadata);

public static class SiemRules
{
    private static readonly string[] SensitiveKeys =
    [
        "body", "secret", "token", "password", "authorization", "cookie", "apikey", "ciphertext", "nonce", "credential"
    ];

    public static (string Category, string Severity) Classify(string? action)
    {
        var value = string.IsNullOrWhiteSpace(action) ? "audit" : action.Trim();
        var dot = value.IndexOf('.');
        var category = (dot < 0 ? value : value[..dot]).ToLowerInvariant();
        if (category.Length == 0)
        {
            category = "audit";
        }

        var severity = value switch
        {
            "settings.credential.rotate" or "settings.encryption.reencrypt" => SiemSeverities.Critical,
            "message.delete" or "member.role.change" or "guest.revoke" or "integration.token.revoke" => SiemSeverities.High,
            _ when value.StartsWith("dlp.", StringComparison.Ordinal)
                || value.StartsWith("siem.", StringComparison.Ordinal)
                || value.StartsWith("policy.", StringComparison.Ordinal)
                || value.StartsWith("scim.token", StringComparison.Ordinal) => SiemSeverities.High,
            "message.send" or "attachment.upload" or "member.invite" => SiemSeverities.Medium,
            "message.pin" or "profile.update" => SiemSeverities.Low,
            _ => SiemSeverities.Info
        };
        return (category, severity);
    }

    public static bool Allows(IReadOnlyCollection<string> categories, string minSeverity, string category, string severity) =>
        SiemSeverities.Rank(severity) >= SiemSeverities.Rank(minSeverity)
        && (categories.Count == 0 || categories.Contains(category, StringComparer.Ordinal));

    public static bool TryCategories(IReadOnlyList<string>? raw, out string[] categories, out string error)
    {
        categories = [];
        error = string.Empty;
        if (raw is null || raw.Count == 0)
        {
            return true;
        }

        if (raw.Count > SiemLimits.MaxCategories)
        {
            error = SiemErrors.CategoryInvalid;
            return false;
        }

        var list = new string[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            var item = raw[i]?.Trim().ToLowerInvariant() ?? string.Empty;
            if (item.Length is 0 or > SiemLimits.MaxCategoryLength || item.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
            {
                error = SiemErrors.CategoryInvalid;
                return false;
            }

            list[i] = item;
        }

        categories = list.Distinct(StringComparer.Ordinal).ToArray();
        return true;
    }

    public static bool TryBackfill(
        DateTimeOffset? since,
        int? limit,
        DateTimeOffset now,
        out DateTimeOffset start,
        out int take,
        out string error)
    {
        take = limit ?? SiemLimits.DefaultBackfill;
        start = now;
        if (take is < 1 or > SiemLimits.MaxBackfill)
        {
            error = SiemErrors.BackfillInvalid;
            return false;
        }

        var floor = now.AddHours(-SiemLimits.MaxBackfillHours);
        start = since ?? floor;
        if (start > now)
        {
            error = SiemErrors.BackfillInvalid;
            return false;
        }

        if (start < floor)
        {
            start = floor;
        }

        error = string.Empty;
        return true;
    }

    public static bool MaySend(string status, string circuit, DateTimeOffset? openUntil, DateTimeOffset? nextAttempt, DateTimeOffset now)
    {
        if (!string.Equals(status, SiemStatuses.Active, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(circuit, SiemCircuits.Open, StringComparison.Ordinal)
            && openUntil is DateTimeOffset until
            && until > now)
        {
            return false;
        }

        return nextAttempt is not DateTimeOffset next || next <= now;
    }

    public static SiemCircuitState Record(int statusCode, int failures, DateTimeOffset now)
    {
        if (statusCode is >= 200 and < 300)
        {
            return new SiemCircuitState(SiemCircuits.Closed, 0, null, null);
        }

        var next = failures + 1;
        var transient = statusCode is >= 500 and < 600;
        if (!transient || next >= SiemLimits.MaxTransientFailures)
        {
            var until = now.Add(SiemLimits.OpenFor);
            return new SiemCircuitState(SiemCircuits.Open, next, until, until);
        }

        var delaySeconds = Math.Min(30, Math.Pow(2, next));
        return new SiemCircuitState(SiemCircuits.Closed, next, null, now.Add(TimeSpan.FromSeconds(delaySeconds)));
    }

    public static JsonObject? RedactMetadata(string? metadataJson, bool minimize, string? secret)
    {
        if (minimize || string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(metadataJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (node is not JsonObject obj)
        {
            return null;
        }

        var copy = Strip(obj, secret);
        return copy.Count == 0 ? null : copy;
    }

    public static string BuildJson(SiemEnvelope envelope)
    {
        var root = new JsonObject
        {
            ["eventType"] = SiemLimits.EventType,
            ["schemaVersion"] = SiemLimits.SchemaVersion,
            ["eventId"] = envelope.EventId.ToString("D"),
            ["occurredAt"] = envelope.OccurredAt.ToString("O"),
            ["category"] = envelope.Category,
            ["severity"] = envelope.Severity,
            ["action"] = envelope.Action,
            ["synthetic"] = envelope.Synthetic
        };
        if (!string.IsNullOrWhiteSpace(envelope.EntityType))
        {
            root["entityType"] = envelope.EntityType;
        }

        if (!string.IsNullOrWhiteSpace(envelope.EntityId))
        {
            root["entityId"] = envelope.EntityId;
        }

        if (envelope.ActorId is Guid actor)
        {
            root["actorId"] = actor.ToString("D");
        }

        if (envelope.Metadata is JsonObject metadata && metadata.Count > 0)
        {
            root["metadata"] = metadata;
        }

        return root.ToJsonString();
    }

    public static string BuildCef(SiemEnvelope envelope)
    {
        var severity = envelope.Severity switch
        {
            SiemSeverities.Critical => "10",
            SiemSeverities.High => "7",
            SiemSeverities.Medium => "5",
            SiemSeverities.Low => "3",
            _ => "1"
        };
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"CEF:0|VibeChat|Audit|1|{Escape(envelope.Action)}|{Escape(envelope.Action)}|{severity}|rt={envelope.OccurredAt:O} cat={Escape(envelope.Category)} cs1={envelope.EventId:D} cs1Label=eventId");
    }

    public static int LagSeconds(DateTimeOffset? oldestPending, DateTimeOffset now) =>
        oldestPending is DateTimeOffset pending
            ? (int)Math.Max(0, (now - pending).TotalSeconds)
            : 0;

    private static JsonObject Strip(JsonObject source, string? secret)
    {
        var copy = new JsonObject();
        foreach (var pair in source)
        {
            if (IsSensitiveKey(pair.Key))
            {
                continue;
            }

            if (pair.Value is JsonObject child)
            {
                var nested = Strip(child, secret);
                if (nested.Count > 0)
                {
                    copy[pair.Key] = nested;
                }

                continue;
            }

            if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && ContainsSecret(text, secret))
            {
                continue;
            }

            copy[pair.Key] = pair.Value?.DeepClone();
        }

        return copy;
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return SensitiveKeys.Any(item => normalized.Contains(item, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSecret(string text, string? secret) =>
        text.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrEmpty(secret) && text.Contains(secret, StringComparison.Ordinal));

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal);
}

public sealed class SiemDestination
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = SiemTransports.WebhookJson;
    public string Url { get; set; } = string.Empty;
    public EncryptedSecretEnvelope SigningSecret { get; set; } = new();
    public string CategoriesJson { get; set; } = "[]";
    public string MinSeverity { get; set; } = SiemSeverities.Info;
    public bool Minimize { get; set; } = true;
    public string Status { get; set; } = SiemStatuses.Active;
    public string Circuit { get; set; } = SiemCircuits.Closed;
    public int FailureCount { get; set; }
    public DateTimeOffset? OpenUntil { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CheckpointAt { get; set; }
    public Guid? CheckpointEventId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? RotateKey { get; set; }
    public string? TestKey { get; set; }
    public Guid? TestEventId { get; set; }
    public string? BackfillKey { get; set; }
    public int BackfillCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class SiemDelivery
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid DestinationId { get; set; }
    public Guid EventId { get; set; }
    public string Status { get; set; } = SiemDeliveryStatuses.Pending;
    public int Attempts { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
