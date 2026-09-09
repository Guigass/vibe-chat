using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class SearchEndpoints
{
    internal static void MapSearch(this RouteGroupBuilder v1)
    {
        v1.MapGet("/search/messages", async (
            Guid workspaceId,
            string? q,
            Guid? channelId,
            Guid? authorId,
            string? from,
            string? to,
            bool? hasAttachment,
            bool? hasLink,
            string? attachmentKind,
            string? sort,
            string? cursor,
            int? limit,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            ISearchQuery search,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            ChannelId? scopedChannel = null;
            if (channelId is not null)
            {
                var channel = await ResolveChannelAsync(new ChannelId(channelId.Value), profile.Id, db, tenant, ct);
                if (channel is null || channel.WorkspaceId != workspace.Id)
                {
                    return Results.Forbid();
                }

                scopedChannel = channel.Id;
            }

            UserId? scopedAuthor = null;
            if (authorId is not null)
            {
                var authorVisible = await db.WorkspaceMembers.AsNoTracking().AnyAsync(
                    m => m.WorkspaceId == workspace.Id && m.UserId == new UserId(authorId.Value),
                    ct);
                if (!authorVisible)
                {
                    return Results.Forbid();
                }

                scopedAuthor = new UserId(authorId.Value);
            }

            if (!SearchPolicies.TryParseInstant(from, endOfDay: false, out var fromInstant)
                || !SearchPolicies.TryParseInstant(to, endOfDay: true, out var toInstant))
            {
                return Results.BadRequest(new { error = "InvalidDateRange" });
            }

            if (!SearchPolicies.TryParseAttachmentKind(attachmentKind, out var kind))
            {
                return Results.BadRequest(new { error = "InvalidAttachmentKind" });
            }

            if (!string.IsNullOrWhiteSpace(cursor) && !SearchCursorCodec.TryDecode(cursor, out _))
            {
                return Results.BadRequest(new { error = "InvalidCursor" });
            }

            try
            {
                var page = await search.SearchMessagesAsync(
                    new SearchMessagesQuery(
                        workspace.TenantId,
                        profile.Id,
                        workspace.Id,
                        q ?? string.Empty,
                        scopedChannel,
                        SearchPolicies.NormalizeLimit(limit),
                        scopedAuthor,
                        fromInstant,
                        toInstant,
                        hasAttachment,
                        hasLink,
                        kind,
                        SearchPolicies.ParseSort(sort),
                        cursor),
                    ct);

                return Results.Ok(new SearchMessagesResponse(
                    page.Query,
                    page.Limit,
                    page.Items.Select(x => new SearchMessageHitResponse(
                        x.MessageId,
                        x.ChannelId,
                        x.ChannelName,
                        x.ChannelType,
                        x.Sequence,
                        x.AuthorUserId,
                        x.AuthorDisplayName,
                        x.BodyPreview,
                        x.CreatedAt,
                        x.Rank)).ToArray(),
                    page.Total,
                    page.Cursor));
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.GetBaseException().Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        }).RequirePermission(Permissions.Search.Messages, Permissions.Message.Read);
    }
}
