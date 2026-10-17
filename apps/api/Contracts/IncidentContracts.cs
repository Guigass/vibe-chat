namespace VibeChat.Api.Contracts;

public sealed record CreateIncidentRequest(
    string IdempotencyKey,
    string TemplateKey,
    string Title,
    string Severity,
    Guid[]? ResponderIds,
    string? CorrelationId,
    Guid? AutomationId);

public sealed record IncidentTransitionRequest(string Status);

public sealed record IncidentEvidenceRequest(string IdempotencyKey, string Kind, Guid SourceId);

public sealed record IncidentUpdateRequest(string IdempotencyKey, string Note);

public sealed record IncidentStepResponse(
    Guid Id,
    int Index,
    string Code,
    string Status,
    string? ErrorCode,
    Guid? AutomationId);

public sealed record IncidentTimelineResponse(
    Guid Id,
    string Kind,
    Guid SourceId,
    DateTimeOffset CreatedAt,
    string? Note);

public sealed record IncidentResponse(
    Guid Id,
    Guid ChannelId,
    string Title,
    string Severity,
    string Status,
    Guid CommanderUserId,
    string? CorrelationId,
    DateTimeOffset DeclaredAt,
    DateTimeOffset? MitigatingAt,
    DateTimeOffset? ResolvedAt,
    bool CanExport,
    IncidentStepResponse[] Steps,
    IncidentTimelineResponse[] Timeline);

public sealed record IncidentCurrentResponse(bool CanManage, IncidentResponse? Incident);
