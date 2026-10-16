using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// Declarative workspace automation (B-125). The published definition is immutable
/// per version. Secret values are never part of the model.
/// </summary>
public sealed class AutomationDefinition
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AutomationStatus Status { get; set; }
    public bool Enabled { get; set; }
    public int PublishedVersion { get; set; }
    public string DraftJson { get; set; } = "{}";
    public string TriggerType { get; set; } = string.Empty;
    public UserId CreatedBy { get; set; }
    public UserId? IdentityUserId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? WebhookTokenHash { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? CircuitOpenUntil { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AutomationVersion
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid AutomationId { get; set; }
    public int Version { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public string CapabilitiesJson { get; set; } = "[]";
    public UserId PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class AutomationRun
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid AutomationId { get; set; }
    public int Version { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string EventJson { get; set; } = "{}";
    public int Depth { get; set; }
    public AutomationRunMode Mode { get; set; }
    public AutomationRunStatus Status { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public Guid? CausationRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class AutomationStep
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid RunId { get; set; }
    public int ActionIndex { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public AutomationStepStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string LogJson { get; set; } = "{}";
    public Guid? EffectId { get; set; }
    public int Attempts { get; set; }
}

public enum AutomationStatus
{
    Draft = 0,
    Published = 1,
}

public enum AutomationRunMode
{
    Live = 0,
    DryRun = 1,
    Replay = 2,
}

public enum AutomationRunStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Stopped = 3,
    Skipped = 4,
}

public enum AutomationStepStatus
{
    Planned = 0,
    Succeeded = 1,
    Failed = 2,
    Skipped = 3,
}

public sealed class AutomationOptions
{
    public const string SectionName = "Automation";

    public bool Enabled { get; set; }
}

public sealed record AutomationTriggerModel(
    string Type,
    Guid[] ChannelIds,
    Guid[] FormIds,
    string Event,
    int IntervalMinutes);

public sealed record AutomationConditionModel(string Field, string Op, string Value);

public sealed record AutomationActionModel(
    string Type,
    string Capability,
    Guid? ChannelId,
    string Body,
    string Title,
    Guid? TaskId,
    string State,
    Guid[] UserIds,
    Guid? ConnectorId);

public sealed record AutomationDefinitionModel(
    string ContractVersion,
    AutomationTriggerModel Trigger,
    AutomationConditionModel[] Conditions,
    AutomationActionModel[] Actions,
    Guid[] ChannelIds,
    string[] SecretRefs)
{
    public string ToJson()
    {
        var channels = new JsonArray();
        foreach (var channel in ChannelIds)
        {
            channels.Add(channel.ToString("D"));
        }

        var secrets = new JsonArray();
        foreach (var secret in SecretRefs)
        {
            secrets.Add(secret);
        }

        return new JsonObject
        {
            ["contractVersion"] = ContractVersion,
            ["trigger"] = TriggerJson(Trigger),
            ["conditions"] = ConditionsJson(Conditions),
            ["actions"] = ActionsJson(Actions),
            ["channelIds"] = channels,
            ["secretRefs"] = secrets,
        }.ToJsonString();
    }

    public string[] Capabilities() => Actions.Select(action => action.Capability).Distinct(StringComparer.Ordinal).ToArray();

    private static JsonObject TriggerJson(AutomationTriggerModel trigger)
    {
        var channels = new JsonArray();
        foreach (var channel in trigger.ChannelIds)
        {
            channels.Add(channel.ToString("D"));
        }

        var forms = new JsonArray();
        foreach (var form in trigger.FormIds)
        {
            forms.Add(form.ToString("D"));
        }

        return new JsonObject
        {
            ["type"] = trigger.Type,
            ["channelIds"] = channels,
            ["formIds"] = forms,
            ["event"] = trigger.Event,
            ["intervalMinutes"] = trigger.IntervalMinutes,
        };
    }

    private static JsonArray ConditionsJson(IEnumerable<AutomationConditionModel> conditions)
    {
        var array = new JsonArray();
        foreach (var condition in conditions)
        {
            array.Add(new JsonObject
            {
                ["field"] = condition.Field,
                ["op"] = condition.Op,
                ["value"] = condition.Value,
            });
        }

        return array;
    }

    private static JsonArray ActionsJson(IEnumerable<AutomationActionModel> actions)
    {
        var array = new JsonArray();
        foreach (var action in actions)
        {
            array.Add(new JsonObject
            {
                ["type"] = action.Type,
                ["capability"] = action.Capability,
                ["channelId"] = action.ChannelId?.ToString("D"),
                ["body"] = action.Body,
                ["title"] = action.Title,
                ["taskId"] = action.TaskId?.ToString("D"),
                ["state"] = action.State,
                ["userIds"] = GuidArray(action.UserIds),
                ["connectorId"] = action.ConnectorId?.ToString("D"),
            });
        }

        return array;
    }

    private static JsonArray GuidArray(IEnumerable<Guid> ids)
    {
        var array = new JsonArray();
        foreach (var id in ids)
        {
            array.Add(id.ToString("D"));
        }

        return array;
    }
}

public sealed record AutomationSignal(
    string Type,
    string EventName,
    string EventId,
    Guid? ChannelId,
    Guid? SourceId,
    Guid? FormId,
    Guid? TaskId,
    string? State,
    int Depth,
    Guid? CausationRunId)
{
    public string ToJson() => AutomationPolicies.Redact(JsonSerializer.Serialize(new
    {
        type = Type,
        eventName = EventName,
        eventId = EventId,
        channelId = ChannelId,
        sourceId = SourceId,
        formId = FormId,
        taskId = TaskId,
        state = State,
        depth = Depth,
    }));
}

public static partial class AutomationPolicies
{
    public const string ContractVersion = "automation.definition.v1";
    public const string TokenPrefix = "vc_auto_";
    public const string IdentitySubjectPrefix = "automation:";
    public const string JobRole = "automation";
    public const string TaskMethod = "automation";
    public const int MaxDepth = 3;
    public const int MaxConsecutiveFailures = 5;
    public const int CircuitCooldownMinutes = 15;
    public const int MaxRunsPerHour = 30;
    public const int ActionTimeoutSeconds = 2;
    public const int MaxActions = 8;
    public const int MaxConditions = 12;
    public const int MaxChannels = 20;
    public const int MaxUsers = 12;
    public const int MaxNameLength = 80;
    public const int MaxBodyLength = 500;
    public const int MaxTitleLength = 120;
    public const int MaxIdempotencyKeyLength = 80;
    public const int MaxSecretNameLength = 40;
    public const int MaxSecretRefs = 8;
    public const int MaxAutomationsPerWorkspace = 30;
    public const int MaxPageSize = 50;
    public const int MinIntervalMinutes = 5;
    public const int MaxIntervalMinutes = 1440;
    public const int MaxScan = 100;

    public const string Created = "automation.created";
    public const string Published = "automation.published";
    public const string EnabledEvent = "automation.enabled";
    public const string DisabledEvent = "automation.disabled";
    public const string LoopStopped = "automation.loop_stopped";
    public const string CircuitOpened = "automation.circuit_open";
    public const string RateLimited = "automation.rate_limited";
    public const string Notified = "automation.notified";
    public const string ConnectorEvent = "automation.connector";

    public const string TriggerMessage = "message";
    public const string TriggerForm = "form";
    public const string TriggerTask = "task";
    public const string TriggerSchedule = "schedule";
    public const string TriggerWebhook = "webhook";
    public const string ActionPostMessage = "post_message";
    public const string ActionCreateTask = "create_task";
    public const string ActionUpdateTask = "update_task";
    public const string ActionNotify = "notify";
    public const string ActionConnector = "call_connector";

    private static readonly HashSet<string> TopLevel =
        ["contractVersion", "trigger", "conditions", "actions", "channelIds", "secretRefs"];

    private static readonly HashSet<string> TriggerNames =
        ["type", "channelIds", "formIds", "event", "intervalMinutes"];

    private static readonly HashSet<string> ConditionNames = ["field", "op", "value"];

    private static readonly HashSet<string> ActionNames =
        ["type", "capability", "channelId", "body", "title", "taskId", "state", "userIds", "connectorId"];

    private static readonly HashSet<string> TriggerTypes =
        [TriggerMessage, TriggerForm, TriggerTask, TriggerSchedule, TriggerWebhook];

    private static readonly HashSet<string> ConditionFields =
        ["channelId", "state", "event", "formId", "taskId", "sourceId"];

    private static readonly HashSet<string> ConditionOps = ["eq", "contains", "exists"];

    private static readonly HashSet<string> SecretNames =
        ["secret", "secrets", "token", "password", "apikey", "api_key", "signingsecret", "authorization", "credential", "credentials", "webhooktoken"];

    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    public static bool IsValidName(string name) => name.Length is >= 1 and <= MaxNameLength;

    public static bool IsValidIdempotencyKey(string key) => key.Length is >= 1 and <= MaxIdempotencyKeyLength;

    public static string StatusToken(AutomationStatus status) => status == AutomationStatus.Published ? "published" : "draft";

    public static string ModeToken(AutomationRunMode mode) => mode switch
    {
        AutomationRunMode.DryRun => "dry_run",
        AutomationRunMode.Replay => "replay",
        _ => "live",
    };

    public static string RunToken(AutomationRunStatus status) => status switch
    {
        AutomationRunStatus.Succeeded => "succeeded",
        AutomationRunStatus.Failed => "failed",
        AutomationRunStatus.Stopped => "stopped",
        AutomationRunStatus.Skipped => "skipped",
        _ => "pending",
    };

    public static string StepToken(AutomationStepStatus status) => status switch
    {
        AutomationStepStatus.Succeeded => "succeeded",
        AutomationStepStatus.Failed => "failed",
        AutomationStepStatus.Skipped => "skipped",
        _ => "planned",
    };

    public static bool TryParseMode(string? token, out AutomationRunMode mode)
    {
        switch (Normalize(token).ToLowerInvariant())
        {
            case "dry_run":
            case "dry-run":
                mode = AutomationRunMode.DryRun;
                return true;
            case "replay":
                mode = AutomationRunMode.Replay;
                return true;
            case "":
            case "live":
                mode = AutomationRunMode.Live;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static string RequiredCapability(string actionType) => actionType switch
    {
        ActionPostMessage => Permissions.Message.Send,
        ActionCreateTask or ActionUpdateTask => Permissions.Message.Read,
        ActionNotify => Permissions.Automation.Notify,
        ActionConnector => Permissions.Automation.Connector,
        _ => string.Empty,
    };

    public static bool TryParse(string? json, out AutomationDefinitionModel? model, out string error)
    {
        model = null;
        try
        {
            var node = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return TryParseNode(node, out model, out error);
        }
        catch (JsonException)
        {
            error = "InvalidDefinition";
            return false;
        }
    }

    public static bool TryParseNode(JsonNode? node, out AutomationDefinitionModel? model, out string error)
    {
        model = null;
        if (node is not JsonObject root)
        {
            error = "InvalidDefinition";
            return false;
        }

        if (!Known(root, TopLevel, out error))
        {
            return false;
        }

        if (ContainsSecretValue(root))
        {
            error = "SecretValueNotAllowed";
            return false;
        }

        if (root["contractVersion"]?.GetValue<string>() != ContractVersion)
        {
            error = "InvalidDefinition";
            return false;
        }

        if (root["trigger"] is not JsonObject trigger || !TryTrigger(trigger, out var triggerModel, out error))
        {
            error ??= "InvalidDefinition";
            return false;
        }

        if (!TryConditions(root["conditions"] as JsonArray, out var conditions, out error))
        {
            return false;
        }

        if (!TryActions(root["actions"] as JsonArray, out var actions, out error))
        {
            return false;
        }

        if (!TryGuids(root["channelIds"] as JsonArray, MaxChannels, out var channels, out error))
        {
            return false;
        }

        if (!TrySecretRefs(root["secretRefs"] as JsonArray, out var secrets, out error))
        {
            return false;
        }

        if (channels.Length == 0 && triggerModel.Type is TriggerMessage or TriggerForm or TriggerTask)
        {
            error = "ChannelNotInScope";
            return false;
        }

        if (triggerModel.ChannelIds.Any(id => !channels.Contains(id)))
        {
            error = "ChannelNotInScope";
            return false;
        }

        foreach (var action in actions)
        {
            if (action.ChannelId is Guid channel && !channels.Contains(channel))
            {
                error = "ChannelNotInScope";
                return false;
            }
        }

        model = new AutomationDefinitionModel(ContractVersion, triggerModel, conditions, actions, channels, secrets);
        error = string.Empty;
        return true;
    }

    public static bool Matches(AutomationDefinitionModel model, AutomationSignal signal)
    {
        if (!string.Equals(model.Trigger.Type, signal.Type, StringComparison.Ordinal))
        {
            return false;
        }

        if (model.Trigger.Event.Length > 0
            && !string.Equals(model.Trigger.Event, signal.EventName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (model.Trigger.ChannelIds.Length > 0
            && (signal.ChannelId is null || !model.Trigger.ChannelIds.Contains(signal.ChannelId.Value)))
        {
            return false;
        }

        if (model.Trigger.FormIds.Length > 0
            && (signal.FormId is null || !model.Trigger.FormIds.Contains(signal.FormId.Value)))
        {
            return false;
        }

        if (signal.ChannelId is Guid channel && !model.ChannelIds.Contains(channel))
        {
            return false;
        }

        return true;
    }

    public static bool ConditionsPass(AutomationDefinitionModel model, AutomationSignal signal)
    {
        foreach (var condition in model.Conditions)
        {
            var actual = FieldValue(signal, condition.Field);
            var expected = condition.Value;
            var ok = condition.Op switch
            {
                "exists" => !string.IsNullOrEmpty(actual),
                "eq" => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                "contains" => actual?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true,
                _ => false,
            };
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryAdmit(
        bool flagOn,
        bool enabled,
        DateTimeOffset? circuitOpenUntil,
        int runsInWindow,
        int depth,
        DateTimeOffset now,
        out string block)
    {
        if (!flagOn)
        {
            block = "AutomationDisabled";
            return false;
        }

        if (!enabled)
        {
            block = "AutomationDisabled";
            return false;
        }

        if (circuitOpenUntil is DateTimeOffset until && until > now)
        {
            block = "CircuitOpen";
            return false;
        }

        if (runsInWindow >= MaxRunsPerHour)
        {
            block = "RateLimited";
            return false;
        }

        if (depth >= MaxDepth)
        {
            block = "LoopStopped";
            return false;
        }

        block = string.Empty;
        return true;
    }

    public static string[] Intersect(IReadOnlySet<string> ownerPermissions, IEnumerable<string> requested) =>
        requested.Where(ownerPermissions.Contains).Distinct(StringComparer.Ordinal).ToArray();

    public static bool IsTransient(string? error) =>
        error is "Timeout" or "ConnectorUnavailable";

    public static string StepKey(Guid automationId, int version, string eventId, int actionIndex) =>
        Hash($"{automationId:N}:{version}:{eventId}:{actionIndex}");

    public static string RunKey(Guid automationId, int version, string eventId, AutomationRunMode mode) =>
        Hash($"{automationId:N}:{version}:{eventId}:{ModeToken(mode)}");

    public static string HashToken(string raw) => Hash(raw.Trim());

    public static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return TokenPrefix + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = SecretInlinePattern().Replace(value, "$1=[redacted]");
        text = TokenPattern().Replace(text, "[redacted]");
        text = BearerPattern().Replace(text, "Bearer [redacted]");
        return text;
    }

    public static string StepLog(string action, string status, string? error, Guid? effectId) =>
        Redact(JsonSerializer.Serialize(new
        {
            action,
            status,
            error,
            effectId,
        }));

    public static bool TryReadSignal(string outboxType, string payload, out AutomationSignal? signal)
    {
        signal = null;
        JsonObject root;
        try
        {
            if (JsonNode.Parse(payload) is not JsonObject parsed)
            {
                return false;
            }

            root = parsed;
        }
        catch (JsonException)
        {
            return false;
        }

        var mapped = MapOutbox(outboxType);
        if (mapped is null)
        {
            return false;
        }

        var (type, eventName) = mapped.Value;
        var eventId = FirstGuid(root, "messageId", "submissionId", "taskId")?.ToString("D")
            ?? FirstString(root, "eventId");
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return false;
        }

        var hintedDepth = 0;
        if (root["automationDepth"] is JsonValue depthValue && depthValue.TryGetValue<int>(out var parsedDepth))
        {
            hintedDepth = parsedDepth;
        }

        signal = new AutomationSignal(
            type,
            eventName,
            eventId,
            ReadGuid(root, "channelId"),
            ReadGuid(root, "messageId") ?? ReadGuid(root, "submissionId") ?? ReadGuid(root, "taskId"),
            ReadGuid(root, "formId"),
            ReadGuid(root, "taskId"),
            eventName,
            hintedDepth,
            ReadGuid(root, "automationRunId"));
        return true;
    }

    private static (string Type, string EventName)? MapOutbox(string type) => type switch
    {
        "MessageCreatedEvent" => (TriggerMessage, "created"),
        "form.approved" => (TriggerForm, "approved"),
        "form.submitted" => (TriggerForm, "submitted"),
        "form.rejected" => (TriggerForm, "rejected"),
        "task.created" => (TriggerTask, "created"),
        "task.updated" => (TriggerTask, "updated"),
        "task.completed" => (TriggerTask, "completed"),
        _ => null,
    };

    private static bool TryTrigger(JsonObject trigger, out AutomationTriggerModel model, out string error)
    {
        model = new(TriggerMessage, [], [], string.Empty, 0);
        if (!Known(trigger, TriggerNames, out error))
        {
            return false;
        }

        var type = Normalize(trigger["type"]?.GetValue<string>()).ToLowerInvariant();
        if (!TriggerTypes.Contains(type))
        {
            error = "InvalidDefinition";
            return false;
        }

        if (!TryGuids(trigger["channelIds"] as JsonArray, MaxChannels, out var channels, out error)
            || !TryGuids(trigger["formIds"] as JsonArray, MaxChannels, out var forms, out error))
        {
            return false;
        }

        var eventName = Normalize(trigger["event"]?.GetValue<string>()).ToLowerInvariant();
        var interval = trigger["intervalMinutes"]?.GetValue<int>() ?? 0;
        if (type == TriggerSchedule && interval is < MinIntervalMinutes or > MaxIntervalMinutes)
        {
            error = "InvalidDefinition";
            return false;
        }

        if (type != TriggerSchedule)
        {
            interval = 0;
        }

        if (eventName.Length > 40)
        {
            error = "InvalidDefinition";
            return false;
        }

        model = new AutomationTriggerModel(type, channels, forms, eventName, interval);
        error = string.Empty;
        return true;
    }

    private static bool TryConditions(JsonArray? array, out AutomationConditionModel[] conditions, out string error)
    {
        conditions = [];
        error = string.Empty;
        array ??= [];
        if (array.Count > MaxConditions)
        {
            error = "InvalidDefinition";
            return false;
        }

        var list = new List<AutomationConditionModel>();
        foreach (var item in array)
        {
            if (item is not JsonObject condition)
            {
                error = "InvalidDefinition";
                return false;
            }

            if (!Known(condition, ConditionNames, out error))
            {
                return false;
            }

            var field = Normalize(condition["field"]?.GetValue<string>());
            var op = Normalize(condition["op"]?.GetValue<string>()).ToLowerInvariant();
            var value = condition["value"]?.GetValue<string>() ?? string.Empty;
            if (!ConditionFields.Contains(field) || !ConditionOps.Contains(op) || value.Length > 80)
            {
                error = "InvalidDefinition";
                return false;
            }

            if (SecretNames.Contains(field.ToLowerInvariant()) || ContainsSecretText(value))
            {
                error = "SecretValueNotAllowed";
                return false;
            }

            list.Add(new AutomationConditionModel(field, op, value));
        }

        conditions = list.ToArray();
        return true;
    }

    private static bool TryActions(JsonArray? array, out AutomationActionModel[] actions, out string error)
    {
        actions = [];
        if (array is null || array.Count is < 1 or > MaxActions)
        {
            error = "InvalidDefinition";
            return false;
        }

        var list = new List<AutomationActionModel>();
        foreach (var item in array)
        {
            if (item is not JsonObject action)
            {
                error = "InvalidDefinition";
                return false;
            }

            if (!Known(action, ActionNames, out error))
            {
                return false;
            }

            var type = Normalize(action["type"]?.GetValue<string>()).ToLowerInvariant();
            var capability = RequiredCapability(type);
            if (capability.Length == 0 || action["capability"]?.GetValue<string>() != capability)
            {
                error = "CapabilityMismatch";
                return false;
            }

            var body = action["body"]?.GetValue<string>() ?? string.Empty;
            var title = Normalize(action["title"]?.GetValue<string>());
            var state = Normalize(action["state"]?.GetValue<string>()).ToLowerInvariant();
            if (body.Length > MaxBodyLength || title.Length > MaxTitleLength || ContainsSecretText(body) || ContainsSecretText(title))
            {
                error = ContainsSecretText(body) || ContainsSecretText(title) ? "SecretValueNotAllowed" : "InvalidDefinition";
                return false;
            }

            if (!TryOptionalGuid(action["channelId"], out var channel, out error)
                || !TryOptionalGuid(action["taskId"], out var taskId, out error)
                || !TryOptionalGuid(action["connectorId"], out var connector, out error)
                || !TryGuids(action["userIds"] as JsonArray, MaxUsers, out var users, out error))
            {
                return false;
            }

            if (type == ActionPostMessage && body.Length == 0)
            {
                error = "InvalidDefinition";
                return false;
            }

            if (type == ActionCreateTask && title.Length == 0)
            {
                error = "InvalidDefinition";
                return false;
            }

            if (type == ActionNotify && users.Length == 0)
            {
                error = "InvalidDefinition";
                return false;
            }

            if (type == ActionConnector && connector is null)
            {
                error = "InvalidDefinition";
                return false;
            }

            if (type == ActionUpdateTask && (taskId is null || state is not ("open" or "in_progress" or "completed")))
            {
                error = "InvalidDefinition";
                return false;
            }

            list.Add(new AutomationActionModel(type, capability, channel, body, title, taskId, state, users, connector));
        }

        actions = list.ToArray();
        error = string.Empty;
        return true;
    }

    private static bool TrySecretRefs(JsonArray? array, out string[] secrets, out string error)
    {
        secrets = [];
        array ??= [];
        if (array.Count > MaxSecretRefs)
        {
            error = "InvalidDefinition";
            return false;
        }

        var list = new List<string>();
        foreach (var item in array)
        {
            var name = Normalize(item?.GetValue<string>()).ToLowerInvariant();
            if (!SecretNamePattern().IsMatch(name))
            {
                error = "SecretValueNotAllowed";
                return false;
            }

            list.Add(name);
        }

        secrets = list.Distinct(StringComparer.Ordinal).ToArray();
        error = string.Empty;
        return true;
    }

    private static bool TryGuids(JsonArray? array, int max, out Guid[] ids, out string error)
    {
        ids = [];
        array ??= [];
        if (array.Count > max)
        {
            error = "InvalidDefinition";
            return false;
        }

        var list = new List<Guid>();
        foreach (var item in array)
        {
            if (!Guid.TryParse(item?.GetValue<string>(), out var id) || id == Guid.Empty)
            {
                error = "InvalidDefinition";
                return false;
            }

            list.Add(id);
        }

        ids = list.Distinct().ToArray();
        error = string.Empty;
        return true;
    }

    private static bool TryOptionalGuid(JsonNode? node, out Guid? id, out string error)
    {
        id = null;
        error = string.Empty;
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return true;
        }

        if (!Guid.TryParse(node.GetValue<string>(), out var parsed) || parsed == Guid.Empty)
        {
            error = "InvalidDefinition";
            return false;
        }

        id = parsed;
        return true;
    }

    private static bool Known(JsonObject obj, HashSet<string> names, out string error)
    {
        foreach (var property in obj)
        {
            if (!names.Contains(property.Key))
            {
                error = SecretNames.Contains(property.Key.ToLowerInvariant())
                    ? "SecretValueNotAllowed"
                    : "InvalidDefinition";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool ContainsSecretValue(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                if (SecretNames.Contains(property.Key.ToLowerInvariant()) && property.Value is JsonValue)
                {
                    return true;
                }

                if (ContainsSecretValue(property.Value))
                {
                    return true;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (ContainsSecretValue(item))
                {
                    return true;
                }
            }
        }
        else if (node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && ContainsSecretText(text))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsSecretText(string? text) =>
        !string.IsNullOrEmpty(text)
        && (text.Contains(TokenPrefix, StringComparison.Ordinal)
            || text.Contains("vc_int_", StringComparison.Ordinal)
            || text.Contains("Bearer ", StringComparison.OrdinalIgnoreCase));

    private static string? FieldValue(AutomationSignal signal, string field) => field switch
    {
        "channelId" => signal.ChannelId?.ToString("D"),
        "state" => signal.State,
        "event" => signal.EventName,
        "formId" => signal.FormId?.ToString("D"),
        "taskId" => signal.TaskId?.ToString("D"),
        "sourceId" => signal.SourceId?.ToString("D"),
        _ => null,
    };

    private static Guid? ReadGuid(JsonObject root, string name)
    {
        var node = root[name];
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<Guid>(out var guid))
        {
            return guid;
        }

        return Guid.TryParse(node.ToString(), out var parsed) ? parsed : null;
    }

    private static Guid? FirstGuid(JsonObject root, params string[] names)
    {
        foreach (var name in names)
        {
            var id = ReadGuid(root, name);
            if (id is not null)
            {
                return id;
            }
        }

        return null;
    }

    private static string? FirstString(JsonObject root, string name) =>
        root[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,39})$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretNamePattern();

    [GeneratedRegex("vc_(?:auto|int)_[a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex("Bearer\\s+\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex("(?i)(password|token|secret|apikey)\\s*[:=]\\s*\\S+")]
    private static partial Regex SecretInlinePattern();
}
