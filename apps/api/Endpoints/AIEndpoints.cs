using System.Text.Json;
using VibeChat.AI;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class AIEndpoints
{
    internal static void MapAI(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/summarize", async (Guid workspaceId, Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, ISummarizeChannelFeature summarize, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null || channel.WorkspaceId != workspace.Id)
            {
                return Results.Forbid();
            }

            // Never on SendMessage hot path — opt-in summarize only (D-06 / ADR-012).
            var result = await summarize.SummarizeAsync(workspace.TenantId, workspace.Id, channel.Id, ct);
            if (!result.Ok)
            {
                var status = string.Equals(result.Error, "AiDisabled", StringComparison.Ordinal)
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status502BadGateway;
                return Results.Json(new AiSummaryErrorResponse(result.Error ?? "AiError", result.Summary), statusCode: status);
            }

            return Results.Ok(new AiSummaryResponse(result.Summary));
        }).RequirePermission(Permissions.Ai.Summarize);

        v1.MapPost("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/suggest-reply", async (Guid workspaceId, Guid channelId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, ISuggestChannelReplyFeature suggestReply, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null || channel.WorkspaceId != workspace.Id)
            {
                return Results.Forbid();
            }

            // Never on SendMessage hot path — opt-in suggest-reply only (D-06 / ADR-012 / B-045).
            var result = await suggestReply.SuggestAsync(workspace.TenantId, workspace.Id, channel.Id, ct);
            if (!result.Ok)
            {
                var status = string.Equals(result.Error, "AiDisabled", StringComparison.Ordinal)
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status502BadGateway;
                return Results.Json(new AiSummaryErrorResponse(result.Error ?? "AiError", result.Suggestion), statusCode: status);
            }

            return Results.Ok(new AiSuggestReplyResponse(result.Suggestion));
        }).RequirePermission(Permissions.Ai.SuggestReply);

        v1.MapPost("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/messages/{messageId:guid}/attachments/{attachmentId:guid}/transcribe", async (
            Guid workspaceId,
            Guid channelId,
            Guid messageId,
            Guid attachmentId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            ITranscribeAttachmentFeature transcribe,
            IAuditWriter audit,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null || channel.WorkspaceId != workspace.Id)
            {
                return Results.Forbid();
            }

            var result = await transcribe.TranscribeAsync(
                workspace.TenantId,
                workspace.Id,
                channel.Id,
                new MessageId(messageId),
                attachmentId,
                ct);

            if (!result.Ok)
            {
                var status = result.Error switch
                {
                    "AiDisabled" => StatusCodes.Status503ServiceUnavailable,
                    "NotFound" => StatusCodes.Status404NotFound,
                    "NotAudio" => StatusCodes.Status400BadRequest,
                    "ProviderError" => StatusCodes.Status502BadGateway,
                    _ => StatusCodes.Status502BadGateway
                };
                return Results.Json(new AiTranscribeErrorResponse(result.Error ?? "AiError"), statusCode: status);
            }

            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.AiTranscribe,
                EntityType = "Attachment",
                EntityId = attachmentId.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { channelId, messageId, provider = result.Provider })
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new AiTranscribeResponse(result.Text, result.Language ?? "und", result.Provider ?? "Unknown"));
        }).RequirePermission(Permissions.Ai.Transcribe);
    }
}
