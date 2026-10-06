using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Messaging;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;
using static VibeChat.Api.Endpoints.MessagingEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class MessageHistoryEndpoints
{
    internal static void MapMessageHistory(this RouteGroupBuilder v1)
    {
        v1.MapGet("/channels/{channelId:guid}/messages/{messageId:guid}/history", async (
            Guid channelId,
            Guid messageId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IAuditWriter audit,
            IOptions<MessageHistoryOptions> options,
            IClock clock,
            CancellationToken ct) =>
        {
            if (!options.Value.Enabled)
            {
                return Results.NotFound(new { error = MessageHistoryPolicies.Disabled });
            }

            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            if (!await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.HistoryRead, ct))
            {
                return Results.Forbid();
            }

            var message = await FindMessageInChannelAsync(db, channel.Id, new MessageId(messageId), ct);
            if (message is null)
            {
                return Results.NotFound();
            }

            var policy = await db.MessageLifecyclePolicies.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == channel.TenantId, ct);
            var visible = policy?.HistoryEnabled ?? true;
            var isAdmin = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Workspace.Admin, ct);
            if (!visible && !isAdmin)
            {
                return Results.Json(new { error = MessageHistoryPolicies.Hidden }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (!visible)
            {
                audit.Add(new AuditEvent
                {
                    TenantId = channel.TenantId,
                    ActorUserId = profile.Id,
                    Action = AuditActions.MessageHistoryRead,
                    EntityType = "Message",
                    EntityId = message.Id.Value.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new { channelId, breakGlass = true })
                });
                await db.SaveChangesAsync(ct);
            }

            var versions = await db.MessageVersions.AsNoTracking()
                .Where(x => x.MessageId == message.Id)
                .OrderBy(x => x.VersionNumber)
                .Select(x => new MessageVersionResponse(x.VersionNumber, x.Body, x.ActorUserId.Value, x.CreatedAt))
                .ToArrayAsync(ct);
            var currentBody = message.DeletedAt is null && !MessageHistoryPolicies.IsTombstone(message.Body)
                ? message.Body
                : string.Empty;
            return Results.Ok(new MessageHistoryResponse(versions, currentBody, message.EditedAt));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapGet("/channels/{channelId:guid}/messages/{messageId:guid}/move", async (
            Guid channelId,
            Guid messageId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IOptions<MessageHistoryOptions> options,
            IClock clock,
            CancellationToken ct) =>
        {
            if (!options.Value.Enabled)
            {
                return Results.NotFound(new { error = MessageHistoryPolicies.Disabled });
            }

            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var message = await FindMessageInChannelAsync(db, channel.Id, new MessageId(messageId), ct);
            if (message is null)
            {
                return Results.NotFound();
            }

            var destination = await RevealAsync(db, profile.Id, message.MovedToChannelId, message.MovedToMessageId, ct);
            var origin = await RevealAsync(db, profile.Id, message.MovedFromChannelId, message.MovedFromMessageId, ct);
            return Results.Ok(new MessageMoveLinksResponse(destination, origin));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPost("/channels/{channelId:guid}/messages/{messageId:guid}/move", async (
            Guid channelId,
            Guid messageId,
            MoveMessageRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IConversationSequenceStore sequences,
            IOutboxWriter outbox,
            IAuditWriter audit,
            IOptions<MessageHistoryOptions> options,
            IClock clock,
            CancellationToken ct) =>
        {
            if (!options.Value.Enabled)
            {
                return Results.NotFound(new { error = MessageHistoryPolicies.Disabled });
            }

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                || request.IdempotencyKey.Length > MessageHistoryPolicies.MaxIdempotencyKeyLength
                || request.TargetChannelId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "idempotencyKey and targetChannelId are required." });
            }

            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var sourceChannel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            var destinationChannel = await ResolveChannelAsync(new ChannelId(request.TargetChannelId), profile.Id, db, tenant, ct);
            if (sourceChannel is null || destinationChannel is null)
            {
                return Results.Forbid();
            }

            if (!await permissions.HasPermissionAsync(sourceChannel.TenantId, profile.Id, Permissions.Message.Move, ct))
            {
                return Results.Forbid();
            }

            var message = await FindMessageInChannelAsync(db, sourceChannel.Id, new MessageId(messageId), ct);
            if (message is null || message.DeletedAt is not null)
            {
                return Results.NotFound();
            }

            var (success, failure) = await MessageHistoryCommands.MoveAsync(
                db,
                sequences,
                outbox,
                audit,
                sourceChannel,
                destinationChannel,
                message,
                profile.Id,
                request.IdempotencyKey.Trim(),
                request.Scope,
                request.LeaveTombstone,
                clock.UtcNow,
                ct);
            if (failure is { } denied)
            {
                return denied.StatusCode switch
                {
                    400 => Results.BadRequest(new { error = denied.Error }),
                    404 => Results.NotFound(new { error = denied.Error }),
                    409 => Results.Conflict(new { error = denied.Error }),
                    _ => Results.Json(new { error = denied.Error }, statusCode: denied.StatusCode)
                };
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new MoveMessageResponse(
                success!.SourceMessageId,
                success.DestinationMessageId,
                success.DestinationChannelId,
                success.DestinationSequence,
                success.Tombstone));
        }).RequirePermission(Permissions.Message.Move);
    }

    internal static async Task<MessageResponse[]> ApplyMoveLinksAsync(
        VibeChatDbContext db,
        UserId viewerId,
        MessageResponse[] messages,
        CancellationToken ct)
    {
        if (messages.Length == 0)
        {
            return messages;
        }

        var ids = messages.Select(m => new MessageId(m.Id)).ToArray();
        var links = await db.Messages.AsNoTracking()
            .Where(m => ids.Contains(m.Id) && (m.MovedToMessageId != null || m.MovedFromMessageId != null))
            .Select(m => new
            {
                Id = m.Id.Value,
                ToMessage = m.MovedToMessageId,
                ToChannel = m.MovedToChannelId,
                FromMessage = m.MovedFromMessageId,
                FromChannel = m.MovedFromChannelId
            })
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return messages;
        }

        var channelIds = links
            .SelectMany(x => new[] { x.ToChannel, x.FromChannel })
            .Where(x => x != null)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var memberChannels = (await db.ChannelMembers.AsNoTracking()
            .Where(x => x.UserId == viewerId && channelIds.Contains(x.ChannelId))
            .Select(x => x.ChannelId.Value)
            .ToListAsync(ct)).ToHashSet();
        var targetIds = links
            .SelectMany(x => new[] { x.ToMessage, x.FromMessage })
            .Where(x => x != null)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var sequences = await db.Messages.AsNoTracking()
            .Where(m => targetIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id.Value, m => m.Sequence, ct);
        var byId = links.ToDictionary(x => x.Id);
        return messages.Select(message =>
        {
            if (!byId.TryGetValue(message.Id, out var link))
            {
                return message;
            }

            var toVisible = link.ToChannel is { } toChannel && memberChannels.Contains(toChannel.Value);
            var fromVisible = link.FromChannel is { } fromChannel && memberChannels.Contains(fromChannel.Value);
            return message with
            {
                MovedToAccessible = toVisible,
                MovedToMessageId = toVisible ? link.ToMessage?.Value : null,
                MovedToChannelId = toVisible ? link.ToChannel?.Value : null,
                MovedToSequence = toVisible && link.ToMessage is { } toMessage && sequences.TryGetValue(toMessage.Value, out var toSeq) ? toSeq : null,
                MovedFromMessageId = fromVisible ? link.FromMessage?.Value : null,
                MovedFromChannelId = fromVisible ? link.FromChannel?.Value : null,
                MovedFromSequence = fromVisible && link.FromMessage is { } fromMessage && sequences.TryGetValue(fromMessage.Value, out var fromSeq) ? fromSeq : null
            };
        }).ToArray();
    }

    private static async Task<MessageMoveLinkResponse> RevealAsync(
        VibeChatDbContext db,
        UserId viewerId,
        ChannelId? channelId,
        MessageId? messageId,
        CancellationToken ct)
    {
        if (channelId is null || messageId is null)
        {
            return new MessageMoveLinkResponse(false);
        }

        var member = await db.ChannelMembers.AsNoTracking()
            .AnyAsync(x => x.UserId == viewerId && x.ChannelId == channelId, ct);
        if (!member)
        {
            return new MessageMoveLinkResponse(false);
        }

        var sequence = await db.Messages.AsNoTracking()
            .Where(x => x.Id == messageId)
            .Select(x => (long?)x.Sequence)
            .FirstOrDefaultAsync(ct);
        return new MessageMoveLinkResponse(true, channelId.Value.Value, messageId.Value.Value, sequence);
    }
}
