using System.Text.Json;
using System.Text.Json.Nodes;

namespace VibeChat.Api.Contracts;

public sealed record CreateFormRequest(string IdempotencyKey, Guid ChannelId, string Title, JsonElement Schema);

public sealed record UpdateFormRequest(string Title, JsonElement Schema);

public sealed record SubmitFormRequest(string IdempotencyKey, JsonElement Answers);

public sealed record DecideFormRequest(string IdempotencyKey, string Decision, Guid? ActorUserId);

public sealed record FormListItemResponse(
    Guid Id,
    Guid ChannelId,
    string Title,
    string Status,
    int PublishedVersion,
    DateTimeOffset UpdatedAt);

public sealed record FormListResponse(FormListItemResponse[] Items);

public sealed record FormResponse(
    Guid Id,
    Guid ChannelId,
    string Title,
    string Status,
    int PublishedVersion,
    bool CanEdit,
    bool CanSubmit,
    bool CanReview,
    JsonNode? DraftSchema,
    JsonNode? PublishedSchema,
    DateTimeOffset UpdatedAt);

public sealed record FormDecisionResponse(int Version, Guid ActorUserId, string Decision, DateTimeOffset CreatedAt);

public sealed record FormSubmissionResponse(
    Guid Id,
    Guid FormId,
    int SchemaVersion,
    string State,
    bool AnswersPurged,
    JsonNode? Answers,
    Guid SubmittedBy,
    DateTimeOffset SubmittedAt,
    bool CanDecide,
    Guid? ResultMessageId,
    Guid? ResultTaskId,
    FormDecisionResponse[] Decisions);

public sealed record FormSubmissionListResponse(FormSubmissionResponse[] Items);

public sealed record FormExportResponse(Guid FormId, FormSubmissionResponse[] Items);
