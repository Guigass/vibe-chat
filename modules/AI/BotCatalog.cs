using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VibeChat.SharedKernel;

namespace VibeChat.AI;

public enum AiBotStatus
{
    Draft,
    Published,
    Disabled
}

public sealed class AiBotOptions
{
    public const string SectionName = "Features:AiBots";

    public bool Enabled { get; set; }

    public int MaxBotsPerWorkspace { get; set; } = 20;

    public decimal MaxDailyBudgetUsdPerWorkspace { get; set; } = 50m;
}

/// <summary>Workspace bot definition. The published snapshot lives in <see cref="AiBotVersion"/>.</summary>
public sealed class AiBotDefinition
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid IntegrationBotId { get; set; }
    public UserId PrincipalUserId { get; set; }
    public UserId OwnerUserId { get; set; }
    public AiBotStatus Status { get; set; }
    public int? ActiveVersionNumber { get; set; }
    public int Revision { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AvatarKey { get; set; } = string.Empty;
    public bool AllowDms { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public Guid[] SkillVersionIds { get; set; } = [];
    public Guid[] KnowledgeGrantIds { get; set; } = [];
    public Guid[] McpToolGrantIds { get; set; } = [];
    public string? GuardrailPolicyVersion { get; set; }
    public int MaxTurns { get; set; }
    public int MaxInputTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    public decimal DailyBudgetUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AiBotVersion
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid BotId { get; set; }
    public int VersionNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AvatarKey { get; set; } = string.Empty;
    public bool AllowDms { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string PromptSha256 { get; set; } = string.Empty;
    public Guid[] SkillVersionIds { get; set; } = [];
    public Guid[] KnowledgeGrantIds { get; set; } = [];
    public Guid[] McpToolGrantIds { get; set; } = [];
    public string? GuardrailPolicyVersion { get; set; }
    public int MaxTurns { get; set; }
    public int MaxInputTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    public decimal DailyBudgetUsd { get; set; }
    public UserId PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class AiBotChannelScope
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid BotId { get; set; }
    public int VersionNumber { get; set; }
    public ChannelId ChannelId { get; set; }
}

public sealed class AiModelBinding
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid BotId { get; set; }
    public int VersionNumber { get; set; }
    public string Provider { get; set; } = "none";
    public string ModelId { get; set; } = "none";
    public string ParametersJson { get; set; } = "{}";
    public int MaxTokens { get; set; }
    public int TimeoutMs { get; set; }
    public string? FallbackModelId { get; set; }
    public string DataClass { get; set; } = "internal";
}

public sealed record AiBotDraft(
    string Name,
    string Description,
    string AvatarKey,
    bool AllowDms,
    string SystemPrompt,
    string Provider,
    string ModelId,
    string ParametersJson,
    int MaxTokens,
    int TimeoutMs,
    string? FallbackModelId,
    string DataClass,
    Guid[] ChannelIds,
    Guid[] SkillVersionIds,
    Guid[] KnowledgeGrantIds,
    Guid[] McpToolGrantIds,
    string? GuardrailPolicyVersion,
    int MaxTurns,
    int MaxInputTokens,
    int MaxOutputTokens,
    decimal DailyBudgetUsd);

public sealed record AiBotFieldChange(string Field, string? Before, string? After);

public static class AiBotCatalogPolicies
{
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 280;
    public const int MaxPromptLength = 8000;
    public const int MaxAvatarKeyLength = 200;
    public const int MaxModelIdLength = 120;
    public const int MaxPolicyLength = 64;
    public const int MaxReferences = 32;
    public const int MaxChannels = 32;
    public const int DraftVersion = 0;

    public static readonly string[] Providers = ["none", "mock", "openrouter", "local"];
    public static readonly string[] DataClasses = ["public", "internal", "confidential", "restricted"];
    public static readonly string[] ParameterNames =
        ["temperature", "topP", "maxTokens", "presencePenalty", "frequencyPenalty", "seed", "stop"];

    private static readonly string[] SecretNames =
        ["apikey", "api_key", "secret", "token", "authorization", "password", "bearer", "credential"];

    public static string? RejectSecretNames(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (IsSecretName(property.Name))
                {
                    return AiBotErrors.ProviderKeyRejected;
                }

                var nested = RejectSecretNames(property.Value);
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
                var nested = RejectSecretNames(item);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    public static bool TryNormalize(AiBotDraftInput input, bool publishing, out AiBotDraft draft, out string? error)
    {
        draft = null!;
        error = null;
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > MaxNameLength)
        {
            error = AiBotErrors.InvalidBotName;
            return false;
        }

        var description = (input.Description ?? string.Empty).Trim();
        if (description.Length > MaxDescriptionLength)
        {
            error = AiBotErrors.InvalidBotDescription;
            return false;
        }

        var avatar = (input.AvatarKey ?? string.Empty).Trim();
        if (avatar.Length > MaxAvatarKeyLength || avatar.Contains("://", StringComparison.Ordinal))
        {
            error = AiBotErrors.InvalidAvatarKey;
            return false;
        }

        var prompt = (input.SystemPrompt ?? string.Empty).Trim();
        if (prompt.Length > MaxPromptLength || (publishing && prompt.Length == 0))
        {
            error = AiBotErrors.InvalidSystemPrompt;
            return false;
        }

        var provider = string.IsNullOrWhiteSpace(input.Provider) ? "none" : input.Provider.Trim().ToLowerInvariant();
        if (!Providers.Contains(provider, StringComparer.Ordinal))
        {
            error = AiBotErrors.InvalidModelBinding;
            return false;
        }

        var modelId = string.IsNullOrWhiteSpace(input.ModelId) ? "none" : input.ModelId.Trim();
        if (modelId.Length is < 1 or > MaxModelIdLength || (publishing && provider != "none" && modelId == "none"))
        {
            error = AiBotErrors.InvalidModelBinding;
            return false;
        }

        if (!TryParameters(input.Parameters, out var parameters, out error))
        {
            return false;
        }

        var dataClass = string.IsNullOrWhiteSpace(input.DataClass) ? "internal" : input.DataClass.Trim().ToLowerInvariant();
        if (!DataClasses.Contains(dataClass, StringComparer.Ordinal))
        {
            error = AiBotErrors.InvalidDataClass;
            return false;
        }

        var maxTokens = input.MaxTokens == 0 ? 1024 : input.MaxTokens;
        var timeoutMs = input.TimeoutMs == 0 ? 30_000 : input.TimeoutMs;
        var maxTurns = input.MaxTurns == 0 ? 4 : input.MaxTurns;
        var maxInput = input.MaxInputTokens == 0 ? 4096 : input.MaxInputTokens;
        var maxOutput = input.MaxOutputTokens == 0 ? 1024 : input.MaxOutputTokens;
        if (maxTokens is < 1 or > 128_000 || timeoutMs is < 1 or > 120_000
            || maxTurns is < 1 or > 32 || maxInput is < 1 or > 128_000 || maxOutput is < 1 or > 128_000
            || input.DailyBudgetUsd < 0 || input.DailyBudgetUsd > 10_000)
        {
            error = AiBotErrors.InvalidBotLimits;
            return false;
        }

        var fallback = string.IsNullOrWhiteSpace(input.FallbackModelId) ? null : input.FallbackModelId.Trim();
        if (fallback is { Length: > MaxModelIdLength })
        {
            error = AiBotErrors.InvalidModelBinding;
            return false;
        }

        var policy = string.IsNullOrWhiteSpace(input.GuardrailPolicyVersion) ? null : input.GuardrailPolicyVersion.Trim();
        if (policy is { Length: > MaxPolicyLength })
        {
            error = AiBotErrors.InvalidPolicyReference;
            return false;
        }

        if (!TryIds(input.ChannelIds, MaxChannels, out var channels, out error)
            || !TryIds(input.SkillVersionIds, MaxReferences, out var skills, out error)
            || !TryIds(input.KnowledgeGrantIds, MaxReferences, out var knowledge, out error)
            || !TryIds(input.McpToolGrantIds, MaxReferences, out var tools, out error))
        {
            return false;
        }

        if (publishing && !input.AllowDms && channels.Length == 0)
        {
            error = AiBotErrors.BotInvocationScopeRequired;
            return false;
        }

        draft = new AiBotDraft(
            name, description, avatar, input.AllowDms, prompt, provider, modelId, parameters,
            maxTokens, timeoutMs, fallback, dataClass, channels, skills, knowledge, tools, policy,
            maxTurns, maxInput, maxOutput, input.DailyBudgetUsd);
        return true;
    }

    public static string? InvocationBlock(AiBotStatus status, int? activeVersion) =>
        status == AiBotStatus.Published && activeVersion is > 0 ? null : AiBotErrors.BotNotInvocable;

    public static int NextVersion(IEnumerable<int> published) =>
        published.DefaultIfEmpty(0).Max() + 1;

    public static bool KeepsHistory(IReadOnlyCollection<int> before, IReadOnlyCollection<int> after) =>
        before.All(after.Contains) && after.Count >= before.Count;

    public static string? QuotaError(int botCount, decimal otherActiveBudget, decimal nextBudget, bool creating, AiBotOptions options)
    {
        if (creating && botCount >= options.MaxBotsPerWorkspace)
        {
            return AiBotErrors.BotQuotaExceeded;
        }

        if (otherActiveBudget + nextBudget > options.MaxDailyBudgetUsdPerWorkspace)
        {
            return AiBotErrors.BotBudgetExceeded;
        }

        return null;
    }

    public static IReadOnlyList<AiBotFieldChange> Diff(AiBotSnapshot left, AiBotSnapshot right)
    {
        var changes = new List<AiBotFieldChange>();
        Add(changes, "name", left.Name, right.Name);
        Add(changes, "description", left.Description, right.Description);
        Add(changes, "avatarKey", left.AvatarKey, right.AvatarKey);
        Add(changes, "allowDms", left.AllowDms ? "true" : "false", right.AllowDms ? "true" : "false");
        Add(changes, "systemPrompt", left.SystemPrompt, right.SystemPrompt);
        Add(changes, "provider", left.Provider, right.Provider);
        Add(changes, "modelId", left.ModelId, right.ModelId);
        Add(changes, "parameters", left.ParametersJson, right.ParametersJson);
        Add(changes, "dataClass", left.DataClass, right.DataClass);
        Add(changes, "fallbackModelId", left.FallbackModelId, right.FallbackModelId);
        Add(changes, "channelIds", Join(left.ChannelIds), Join(right.ChannelIds));
        Add(changes, "skillVersionIds", Join(left.SkillVersionIds), Join(right.SkillVersionIds));
        Add(changes, "knowledgeGrantIds", Join(left.KnowledgeGrantIds), Join(right.KnowledgeGrantIds));
        Add(changes, "mcpToolGrantIds", Join(left.McpToolGrantIds), Join(right.McpToolGrantIds));
        Add(changes, "guardrailPolicyVersion", left.GuardrailPolicyVersion, right.GuardrailPolicyVersion);
        Add(changes, "maxTurns", left.MaxTurns.ToString(), right.MaxTurns.ToString());
        Add(changes, "maxInputTokens", left.MaxInputTokens.ToString(), right.MaxInputTokens.ToString());
        Add(changes, "maxOutputTokens", left.MaxOutputTokens.ToString(), right.MaxOutputTokens.ToString());
        Add(changes, "dailyBudgetUsd", left.DailyBudgetUsd.ToString("0.####"), right.DailyBudgetUsd.ToString("0.####"));
        Add(changes, "timeoutMs", left.TimeoutMs.ToString(), right.TimeoutMs.ToString());
        return changes;
    }

    public static string PromptHash(string prompt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prompt));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string EventDocument(
        string eventType,
        Guid botId,
        Guid workspaceId,
        Guid tenantId,
        int version,
        Guid actorId,
        string promptSha256,
        string provider,
        string modelId)
    {
        var document = new JsonObject
        {
            ["botId"] = botId.ToString("D"),
            ["workspaceId"] = workspaceId.ToString("D"),
            ["tenantId"] = tenantId.ToString("D"),
            ["version"] = version,
            ["actorId"] = actorId.ToString("D"),
            ["promptSha256"] = promptSha256,
            ["provider"] = provider,
            ["modelId"] = modelId
        };
        return document.ToJsonString();
    }

    private static void Add(List<AiBotFieldChange> changes, string field, string? before, string? after)
    {
        if (!string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal))
        {
            changes.Add(new AiBotFieldChange(field, before, after));
        }
    }

    private static string Join(Guid[] ids) =>
        string.Join(",", ids.OrderBy(id => id).Select(id => id.ToString("D")));

    private static bool TryIds(Guid[]? raw, int max, out Guid[] ids, out string? error)
    {
        ids = (raw ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length > max)
        {
            error = AiBotErrors.InvalidReference;
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParameters(JsonElement element, out string canonical, out string? error)
    {
        canonical = "{}";
        error = null;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = AiBotErrors.InvalidModelParameter;
            return false;
        }

        var map = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (IsSecretName(property.Name))
            {
                error = AiBotErrors.ProviderKeyRejected;
                return false;
            }

            if (!ParameterNames.Contains(property.Name, StringComparer.Ordinal))
            {
                error = AiBotErrors.InvalidModelParameter;
                return false;
            }

            if (!TryParameterValue(property.Name, property.Value, out var node, out error))
            {
                return false;
            }

            map[property.Name] = node;
        }

        canonical = new JsonObject(map.Select(pair => KeyValuePair.Create(pair.Key, pair.Value))).ToJsonString();
        return true;
    }

    private static bool TryParameterValue(string name, JsonElement value, out JsonNode node, out string? error)
    {
        node = null!;
        error = null;
        if (name == "stop")
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 32)
            {
                node = JsonValue.Create(value.GetString())!;
                return true;
            }

            error = AiBotErrors.InvalidModelParameter;
            return false;
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            error = AiBotErrors.InvalidModelParameter;
            return false;
        }

        node = JsonNode.Parse(value.GetRawText())!;
        return true;
    }

    private static bool IsSecretName(string name)
    {
        var normalized = name.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return SecretNames.Contains(normalized, StringComparer.Ordinal);
    }
}

public sealed class AiBotDraftInput
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? AvatarKey { get; set; }
    public bool AllowDms { get; set; }
    public string? SystemPrompt { get; set; }
    public string? Provider { get; set; }
    public string? ModelId { get; set; }
    public JsonElement Parameters { get; set; }
    public int MaxTokens { get; set; }
    public int TimeoutMs { get; set; }
    public string? FallbackModelId { get; set; }
    public string? DataClass { get; set; }
    public Guid[]? ChannelIds { get; set; }
    public Guid[]? SkillVersionIds { get; set; }
    public Guid[]? KnowledgeGrantIds { get; set; }
    public Guid[]? McpToolGrantIds { get; set; }
    public string? GuardrailPolicyVersion { get; set; }
    public int MaxTurns { get; set; }
    public int MaxInputTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    public decimal DailyBudgetUsd { get; set; }
}

public sealed record AiBotSnapshot(
    string Name,
    string Description,
    string AvatarKey,
    bool AllowDms,
    string SystemPrompt,
    string Provider,
    string ModelId,
    string ParametersJson,
    string? FallbackModelId,
    string DataClass,
    Guid[] ChannelIds,
    Guid[] SkillVersionIds,
    Guid[] KnowledgeGrantIds,
    Guid[] McpToolGrantIds,
    string? GuardrailPolicyVersion,
    int MaxTurns,
    int MaxInputTokens,
    int MaxOutputTokens,
    decimal DailyBudgetUsd,
    int TimeoutMs);

public static class AiBotErrors
{
    public const string Disabled = "AiBotsDisabled";
    public const string InvalidBotName = "InvalidBotName";
    public const string InvalidBotDescription = "InvalidBotDescription";
    public const string InvalidAvatarKey = "InvalidAvatarKey";
    public const string InvalidSystemPrompt = "InvalidSystemPrompt";
    public const string InvalidModelBinding = "InvalidModelBinding";
    public const string InvalidModelParameter = "InvalidModelParameter";
    public const string InvalidDataClass = "InvalidDataClass";
    public const string InvalidBotLimits = "InvalidBotLimits";
    public const string InvalidPolicyReference = "InvalidPolicyReference";
    public const string InvalidReference = "InvalidReference";
    public const string InvalidChannelScope = "InvalidChannelScope";
    public const string BotInvocationScopeRequired = "BotInvocationScopeRequired";
    public const string ProviderKeyRejected = "ProviderKeyRejected";
    public const string BotQuotaExceeded = "BotQuotaExceeded";
    public const string BotBudgetExceeded = "BotBudgetExceeded";
    public const string BotNotFound = "BotNotFound";
    public const string BotVersionNotFound = "BotVersionNotFound";
    public const string BotRevisionConflict = "BotRevisionConflict";
    public const string BotNotInvocable = "BotNotInvocable";
    public const string BotNotPublished = "BotNotPublished";
    public const string IdempotencyRequired = "IdempotencyRequired";
    public const string IdempotencyConflict = "IdempotencyConflict";
}
