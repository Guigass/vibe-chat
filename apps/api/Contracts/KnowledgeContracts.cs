public sealed record CreateKnowledgeCollectionRequest(
    string IdempotencyKey,
    string Title,
    string Scope,
    Guid? ScopeId,
    Guid? ParentId);

public sealed record CreateKnowledgePageRequest(
    string IdempotencyKey,
    Guid CollectionId,
    string Title,
    string Body);

public sealed record UpdateKnowledgePageRequest(
    int Version,
    string Title,
    string Body);

public sealed record RestoreKnowledgePageRequest(
    int ExpectedVersion,
    int SourceVersion);

public sealed record KnowledgeReferenceResponse(
    string Kind,
    Guid TargetId,
    string State,
    string? Label,
    Guid? ChannelId);

public sealed record KnowledgeCollectionListResponse(
    IReadOnlyList<KnowledgeCollectionResponse> Items,
    bool CanEdit,
    bool CanManage);

public sealed record KnowledgePageListResponse(
    IReadOnlyList<KnowledgePageSummaryResponse> Items);

public sealed record KnowledgeSearchResponse(
    IReadOnlyList<KnowledgePageSummaryResponse> Items);

public sealed record KnowledgeHistoryResponse(
    IReadOnlyList<KnowledgeVersionResponse> Items);

public sealed record KnowledgeCollectionResponse(
    Guid Id,
    string Title,
    string Slug,
    string Scope,
    Guid ScopeId,
    Guid? ParentId,
    DateTimeOffset UpdatedAt);

public sealed record KnowledgePageResponse(
    Guid Id,
    Guid CollectionId,
    string Title,
    string Slug,
    string Body,
    int Version,
    Guid CreatedBy,
    Guid UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Deleted,
    IReadOnlyList<KnowledgeReferenceResponse> References);

public sealed record KnowledgePageSummaryResponse(
    Guid Id,
    Guid CollectionId,
    string Title,
    string Slug,
    int Version,
    DateTimeOffset UpdatedAt);

public sealed record KnowledgeVersionResponse(
    int Version,
    string Title,
    string Body,
    string Action,
    Guid ActorUserId,
    int? RestoredFromVersion,
    DateTimeOffset At);

public sealed record KnowledgeExportResponse(
    KnowledgePageResponse Page,
    IReadOnlyList<KnowledgeVersionResponse> Versions);
