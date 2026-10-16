using System.Text.Json;

namespace VibeChat.Api.Contracts;

public sealed record CreateAutomationRequest(string IdempotencyKey, string Name, JsonElement Definition);

public sealed record UpdateAutomationRequest(string Name, JsonElement Definition);

public sealed record AutomationSignalRequest(
    string? EventId,
    string? Type,
    string? EventName,
    Guid? ChannelId,
    Guid? SourceId,
    Guid? FormId,
    Guid? TaskId,
    string? State);

public sealed record ReplayAutomationRequest(string IdempotencyKey);

public sealed record AutomationResponse(
    Guid Id,
    string Name,
    string Status,
    bool Enabled,
    int PublishedVersion,
    string TriggerType,
    Guid? IdentityUserId,
    string? IdentityName,
    string[] Capabilities,
    Guid[] ChannelIds,
    DateTimeOffset? CircuitOpenUntil,
    string? WebhookToken,
    DateTimeOffset UpdatedAt);

public sealed record AutomationListResponse(AutomationResponse[] Items);

public sealed record AutomationStepResponse(
    int Index,
    string Action,
    string Status,
    string? ErrorCode,
    Guid? EffectId);

public sealed record AutomationRunResponse(
    Guid Id,
    int Version,
    string Mode,
    string Status,
    int Depth,
    string? ErrorCode,
    string EventJson,
    AutomationStepResponse[] Steps);

public sealed record AutomationRunListResponse(AutomationRunResponse[] Items);
