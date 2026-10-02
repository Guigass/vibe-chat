namespace VibeChat.Api;

public sealed record TemplateSummaryResponse(
    string Id,
    int Version,
    int SpaceCount,
    int ChannelCount,
    string[] Checklist,
    bool Builtin);

public sealed record TemplateCatalogResponse(
    TemplateSummaryResponse[] Builtins,
    TemplateSummaryResponse[] Custom);

public sealed record TemplateValidationResponse(string Id, int Version, int SpaceCount, int ChannelCount);

public sealed record TemplatePlanItemResponse(
    string Kind,
    string Action,
    string Key,
    string? Name,
    string? Detail,
    Guid? ResourceId);

public sealed record TemplatePreviewResponse(
    string TemplateId,
    int Version,
    bool HasConflicts,
    bool DryRun,
    TemplatePlanItemResponse[] Items);

public sealed record TemplateApplyResponse(
    string TemplateId,
    int Version,
    bool HasConflicts,
    bool DryRun,
    bool Idempotent,
    TemplatePlanItemResponse[] Items);

public sealed record OnboardingItemResponse(string Key, string State);

public sealed record OnboardingResponse(
    string Status,
    string? TemplateId,
    int? TemplateVersion,
    OnboardingItemResponse[] Items);
