using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VibeChat.SharedKernel;

namespace VibeChat.AI;

public enum AiSkillStatus
{
    Draft,
    Published,
    Deprecated
}

/// <summary>Workspace skill definition. The published snapshot lives in <see cref="AiSkillVersion"/>.</summary>
public sealed class AiSkillDefinition
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public AiSkillStatus Status { get; set; }
    public int? ActiveVersionNumber { get; set; }
    public int Revision { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string ArgumentsSchemaJson { get; set; } = """{"type":"object"}""";
    public string ExamplesJson { get; set; } = "[]";
    public UserId OwnerUserId { get; set; }
    public string Compatibility { get; set; } = AiSkillCatalogPolicies.DefaultCompatibility;
    public string Classification { get; set; } = "internal";
    public Guid[] KnowledgeGrantIds { get; set; } = [];
    public Guid[] McpToolGrantIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AiSkillVersion
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid SkillId { get; set; }
    public int VersionNumber { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string InstructionsSha256 { get; set; } = string.Empty;
    public string ArgumentsSchemaJson { get; set; } = """{"type":"object"}""";
    public string ExamplesJson { get; set; } = "[]";
    public UserId OwnerUserId { get; set; }
    public string Compatibility { get; set; } = AiSkillCatalogPolicies.DefaultCompatibility;
    public string Classification { get; set; } = "internal";
    public Guid[] KnowledgeGrantIds { get; set; } = [];
    public Guid[] McpToolGrantIds { get; set; } = [];
    public UserId PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed record AiSkillDraft(
    string Slug,
    string Name,
    string Description,
    string Instructions,
    string ArgumentsSchemaJson,
    string ExamplesJson,
    string Compatibility,
    string Classification,
    Guid[] KnowledgeGrantIds,
    Guid[] McpToolGrantIds);

public sealed record AiSkillImport(
    string Slug,
    int Version,
    string Name,
    string Description,
    string Instructions,
    string ArgumentsSchemaJson,
    string ExamplesJson,
    string Compatibility,
    string Classification,
    Guid[] KnowledgeGrantIds,
    Guid[] McpToolGrantIds);

public sealed record AiSkillFieldChange(string Field, string? Before, string? After);

public sealed record AiSkillPromptLayer(int Order, string Layer, Guid? SkillVersionId, string? InstructionsSha256);

public sealed class AiSkillDraftInput
{
    public string? Slug { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public JsonElement ArgumentsSchema { get; set; }
    public JsonElement Examples { get; set; }
    public string? Compatibility { get; set; }
    public string? Classification { get; set; }
    public Guid[]? KnowledgeGrantIds { get; set; }
    public Guid[]? McpToolGrantIds { get; set; }
}

public static class AiSkillCatalogPolicies
{
    public const string ManifestSchema = "vibechat.bot.skill.v1";
    public const string DefaultCompatibility = "bots.v1";
    public const int MaxSkillsPerWorkspace = 50;
    public const int MaxNameLength = 80;
    public const int MaxSlugLength = 64;
    public const int MaxDescriptionLength = 280;
    public const int MaxInstructionsLength = 8000;
    public const int MaxSchemaLength = 4000;
    public const int MaxExamplesLength = 4000;
    public const int MaxExamples = 8;
    public const int MaxCompatibilityLength = 64;
    public const int MaxReferences = 32;
    public const int DraftVersion = 0;

    public static readonly string[] Classifications = ["public", "internal", "confidential", "restricted"];
    public static readonly string[] ManifestProperties =
    [
        "schema", "slug", "version", "name", "description", "instructions",
        "argumentsSchema", "examples", "compatibility", "classification",
        "knowledgeGrantIds", "mcpToolGrantIds", "ownerUserId"
    ];

    private static readonly string[] SecretNames =
        ["apikey", "api_key", "secret", "token", "authorization", "password", "bearer", "credential"];

    private static readonly string[] CodeNames =
        ["script", "code", "eval", "package", "entrypoint", "dll", "module", "container", "capabilities", "capability"];

    private static readonly string[] ForbiddenInstructions =
    [
        "ignore previous",
        "ignore all previous",
        "disregard previous",
        "disable guardrail",
        "remove guardrail",
        "bypass guardrail",
        "sem guardrail",
        "ignore as instruções",
        "grant capability",
        "elevate tool",
        "grant tool",
        "ai.bot.manage",
        "workspace.admin"
    ];

    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions CanonicalJson = new() { WriteIndented = false };

    public static string? RejectSecretOrCode(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (IsForbiddenName(property.Name))
                {
                    return IsSecretName(property.Name) ? AiSkillErrors.SkillSecretRejected : AiSkillErrors.SkillCodeRejected;
                }

                var nested = RejectSecretOrCode(property.Value);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                var nested = RejectSecretOrCode(item);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    public static bool TryNormalize(AiSkillDraftInput input, bool publishing, out AiSkillDraft draft, out string? error)
    {
        draft = null!;
        if (!TryCore(
                input.Slug,
                input.Name,
                input.Description,
                input.Instructions,
                input.ArgumentsSchema,
                input.Examples,
                input.Compatibility,
                input.Classification,
                input.KnowledgeGrantIds,
                input.McpToolGrantIds,
                publishing,
                out var slug,
                out var name,
                out var description,
                out var instructions,
                out var schema,
                out var examples,
                out var compatibility,
                out var classification,
                out var knowledge,
                out var tools,
                out error))
        {
            return false;
        }

        draft = new AiSkillDraft(slug, name, description, instructions, schema, examples, compatibility, classification, knowledge, tools);
        return true;
    }

    public static bool TryImport(JsonElement body, out AiSkillImport imported, out string? error)
    {
        imported = null!;
        error = RejectSecretOrCode(body);
        if (error is not null)
        {
            return false;
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            error = AiSkillErrors.SkillManifestRejected;
            return false;
        }

        foreach (var property in body.EnumerateObject())
        {
            if (!ManifestProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                error = AiSkillErrors.SkillManifestRejected;
                return false;
            }
        }

        if (!body.TryGetProperty("schema", out var schemaName)
            || schemaName.ValueKind != JsonValueKind.String
            || schemaName.GetString() != ManifestSchema)
        {
            error = AiSkillErrors.SkillManifestRejected;
            return false;
        }

        if (!body.TryGetProperty("version", out var versionNode) || !versionNode.TryGetInt32(out var version) || version < 1)
        {
            error = AiSkillErrors.SkillManifestRejected;
            return false;
        }

        var arguments = body.TryGetProperty("argumentsSchema", out var argumentsNode) ? argumentsNode : default;
        var examples = body.TryGetProperty("examples", out var examplesNode) ? examplesNode : default;
        Guid[]? knowledge = null;
        Guid[]? tools = null;
        if (body.TryGetProperty("knowledgeGrantIds", out var knowledgeNode) && !TryGuidArray(knowledgeNode, out knowledge))
        {
            error = AiSkillErrors.SkillManifestRejected;
            return false;
        }

        if (body.TryGetProperty("mcpToolGrantIds", out var toolNode) && !TryGuidArray(toolNode, out tools))
        {
            error = AiSkillErrors.SkillManifestRejected;
            return false;
        }

        if (!TryCore(
                Text(body, "slug"),
                Text(body, "name"),
                Text(body, "description"),
                Text(body, "instructions"),
                arguments,
                examples,
                Text(body, "compatibility"),
                Text(body, "classification"),
                knowledge,
                tools,
                publishing: true,
                out var slug,
                out var name,
                out var description,
                out var instructions,
                out var schema,
                out var exampleJson,
                out var compatibility,
                out var classification,
                out var knowledgeIds,
                out var toolIds,
                out error))
        {
            return false;
        }

        imported = new AiSkillImport(slug, version, name, description, instructions, schema, exampleJson, compatibility, classification, knowledgeIds, toolIds);
        return true;
    }

    public static string ExportManifest(AiSkillVersion version)
    {
        var document = new JsonObject
        {
            ["schema"] = ManifestSchema,
            ["slug"] = version.Slug,
            ["version"] = version.VersionNumber,
            ["name"] = version.Name,
            ["description"] = version.Description,
            ["instructions"] = version.Instructions,
            ["argumentsSchema"] = JsonNode.Parse(version.ArgumentsSchemaJson),
            ["examples"] = JsonNode.Parse(version.ExamplesJson),
            ["compatibility"] = version.Compatibility,
            ["classification"] = version.Classification,
            ["knowledgeGrantIds"] = new JsonArray(version.KnowledgeGrantIds.Select(id => (JsonNode)id.ToString("D")).ToArray()),
            ["mcpToolGrantIds"] = new JsonArray(version.McpToolGrantIds.Select(id => (JsonNode)id.ToString("D")).ToArray()),
            ["ownerUserId"] = version.OwnerUserId.Value.ToString("D")
        };
        return document.ToJsonString(CanonicalJson);
    }

    public static bool TryCompose(
        IReadOnlyList<(Guid Id, string Instructions, string Hash)> skills,
        out IReadOnlyList<AiSkillPromptLayer> layers,
        out string? error)
    {
        layers = [];
        var built = new List<AiSkillPromptLayer>
        {
            new(1, "platform", null, null),
            new(2, "workspace", null, null),
            new(3, "bot", null, null)
        };
        var order = 4;
        foreach (var skill in skills)
        {
            error = InstructionError(skill.Instructions);
            if (error is not null)
            {
                layers = [];
                return false;
            }

            built.Add(new AiSkillPromptLayer(order, "skill", skill.Id, skill.Hash));
            order++;
        }

        layers = built;
        error = null;
        return true;
    }

    public static int NextVersion(IEnumerable<int> published) =>
        published.DefaultIfEmpty(0).Max() + 1;

    public static bool KeepsHistory(IReadOnlyCollection<int> before, IReadOnlyCollection<int> after) =>
        before.All(after.Contains) && after.Count >= before.Count;

    public static string? QuotaError(int skillCount, bool creating) =>
        creating && skillCount >= MaxSkillsPerWorkspace ? AiSkillErrors.SkillQuotaExceeded : null;

    public static IReadOnlyList<AiSkillFieldChange> Diff(AiSkillDraft left, AiSkillDraft right)
    {
        var changes = new List<AiSkillFieldChange>();
        Add(changes, "slug", left.Slug, right.Slug);
        Add(changes, "name", left.Name, right.Name);
        Add(changes, "description", left.Description, right.Description);
        Add(changes, "instructions", left.Instructions, right.Instructions);
        Add(changes, "argumentsSchema", left.ArgumentsSchemaJson, right.ArgumentsSchemaJson);
        Add(changes, "examples", left.ExamplesJson, right.ExamplesJson);
        Add(changes, "compatibility", left.Compatibility, right.Compatibility);
        Add(changes, "classification", left.Classification, right.Classification);
        Add(changes, "knowledgeGrantIds", Join(left.KnowledgeGrantIds), Join(right.KnowledgeGrantIds));
        Add(changes, "mcpToolGrantIds", Join(left.McpToolGrantIds), Join(right.McpToolGrantIds));
        return changes;
    }

    public static string InstructionsHash(string instructions)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(instructions));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string EventDocument(
        string eventType,
        Guid skillId,
        Guid workspaceId,
        Guid tenantId,
        int version,
        Guid actorId,
        string instructionsSha256,
        string slug)
    {
        var document = new JsonObject
        {
            ["skillId"] = skillId.ToString("D"),
            ["workspaceId"] = workspaceId.ToString("D"),
            ["tenantId"] = tenantId.ToString("D"),
            ["version"] = version,
            ["actorId"] = actorId.ToString("D"),
            ["instructionsSha256"] = instructionsSha256,
            ["slug"] = slug
        };
        return document.ToJsonString();
    }

    public static AiSkillDraft FromVersion(AiSkillVersion version) => new(
        version.Slug,
        version.Name,
        version.Description,
        version.Instructions,
        version.ArgumentsSchemaJson,
        version.ExamplesJson,
        version.Compatibility,
        version.Classification,
        version.KnowledgeGrantIds.ToArray(),
        version.McpToolGrantIds.ToArray());

    private static bool TryCore(
        string? slugRaw,
        string? nameRaw,
        string? descriptionRaw,
        string? instructionsRaw,
        JsonElement arguments,
        JsonElement examples,
        string? compatibilityRaw,
        string? classificationRaw,
        Guid[]? knowledgeRaw,
        Guid[]? toolsRaw,
        bool publishing,
        out string slug,
        out string name,
        out string description,
        out string instructions,
        out string schema,
        out string exampleJson,
        out string compatibility,
        out string classification,
        out Guid[] knowledge,
        out Guid[] tools,
        out string? error)
    {
        slug = (slugRaw ?? string.Empty).Trim().ToLowerInvariant();
        name = (nameRaw ?? string.Empty).Trim();
        description = (descriptionRaw ?? string.Empty).Trim();
        instructions = (instructionsRaw ?? string.Empty).Trim();
        schema = """{"type":"object"}""";
        exampleJson = "[]";
        compatibility = string.IsNullOrWhiteSpace(compatibilityRaw) ? DefaultCompatibility : compatibilityRaw.Trim();
        classification = string.IsNullOrWhiteSpace(classificationRaw) ? "internal" : classificationRaw.Trim().ToLowerInvariant();
        knowledge = [];
        tools = [];
        error = null;

        if (!SlugPattern.IsMatch(slug) || slug.Length > MaxSlugLength)
        {
            error = AiSkillErrors.InvalidSkillSlug;
            return false;
        }

        if (name.Length is < 1 or > MaxNameLength)
        {
            error = AiSkillErrors.InvalidSkillName;
            return false;
        }

        if (description.Length > MaxDescriptionLength)
        {
            error = AiSkillErrors.InvalidSkillDescription;
            return false;
        }

        if (instructions.Length > MaxInstructionsLength || (publishing && instructions.Length == 0))
        {
            error = AiSkillErrors.InvalidSkillInstructions;
            return false;
        }

        error = InstructionError(instructions);
        if (error is not null)
        {
            return false;
        }

        if (!TrySchema(arguments, out schema, out error) || !TryExamples(examples, out exampleJson, out error))
        {
            return false;
        }

        if (compatibility.Length is < 1 or > MaxCompatibilityLength || compatibility.Contains("://", StringComparison.Ordinal))
        {
            error = AiSkillErrors.InvalidSkillCompatibility;
            return false;
        }

        if (!Classifications.Contains(classification, StringComparer.Ordinal))
        {
            error = AiSkillErrors.InvalidSkillClassification;
            return false;
        }

        if (!TryIds(knowledgeRaw, out knowledge, out error) || !TryIds(toolsRaw, out tools, out error))
        {
            return false;
        }

        if (knowledge.Length > 0 || tools.Length > 0)
        {
            error = AiSkillErrors.SkillGrantUnknown;
            return false;
        }

        return true;
    }

    public static string? InstructionError(string instructions)
    {
        if (string.IsNullOrEmpty(instructions))
        {
            return null;
        }

        if (instructions.Contains("://", StringComparison.OrdinalIgnoreCase)
            || instructions.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || instructions.Contains("data:", StringComparison.OrdinalIgnoreCase))
        {
            return AiSkillErrors.SkillLinkRejected;
        }

        if (instructions.Contains("```", StringComparison.Ordinal)
            || instructions.Contains("<script", StringComparison.OrdinalIgnoreCase)
            || instructions.Contains("eval(", StringComparison.OrdinalIgnoreCase)
            || instructions.Contains("Function(", StringComparison.Ordinal)
            || instructions.Contains("require(", StringComparison.Ordinal)
            || instructions.Contains("import ", StringComparison.Ordinal))
        {
            return AiSkillErrors.SkillCodeRejected;
        }

        foreach (var phrase in ForbiddenInstructions)
        {
            if (instructions.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return AiSkillErrors.SkillInstructionRejected;
            }
        }

        return null;
    }

    private static bool TrySchema(JsonElement element, out string canonical, out string? error)
    {
        canonical = """{"type":"object"}""";
        error = null;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = AiSkillErrors.InvalidSkillSchema;
            return false;
        }

        var raw = element.GetRawText();
        if (raw.Length > MaxSchemaLength)
        {
            error = AiSkillErrors.InvalidSkillSchema;
            return false;
        }

        error = RejectSecretOrCode(element);
        if (error is not null)
        {
            return false;
        }

        if (element.TryGetProperty("type", out var type)
            && (type.ValueKind != JsonValueKind.String || type.GetString() != "object"))
        {
            error = AiSkillErrors.InvalidSkillSchema;
            return false;
        }

        canonical = JsonNode.Parse(raw)!.ToJsonString(CanonicalJson);
        return true;
    }

    private static bool TryExamples(JsonElement element, out string canonical, out string? error)
    {
        canonical = "[]";
        error = null;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaxExamples)
        {
            error = AiSkillErrors.InvalidSkillExamples;
            return false;
        }

        var raw = element.GetRawText();
        if (raw.Length > MaxExamplesLength)
        {
            error = AiSkillErrors.InvalidSkillExamples;
            return false;
        }

        error = RejectSecretOrCode(element);
        if (error is not null)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = AiSkillErrors.InvalidSkillExamples;
                return false;
            }

            foreach (var property in item.EnumerateObject())
            {
                if (property.Name is not ("name" or "input" or "expected"))
                {
                    error = AiSkillErrors.InvalidSkillExamples;
                    return false;
                }

                if (property.Value.ValueKind == JsonValueKind.String
                    && InstructionError(property.Value.GetString() ?? string.Empty) is not null)
                {
                    error = AiSkillErrors.InvalidSkillExamples;
                    return false;
                }
            }
        }

        canonical = JsonNode.Parse(raw)!.ToJsonString(CanonicalJson);
        return true;
    }

    private static bool TryIds(Guid[]? raw, out Guid[] ids, out string? error)
    {
        ids = (raw ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length > MaxReferences)
        {
            error = AiSkillErrors.SkillGrantUnknown;
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGuidArray(JsonElement element, out Guid[] ids)
    {
        ids = [];
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<Guid>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var id))
            {
                return false;
            }

            parsed.Add(id);
        }

        ids = parsed.ToArray();
        return true;
    }

    private static string? Text(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void Add(List<AiSkillFieldChange> changes, string field, string? before, string? after)
    {
        if (!string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal))
        {
            changes.Add(new AiSkillFieldChange(field, before, after));
        }
    }

    private static string Join(Guid[] ids) =>
        string.Join(",", ids.OrderBy(id => id).Select(id => id.ToString("D")));

    private static bool IsForbiddenName(string name) => IsSecretName(name) || IsCodeName(name);

    private static bool IsSecretName(string name) =>
        SecretNames.Any(secret => Normalize(secret) == Normalize(name));

    private static bool IsCodeName(string name) =>
        CodeNames.Any(code => Normalize(code) == Normalize(name));

    private static string Normalize(string name) =>
        name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLower(CultureInfo.InvariantCulture);
}

public static class AiSkillErrors
{
    public const string Disabled = "AiBotsDisabled";
    public const string InvalidSkillSlug = "InvalidSkillSlug";
    public const string InvalidSkillName = "InvalidSkillName";
    public const string InvalidSkillDescription = "InvalidSkillDescription";
    public const string InvalidSkillInstructions = "InvalidSkillInstructions";
    public const string InvalidSkillSchema = "InvalidSkillSchema";
    public const string InvalidSkillExamples = "InvalidSkillExamples";
    public const string InvalidSkillCompatibility = "InvalidSkillCompatibility";
    public const string InvalidSkillClassification = "InvalidSkillClassification";
    public const string SkillCodeRejected = "SkillCodeRejected";
    public const string SkillSecretRejected = "SkillSecretRejected";
    public const string SkillInstructionRejected = "SkillInstructionRejected";
    public const string SkillLinkRejected = "SkillLinkRejected";
    public const string SkillGrantUnknown = "SkillGrantUnknown";
    public const string SkillManifestRejected = "SkillManifestRejected";
    public const string SkillQuotaExceeded = "SkillQuotaExceeded";
    public const string SkillNotFound = "SkillNotFound";
    public const string SkillVersionNotFound = "SkillVersionNotFound";
    public const string SkillRevisionConflict = "SkillRevisionConflict";
    public const string SkillSlugConflict = "SkillSlugConflict";
    public const string SkillSlugVersionConflict = "SkillSlugVersionConflict";
    public const string SkillNotPublished = "SkillNotPublished";
    public const string SkillReferenceRejected = "SkillReferenceRejected";
    public const string IdempotencyRequired = "IdempotencyRequired";
    public const string IdempotencyConflict = "IdempotencyConflict";
}
