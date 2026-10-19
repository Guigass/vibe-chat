using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VibeChat.Integrations;

/// <summary>
/// Repo-native public event catalog (B-139 / ADR-041). Not an external schema registry.
/// </summary>
public static class PublicEventCatalog
{
    public const int MaxDocumentBytes = 16_384;
    public static readonly DateOnly IntroducedOn = new(2026, 10, 19);
    public static readonly DateOnly MessageCreatedV1RetiresOn = new(2027, 4, 19);

    public const string MessageCreated = "messaging.message.created";
    public const string MessageEdited = "messaging.message.edited";
    public const string MessageDeleted = "messaging.message.deleted";
    public const string ReactionChanged = "messaging.message.reaction_changed";
    public const string WebhookTest = "integrations.webhook.test";

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<PublicEventVersion> All = Build();

    public static IReadOnlyList<PublicEventVersion> Versions => All;

    public static EventSchemaDecision Accept(string? wireOrType, string? payloadJson)
    {
        var matches = Find(wireOrType);
        if (matches.Count == 0)
        {
            return Reject(EventSchemaReasons.UnknownEventType, "unknown", 0);
        }

        var eventType = matches[0].EventType;
        if (payloadJson is null || Encoding.UTF8.GetByteCount(payloadJson) > MaxDocumentBytes)
        {
            return Reject(EventSchemaReasons.SchemaDocumentTooLarge, eventType, 0);
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return Reject(EventSchemaReasons.SchemaMismatch, eventType, 0);
        }

        var version = matches.Min(item => item.SchemaVersion);
        if (node is JsonObject root
            && root["schemaVersion"] is JsonValue declared
            && EventSchemaSubset.TryReadInt(declared, out var declaredVersion))
        {
            version = declaredVersion;
        }

        var selected = matches.FirstOrDefault(item => item.SchemaVersion == version);
        if (selected is null)
        {
            return Reject(EventSchemaReasons.UnknownSchemaVersion, eventType, version);
        }

        if (!EventSchemaSubset.TryValidate(selected.Schema, node, out _))
        {
            return Reject(EventSchemaReasons.SchemaMismatch, eventType, version);
        }

        return new EventSchemaDecision(true, EventSchemaReasons.Accepted, eventType, version);
    }

    public static EventSchemaDecision Validate(string? eventType, int schemaVersion, string? documentJson)
    {
        if (documentJson is null || Encoding.UTF8.GetByteCount(documentJson) > MaxDocumentBytes)
        {
            var known = Find(eventType);
            var type = known.Count == 0 ? "unknown" : known[0].EventType;
            return Reject(EventSchemaReasons.SchemaDocumentTooLarge, type, schemaVersion);
        }

        var matches = Find(eventType);
        if (matches.Count == 0)
        {
            return Reject(EventSchemaReasons.UnknownEventType, "unknown", schemaVersion);
        }

        var selected = matches.FirstOrDefault(item => item.SchemaVersion == schemaVersion);
        if (selected is null)
        {
            return Reject(EventSchemaReasons.UnknownSchemaVersion, matches[0].EventType, schemaVersion);
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(documentJson);
        }
        catch (JsonException)
        {
            return Reject(EventSchemaReasons.SchemaMismatch, selected.EventType, schemaVersion);
        }

        if (!EventSchemaSubset.TryValidate(selected.Schema, node, out _))
        {
            return Reject(EventSchemaReasons.SchemaMismatch, selected.EventType, schemaVersion);
        }

        return new EventSchemaDecision(true, EventSchemaReasons.Accepted, selected.EventType, schemaVersion);
    }

    /// <summary>
    /// Projects messaging.message.created v1 into the v2 envelope. Other pairs return null.
    /// </summary>
    public static string? Project(
        string eventType,
        int fromVersion,
        int toVersion,
        string documentJson,
        Guid eventId,
        Guid correlationId,
        DateTimeOffset occurredAt)
    {
        if (!string.Equals(eventType, MessageCreated, StringComparison.Ordinal)
            || fromVersion != 1
            || toVersion != 2)
        {
            return null;
        }

        if (!Validate(eventType, fromVersion, documentJson).Accepted)
        {
            return null;
        }

        var payload = JsonNode.Parse(documentJson)!.AsObject();
        var envelope = new JsonObject
        {
            ["eventId"] = eventId.ToString("D"),
            ["eventType"] = MessageCreated,
            ["occurredAt"] = occurredAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["tenantId"] = payload["tenantId"]!.GetValue<string>(),
            ["aggregateId"] = payload["messageId"]!.GetValue<string>(),
            ["correlationId"] = correlationId.ToString("D"),
            ["schemaVersion"] = 2,
            ["payload"] = payload
        };
        return envelope.ToJsonString();
    }

    public static string ExportJson()
    {
        var versions = new JsonArray();
        foreach (var version in All)
        {
            versions.Add(new JsonObject
            {
                ["eventType"] = version.EventType,
                ["schemaVersion"] = version.SchemaVersion,
                ["wireName"] = version.WireName,
                ["status"] = version.Status == PublicEventStatus.Deprecated ? "deprecated" : "active",
                ["deprecatedUntil"] = version.DeprecatedUntil?.ToString("yyyy-MM-dd"),
                ["changelog"] = version.Changelog,
                ["schema"] = version.Schema.DeepClone(),
                ["example"] = version.Example.DeepClone()
            });
        }

        return new JsonObject { ["versions"] = versions }.ToJsonString(CanonicalJson) + "\n";
    }

    public static string RenderMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Eventos públicos versionados");
        builder.AppendLine();
        builder.AppendLine("Fonte canônica: `PublicEventCatalog` (B-139 / ADR-041). Este arquivo é a");
        builder.AppendLine("saída de `PublicEventCatalog.RenderMarkdown()`; o teste de unidade falha se divergir.");
        builder.AppendLine();
        builder.AppendLine("O registry é um artefato do repositório e uma API da própria aplicação.");
        builder.AppendLine("Não há Kafka nem serviço externo de schema (ADR-015).");
        builder.AppendLine();
        builder.AppendLine("Flag `Integrations:EventSchemas:Enabled` nasce **false**. Desligada, o webhook");
        builder.AppendLine("entrega o body atual sem checagem e `GET/POST /api/v1/admin/event-schemas`");
        builder.AppendLine("responde 404 `EventSchemasDisabled`. Ligada, a entrega valida o body contra a");
        builder.AppendLine("versão declarada (ou a v1, se o JSON não trouxer `schemaVersion`) e recusa");
        builder.AppendLine("tipo ou versão desconhecidos sem chamar a URL e sem relançar para a outbox.");
        builder.AppendLine();
        builder.AppendLine("## Kit do consumidor");
        builder.AppendLine();
        builder.AppendLine("- `PublicEventCatalog.Validate(eventType, schemaVersion, json)`");
        builder.AppendLine("- `PublicEventCatalog.Project` só cobre `messaging.message.created` de v1 para v2");
        builder.AppendLine("- `POST /api/v1/admin/event-schemas/check` com `workspace.admin`");
        builder.AppendLine("- documento acima de 16 KiB → `SchemaDocumentTooLarge`");
        builder.AppendLine();
        builder.AppendLine("## Catálogo");
        builder.AppendLine();
        foreach (var version in All)
        {
            builder.AppendLine($"### {version.EventType} v{version.SchemaVersion}");
            builder.AppendLine();
            builder.AppendLine($"- Wire: `{version.WireName}`");
            builder.AppendLine($"- Status: {(version.Status == PublicEventStatus.Deprecated ? "deprecated" : "active")}");
            if (version.DeprecatedUntil is DateOnly until)
            {
                builder.AppendLine($"- Janela: até {until:yyyy-MM-dd} (inclusive o dia anterior; a partir dessa data a versão pode sair)");
            }

            builder.AppendLine($"- Changelog: {version.Changelog}");
            builder.AppendLine();
            builder.AppendLine("Exemplo:");
            builder.AppendLine();
            builder.AppendLine("```json");
            builder.AppendLine(version.Example.ToJsonString(CanonicalJson));
            builder.AppendLine("```");
            builder.AppendLine();
            builder.AppendLine("Schema:");
            builder.AppendLine();
            builder.AppendLine("```json");
            builder.AppendLine(version.Schema.ToJsonString(CanonicalJson));
            builder.AppendLine("```");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static List<PublicEventVersion> Find(string? wireOrType)
    {
        if (string.IsNullOrWhiteSpace(wireOrType))
        {
            return [];
        }

        return All.Where(item =>
                string.Equals(item.WireName, wireOrType, StringComparison.Ordinal)
                || string.Equals(item.EventType, wireOrType, StringComparison.Ordinal))
            .ToList();
    }

    private static EventSchemaDecision Reject(string reason, string eventType, int schemaVersion) =>
        new(false, reason, eventType, schemaVersion);

    private static IReadOnlyList<PublicEventVersion> Build()
    {
        var createdV1 = Version(
            MessageCreated,
            1,
            WebhookEventTypes.MessageCreated,
            PublicEventStatus.Deprecated,
            MessageCreatedV1RetiresOn,
            "Wire payload already delivered to webhook consumers. Deprecated in favor of the v2 envelope; both stay valid through 2027-04-18.",
            ObjectSchema(
                additionalProperties: true,
                ["tenantId", "channelId", "messageId", "sequence", "authorId", "createdAt"],
                ("tenantId", StringSchema("uuid")),
                ("channelId", StringSchema("uuid")),
                ("messageId", StringSchema("uuid")),
                ("sequence", IntegerSchema()),
                ("authorId", StringSchema("uuid")),
                ("createdAt", StringSchema("date-time"))),
            Example(
                ("tenantId", "00000000-0000-0000-0000-000000000001"),
                ("channelId", "00000000-0000-0000-0000-000000000002"),
                ("messageId", "00000000-0000-0000-0000-000000000003"),
                ("authorId", "00000000-0000-0000-0000-000000000004"),
                ("createdAt", "2026-10-19T12:00:00Z"),
                sequence: 1));

        var createdV2 = Version(
            MessageCreated,
            2,
            WebhookEventTypes.MessageCreated,
            PublicEventStatus.Active,
            null,
            "Common envelope. payload is a v1 document. Coexists with v1 until the deprecation date.",
            ObjectSchema(
                additionalProperties: false,
                ["eventId", "eventType", "occurredAt", "tenantId", "aggregateId", "correlationId", "schemaVersion", "payload"],
                ("eventId", StringSchema("uuid")),
                ("eventType", EnumSchema(MessageCreated)),
                ("occurredAt", StringSchema("date-time")),
                ("tenantId", StringSchema("uuid")),
                ("aggregateId", StringSchema("uuid")),
                ("correlationId", StringSchema("uuid")),
                ("schemaVersion", IntegerSchema(minimum: 2, maximum: 2)),
                ("payload", (JsonObject)createdV1.Schema.DeepClone())),
            EnvelopeExample(createdV1.Example));

        return
        [
            createdV1,
            createdV2,
            Wire(
                MessageEdited,
                WebhookEventTypes.MessageEdited,
                ["tenantId", "channelId", "messageId", "sequence", "editedAt"],
                ("tenantId", StringSchema("uuid")),
                ("channelId", StringSchema("uuid")),
                ("messageId", StringSchema("uuid")),
                ("sequence", IntegerSchema()),
                ("editedAt", StringSchema("date-time")),
                Example(
                    ("tenantId", "00000000-0000-0000-0000-000000000001"),
                    ("channelId", "00000000-0000-0000-0000-000000000002"),
                    ("messageId", "00000000-0000-0000-0000-000000000003"),
                    ("editedAt", "2026-10-19T12:00:00Z"),
                    sequence: 2)),
            Wire(
                MessageDeleted,
                WebhookEventTypes.MessageDeleted,
                ["tenantId", "channelId", "messageId", "sequence", "deletedAt"],
                ("tenantId", StringSchema("uuid")),
                ("channelId", StringSchema("uuid")),
                ("messageId", StringSchema("uuid")),
                ("sequence", IntegerSchema()),
                ("deletedAt", StringSchema("date-time")),
                Example(
                    ("tenantId", "00000000-0000-0000-0000-000000000001"),
                    ("channelId", "00000000-0000-0000-0000-000000000002"),
                    ("messageId", "00000000-0000-0000-0000-000000000003"),
                    ("deletedAt", "2026-10-19T12:00:00Z"),
                    sequence: 3)),
            Wire(
                ReactionChanged,
                WebhookEventTypes.ReactionChanged,
                ["tenantId", "channelId", "messageId", "userId", "emoji", "added"],
                ("tenantId", StringSchema("uuid")),
                ("channelId", StringSchema("uuid")),
                ("messageId", StringSchema("uuid")),
                ("userId", StringSchema("uuid")),
                ("emoji", StringSchema()),
                ("added", BoolSchema()),
                ReactionExample()),
            Wire(
                WebhookTest,
                WebhookEventTypes.WebhookTest,
                ["type", "tenantId", "endpointId", "sentAt"],
                ("type", EnumSchema(WebhookEventTypes.WebhookTest)),
                ("tenantId", StringSchema("uuid")),
                ("endpointId", StringSchema("uuid")),
                ("sentAt", StringSchema("date-time")),
                WebhookTestExample())
        ];
    }

    private static PublicEventVersion Wire(
        string eventType,
        string wireName,
        string[] required,
        (string Name, JsonObject Schema) p1,
        (string Name, JsonObject Schema) p2,
        (string Name, JsonObject Schema) p3,
        (string Name, JsonObject Schema) p4,
        JsonObject example) =>
        Version(
            eventType,
            1,
            wireName,
            PublicEventStatus.Active,
            null,
            "Initial public schema for the current webhook body. Additional properties remain allowed.",
            ObjectSchema(true, required, p1, p2, p3, p4),
            example);

    private static PublicEventVersion Wire(
        string eventType,
        string wireName,
        string[] required,
        (string Name, JsonObject Schema) p1,
        (string Name, JsonObject Schema) p2,
        (string Name, JsonObject Schema) p3,
        (string Name, JsonObject Schema) p4,
        (string Name, JsonObject Schema) p5,
        JsonObject example) =>
        Version(
            eventType,
            1,
            wireName,
            PublicEventStatus.Active,
            null,
            "Initial public schema for the current webhook body. Additional properties remain allowed.",
            ObjectSchema(true, required, p1, p2, p3, p4, p5),
            example);

    private static PublicEventVersion Wire(
        string eventType,
        string wireName,
        string[] required,
        (string Name, JsonObject Schema) p1,
        (string Name, JsonObject Schema) p2,
        (string Name, JsonObject Schema) p3,
        (string Name, JsonObject Schema) p4,
        (string Name, JsonObject Schema) p5,
        (string Name, JsonObject Schema) p6,
        JsonObject example) =>
        Version(
            eventType,
            1,
            wireName,
            PublicEventStatus.Active,
            null,
            "Initial public schema for the current webhook body. Additional properties remain allowed.",
            ObjectSchema(true, required, p1, p2, p3, p4, p5, p6),
            example);

    private static PublicEventVersion Version(
        string eventType,
        int schemaVersion,
        string wireName,
        PublicEventStatus status,
        DateOnly? deprecatedUntil,
        string changelog,
        JsonObject schema,
        JsonObject example) =>
        new(eventType, schemaVersion, wireName, status, deprecatedUntil, changelog, schema, example);

    private static JsonObject ObjectSchema(
        bool additionalProperties,
        string[] required,
        params (string Name, JsonObject Schema)[] properties)
    {
        var requiredNode = new JsonArray();
        foreach (var name in required)
        {
            requiredNode.Add(name);
        }

        var props = new JsonObject();
        foreach (var (name, schema) in properties)
        {
            props[name] = schema;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = additionalProperties,
            ["required"] = requiredNode,
            ["properties"] = props
        };
    }

    private static JsonObject StringSchema(string? format = null)
    {
        var schema = new JsonObject { ["type"] = "string" };
        if (format is not null)
        {
            schema["format"] = format;
        }

        return schema;
    }

    private static JsonObject IntegerSchema(int? minimum = null, int? maximum = null)
    {
        var schema = new JsonObject { ["type"] = "integer" };
        if (minimum is int min)
        {
            schema["minimum"] = min;
        }

        if (maximum is int max)
        {
            schema["maximum"] = max;
        }

        return schema;
    }

    private static JsonObject BoolSchema() => new() { ["type"] = "boolean" };

    private static JsonObject EnumSchema(string value) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(JsonValue.Create(value))
    };

    private static JsonObject Example(
        (string Name, string Value) a,
        (string Name, string Value) b,
        (string Name, string Value) c,
        (string Name, string Value) d,
        int sequence) =>
        new()
        {
            [a.Name] = a.Value,
            [b.Name] = b.Value,
            [c.Name] = c.Value,
            ["sequence"] = sequence,
            [d.Name] = d.Value
        };

    private static JsonObject Example(
        (string Name, string Value) a,
        (string Name, string Value) b,
        (string Name, string Value) c,
        (string Name, string Value) d,
        (string Name, string Value) e,
        int sequence) =>
        new()
        {
            [a.Name] = a.Value,
            [b.Name] = b.Value,
            [c.Name] = c.Value,
            ["sequence"] = sequence,
            [d.Name] = d.Value,
            [e.Name] = e.Value
        };

    private static JsonObject ReactionExample() => new()
    {
        ["tenantId"] = "00000000-0000-0000-0000-000000000001",
        ["channelId"] = "00000000-0000-0000-0000-000000000002",
        ["messageId"] = "00000000-0000-0000-0000-000000000003",
        ["userId"] = "00000000-0000-0000-0000-000000000004",
        ["emoji"] = "thumbsup",
        ["added"] = true
    };

    private static JsonObject WebhookTestExample() => new()
    {
        ["type"] = WebhookEventTypes.WebhookTest,
        ["tenantId"] = "00000000-0000-0000-0000-000000000001",
        ["endpointId"] = "00000000-0000-0000-0000-000000000007",
        ["sentAt"] = "2026-10-19T12:00:00Z"
    };

    private static JsonObject EnvelopeExample(JsonObject payload) => new()
    {
        ["eventId"] = "00000000-0000-0000-0000-000000000005",
        ["eventType"] = MessageCreated,
        ["occurredAt"] = "2026-10-19T12:00:00Z",
        ["tenantId"] = "00000000-0000-0000-0000-000000000001",
        ["aggregateId"] = "00000000-0000-0000-0000-000000000003",
        ["correlationId"] = "00000000-0000-0000-0000-000000000006",
        ["schemaVersion"] = 2,
        ["payload"] = payload.DeepClone()
    };
}

public enum PublicEventStatus
{
    Active,
    Deprecated
}

public sealed record PublicEventVersion(
    string EventType,
    int SchemaVersion,
    string WireName,
    PublicEventStatus Status,
    DateOnly? DeprecatedUntil,
    string Changelog,
    JsonObject Schema,
    JsonObject Example);

public sealed record EventSchemaDecision(bool Accepted, string Reason, string EventType, int SchemaVersion);

public static class EventSchemaReasons
{
    public const string Accepted = "Accepted";
    public const string UnknownEventType = "UnknownEventType";
    public const string UnknownSchemaVersion = "UnknownSchemaVersion";
    public const string SchemaMismatch = "SchemaMismatch";
    public const string SchemaDocumentTooLarge = "SchemaDocumentTooLarge";
    public const string Disabled = "EventSchemasDisabled";
}

public sealed class EventSchemaOptions
{
    public const string SectionName = "Integrations:EventSchemas";

    public bool Enabled { get; set; }
}
