using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.ConversationsEndpointHelpers;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;
using static VibeChat.Api.Endpoints.MessagingEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class MessagingEndpoints
{
    internal static void MapCommands(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/commands", async (
            Guid workspaceId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var locale = UserLocales.Resolve(profile.Locale);
            var catalog = new (string Name, string Usage, string? Permission)[]
            {
                ("dm", "/dm @pessoa", null),
                ("topico", "/topico <texto>", Permissions.Channel.Create),
                ("convidar", "/convidar <email>", Permissions.Workspace.Admin),
                ("resumir", "/resumir", Permissions.Ai.Summarize),
                ("apagar", "/apagar", Permissions.Message.DeleteOwn),
                ("enquete", "/enquete", Permissions.Message.Send),
                ("ajuda", "/ajuda", null),
            };

            var allowed = new List<SlashCommandResponse>(catalog.Length);
            foreach (var item in catalog)
            {
                if (item.Permission is not null
                    && !await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, item.Permission, ct))
                {
                    continue;
                }

                allowed.Add(new SlashCommandResponse(
                    item.Name,
                    SlashCommandCatalog.Describe(item.Name, locale),
                    item.Usage,
                    item.Permission));
            }

            return Results.Ok(allowed);
        });
    }

    internal static void MapPolls(this RouteGroupBuilder v1)
    {
        v1.MapPost("/channels/{channelId:guid}/polls", async (
            Guid channelId,
            CreatePollRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPollWriter polls,
            IPermissionChecker permissions,
            IRateLimiter rateLimiter,
            RateLimitSettingsResolver rateLimits,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var rate = await rateLimits.ResolveAsync(channel.TenantId, ct);
            var allowed = await rateLimiter.TryAcquireAsync(
                RateLimitKeys.SendMessage(channel.TenantId, profile.Id),
                rate.SendPerMinute,
                TimeSpan.FromMinutes(1),
                ct);
            if (!allowed)
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            if (request.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                return Results.BadRequest(new { error = "messageId and idempotencyKey are required." });
            }

            try
            {
                var result = await polls.CreateAsync(new CreatePollCommand(
                    channel.TenantId,
                    profile.Id,
                    channel.Id,
                    new MessageId(request.MessageId),
                    request.IdempotencyKey,
                    request.Question,
                    request.Options ?? [],
                    request.AllowMultiple,
                    request.Anonymous,
                    request.ClosesAt), ct);

                var canVote = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.Send, ct);
                var poll = await PollQuery.LoadAsync(db, result.MessageId, profile.Id, includeVoters: true, canVote, ct);
                return Results.Accepted(
                    $"/api/v1/channels/{channel.Id.Value}/messages?after={result.Sequence - 1}",
                    new MessageResponse(
                        result.MessageId.Value,
                        channel.Id.Value,
                        result.Sequence,
                        profile.Id.Value,
                        PollPolicies.Normalize(request.Question),
                        result.CreatedAt,
                        null,
                        null,
                        profile.DisplayName,
                        [],
                        null,
                        null,
                        0,
                        channel.Id.Value,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        false,
                        poll));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        }).RequirePermission(Permissions.Message.Send);

        v1.MapPost("/polls/{pollId:guid}/votes", async (
            Guid pollId,
            CastPollVoteRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPollWriter polls,
            IPermissionChecker permissions,
            IRateLimiter rateLimiter,
            RateLimitSettingsResolver rateLimits,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var pollRow = await db.Polls.AsNoTracking().FirstOrDefaultAsync(x => x.MessageId == new MessageId(pollId), ct);
            if (pollRow is null)
            {
                return Results.Forbid();
            }

            var channel = await ResolveChannelAsync(pollRow.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var rate = await rateLimits.ResolveAsync(channel.TenantId, ct);
            if (!await rateLimiter.TryAcquireAsync(
                    RateLimitKeys.SendMessage(channel.TenantId, profile.Id),
                    rate.SendPerMinute,
                    TimeSpan.FromMinutes(1),
                    ct))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            try
            {
                await polls.VoteAsync(new CastPollVoteCommand(
                    channel.TenantId,
                    profile.Id,
                    new MessageId(pollId),
                    request.OptionIds ?? []), ct);
                var canVote = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.Send, ct);
                var poll = await PollQuery.LoadAsync(db, new MessageId(pollId), profile.Id, includeVoters: true, canVote, ct);
                return Results.Ok(poll);
            }
            catch (PollClosedException)
            {
                return Results.Conflict(new { error = "PollClosed" });
            }
            catch (PollNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        }).RequirePermission(Permissions.Message.Send);

        v1.MapDelete("/polls/{pollId:guid}/votes", async (
            Guid pollId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPollWriter polls,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var pollRow = await db.Polls.AsNoTracking().FirstOrDefaultAsync(x => x.MessageId == new MessageId(pollId), ct);
            if (pollRow is null)
            {
                return Results.Forbid();
            }

            var channel = await ResolveChannelAsync(pollRow.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            try
            {
                await polls.UnvoteAsync(channel.TenantId, profile.Id, new MessageId(pollId), ct);
                var canVote = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.Send, ct);
                var poll = await PollQuery.LoadAsync(db, new MessageId(pollId), profile.Id, includeVoters: true, canVote, ct);
                return Results.Ok(poll);
            }
            catch (PollClosedException)
            {
                return Results.Conflict(new { error = "PollClosed" });
            }
            catch (PollNotFoundException)
            {
                return Results.NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        }).RequirePermission(Permissions.Message.Send);

        v1.MapPost("/polls/{pollId:guid}/close", async (
            Guid pollId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPollWriter polls,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var pollRow = await db.Polls.AsNoTracking().FirstOrDefaultAsync(x => x.MessageId == new MessageId(pollId), ct);
            if (pollRow is null)
            {
                return Results.Forbid();
            }

            var channel = await ResolveChannelAsync(pollRow.ChannelId, profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var asAdmin = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Workspace.Admin, ct);
            try
            {
                await polls.CloseAsync(channel.TenantId, profile.Id, new MessageId(pollId), asAdmin, ct);
                var canVote = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.Send, ct);
                var poll = await PollQuery.LoadAsync(db, new MessageId(pollId), profile.Id, includeVoters: true, canVote, ct);
                return Results.Ok(poll);
            }
            catch (PollNotFoundException)
            {
                return Results.NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        }).RequirePermission(Permissions.Message.Send);
    }

    internal static void MapMessages(this RouteGroupBuilder v1)
    {
        v1.MapGet("/channels/{channelId:guid}/messages", async (
            Guid channelId,
            long? after,
            long? before,
            long? around,
            int? limit,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
            var cursorCount = (after.HasValue ? 1 : 0) + (before.HasValue ? 1 : 0) + (around.HasValue ? 1 : 0);
            if (cursorCount > 1)
            {
                return Results.BadRequest(new
                {
                    error = "InvalidMessagePagination",
                    message = "after, before and around are mutually exclusive.",
                });
            }

            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var take = Math.Clamp(limit ?? 50, 1, 100);
            var canVote = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.Send, ct);
            var membership = await db.ChannelMembers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ChannelId == channel.Id && x.UserId == profile.Id, ct);
            var minSeq = channel.Type == ChannelType.GroupDm ? membership?.JoinedSeq ?? 0 : 0;
            var page = await ListChannelMessagesAsync(channel, profile, db, take, after, before, around, canVote, minSeq, ct);
            return Results.Ok(page);
        });

        v1.MapPost("/channels/{channelId:guid}/messages", async (
            Guid channelId,
            SendMessageRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IMessageWriter writer,
            IRateLimiter rateLimiter,
            RateLimitSettingsResolver rateLimits,
            FilesSettingsResolver filesSettings,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var rate = await rateLimits.ResolveAsync(channel.TenantId, ct);
            var allowed = await rateLimiter.TryAcquireAsync(
                RateLimitKeys.SendMessage(channel.TenantId, profile.Id),
                rate.SendPerMinute,
                TimeSpan.FromMinutes(1),
                ct);
            if (!allowed)
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            var hasAttachments = request.AttachmentIds is { Length: > 0 };
            if (request.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                return Results.BadRequest(new { error = "messageId, idempotencyKey and body or attachments are required." });
            }

            var normalizedBody = MessageBodyPolicies.Normalize(request.Body);
            if (MessageBodyPolicies.IsEmpty(normalizedBody) && !hasAttachments)
            {
                return Results.BadRequest(new { error = "messageId, idempotencyKey and body or attachments are required." });
            }

            if (!MessageBodyPolicies.IsWithinLimit(normalizedBody))
            {
                return Results.BadRequest(MessageBodyPolicies.TooLongPayload());
            }

            var files = await filesSettings.ResolveAsync(channel.TenantId, ct);
            var maxAttachments = files.MaxAttachmentsPerMessage;
            var attachmentCount = request.AttachmentIds?.Length ?? 0;
            if (!AttachmentPolicies.IsWithinAttachmentCount(attachmentCount, maxAttachments))
            {
                return Results.BadRequest(new { error = "TooManyAttachments", max = maxAttachments });
            }

            try
            {
                var result = await writer.SendAsync(new SendMessageCommand(
                    channel.TenantId,
                    profile.Id,
                    channel.Id,
                    new MessageId(request.MessageId),
                    request.IdempotencyKey,
                    normalizedBody,
                    request.ReplyToMessageId is null ? null : new MessageId(request.ReplyToMessageId.Value),
                    request.ThreadId,
                    request.AttachmentIds), ct);

                var attachments = await db.Attachments.AsNoTracking()
                    .Where(x => x.MessageId == result.MessageId)
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => new AttachmentResponse(
                        x.Id,
                        x.FileName,
                        x.ContentType,
                        x.SizeBytes,
                        x.Status.ToString(),
                        x.Kind.ToString(),
                        x.DurationMs,
                        x.Waveform,
                        x.ThumbnailStatus != null ? x.ThumbnailStatus.ToString() : null,
                        x.Width,
                        x.Height,
                        x.PageCount))
                    .ToArrayAsync(ct);

                var replyToId = request.ReplyToMessageId is Guid rid ? new MessageId(rid) : (MessageId?)null;
                var replyToById = await LoadReplyToByIdsAsync(db, [replyToId], ct);
                replyToById.TryGetValue(request.ReplyToMessageId ?? Guid.Empty, out var replyTo);

                return Results.Accepted(
                    $"/api/v1/channels/{channel.Id.Value}/messages?after={result.Sequence - 1}",
                    new MessageResponse(
                        result.MessageId.Value,
                        channel.Id.Value,
                        result.Sequence,
                        profile.Id.Value,
                        normalizedBody,
                        result.CreatedAt,
                        null,
                        null,
                        profile.DisplayName,
                        attachments,
                        request.ThreadId,
                        request.ReplyToMessageId,
                        0,
                        channel.Id.Value,
                        null,
                        replyTo));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex) when (ex is MentionAllForbiddenException)
            {
                return Results.Json(
                    new { error = "MentionAllForbidden", message = "Channel-wide mention is not allowed in this channel." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        }).RequirePermission(Permissions.Message.Send);

        v1.MapPost("/workspaces/{workspaceId:guid}/messages/{messageId:guid}/forward", async (
            Guid workspaceId,
            Guid messageId,
            ForwardMessageRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IMessageWriter writer,
            IRateLimiter rateLimiter,
            RateLimitSettingsResolver rateLimits,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);

            var membership = await db.WorkspaceMembers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkspaceId == new WorkspaceId(workspaceId) && x.UserId == profile.Id, ct);
            if (membership is null)
            {
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                || request.TargetChannelIds is null
                || request.TargetChannelIds.Length == 0)
            {
                return Results.BadRequest(new { error = "idempotencyKey and targetChannelIds are required." });
            }

            if (request.TargetChannelIds.Length > MessageForwardPolicies.MaxTargets)
            {
                return Results.BadRequest(new { error = "TooManyForwardTargets", max = MessageForwardPolicies.MaxTargets });
            }

            var normalizedComment = MessageBodyPolicies.Normalize(request.Comment);
            if (!MessageBodyPolicies.IsWithinLimit(normalizedComment))
            {
                return Results.BadRequest(MessageBodyPolicies.TooLongPayload());
            }

            var rate = await rateLimits.ResolveAsync(membership.TenantId, ct);
            var allowed = await rateLimiter.TryAcquireAsync(
                RateLimitKeys.SendMessage(membership.TenantId, profile.Id),
                rate.SendPerMinute,
                TimeSpan.FromMinutes(1),
                ct);
            if (!allowed)
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            try
            {
                var result = await writer.ForwardAsync(new ForwardMessageCommand(
                    membership.TenantId,
                    profile.Id,
                    new WorkspaceId(workspaceId),
                    new MessageId(messageId),
                    request.IdempotencyKey,
                    request.TargetChannelIds.Select(x => new ChannelId(x)).ToArray(),
                    normalizedComment), ct);

                var messageIds = result.Messages.Select(x => x.MessageId).ToArray();
                var messageIdGuids = messageIds.Select(x => x.Value).ToHashSet();
                var attachmentCandidates = await db.Attachments.AsNoTracking()
                    .Where(x => x.MessageId != null)
                    .OrderBy(x => x.CreatedAt)
                    .ToArrayAsync(ct);
                var attachments = attachmentCandidates
                    .Where(x => x.MessageId is { } mid && messageIdGuids.Contains(mid.Value))
                    .ToArray();
                var attachmentsByMessage = attachments
                    .GroupBy(x => x.MessageId!.Value.Value)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => new AttachmentResponse(
                            x.Id,
                            x.FileName,
                            x.ContentType,
                            x.SizeBytes,
                            x.Status.ToString(),
                            x.Kind.ToString(),
                            x.DurationMs,
                            x.Waveform,
                            x.ThumbnailStatus?.ToString(),
                            x.Width,
                            x.Height,
                            x.PageCount)).ToArray());

                var createdRows = await db.Messages.AsNoTracking()
                    .Where(x => messageIds.Contains(x.Id))
                    .ToArrayAsync(ct);
                var forwardedFromById = await LoadForwardedFromByIdsAsync(
                    db,
                    createdRows.Select(x => (x.ForwardedFromMessageId, x.ForwardedFromChannelId)),
                    profile.Id,
                    ct);

                var responses = result.Messages.Select(m =>
                {
                    var row = createdRows.First(x => x.Id == m.MessageId);
                    forwardedFromById.TryGetValue(row.ForwardedFromMessageId?.Value ?? Guid.Empty, out var forwarded);
                    attachmentsByMessage.TryGetValue(m.MessageId.Value, out var atts);
                    return new MessageResponse(
                        m.MessageId.Value,
                        m.ChannelId.Value,
                        m.Sequence,
                        profile.Id.Value,
                        row.Body,
                        m.CreatedAt,
                        null,
                        null,
                        profile.DisplayName,
                        atts ?? [],
                        null,
                        null,
                        0,
                        m.ChannelId.Value,
                        null,
                        null,
                        row.ForwardedFromMessageId?.Value,
                        row.ForwardedFromChannelId?.Value,
                        forwarded);
                }).ToArray();

                return Results.Accepted(
                    $"/api/v1/workspaces/{workspaceId}/messages/{messageId}/forward",
                    new ForwardMessageResponse(responses));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        }).RequirePermission(Permissions.Message.Send);
    }

    internal static void MapMessageActions(this RouteGroupBuilder v1)
    {
        v1.MapPut("/channels/{channelId:guid}/messages/{messageId:guid}", async (Guid channelId, Guid messageId, EditMessageRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IOutboxWriter outbox, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.Body))
            {
                return Results.BadRequest(new { error = "body is required." });
            }

            var normalizedBody = MessageBodyPolicies.Normalize(request.Body);
            if (MessageBodyPolicies.IsEmpty(normalizedBody))
            {
                return Results.BadRequest(new { error = "body is required." });
            }

            if (!MessageBodyPolicies.IsWithinLimit(normalizedBody))
            {
                return Results.BadRequest(MessageBodyPolicies.TooLongPayload());
            }

            var message = await FindMessageInChannelAsync(db, channel.Id, new MessageId(messageId), ct);
            if (message is null || message.DeletedAt is not null)
            {
                return Results.NotFound();
            }

            var canEditOwn = message.AuthorId == profile.Id
                && await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.EditOwn, ct);
            if (!canEditOwn)
            {
                return Results.Forbid();
            }

            message.Body = normalizedBody;
            message.EditedAt = clock.UtcNow;
            outbox.Add(new OutboxMessage
            {
                TenantId = channel.TenantId,
                Type = nameof(MessageEditedEvent),
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = channel.TenantId.Value,
                    channelId,
                    conversationId = message.ConversationId.Value,
                    threadId = message.ThreadId,
                    messageId,
                    sequence = message.Sequence,
                    body = message.Body,
                    editedAt = message.EditedAt
                })
            });
            await db.SaveChangesAsync(ct);
            var attachments = await db.Attachments.AsNoTracking()
                .Where(x => x.MessageId == message.Id)
                .OrderBy(x => x.CreatedAt)
                .Select(x => new AttachmentResponse(
                    x.Id,
                    x.FileName,
                    x.ContentType,
                    x.SizeBytes,
                    x.Status.ToString(),
                    x.Kind.ToString(),
                    x.DurationMs,
                    x.Waveform,
                    x.ThumbnailStatus != null ? x.ThumbnailStatus.ToString() : null,
                    x.Width,
                    x.Height,
                    x.PageCount))
                .ToArrayAsync(ct);
            return Results.Ok(new MessageResponse(
                message.Id.Value,
                channel.Id.Value,
                message.Sequence,
                message.AuthorId.Value,
                message.Body,
                message.CreatedAt,
                message.EditedAt,
                message.DeletedAt,
                profile.DisplayName,
                attachments,
                message.ThreadId,
                message.ReplyToMessageId?.Value,
                0,
                message.ConversationId.Value));
        }).AllowPermissionGateExempt("conditional EditOwn vs authorship (B-023)");

        v1.MapPut("/channels/{channelId:guid}/messages/{messageId:guid}/reactions", async (
            Guid channelId,
            Guid messageId,
            ToggleReactionRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IOutboxWriter outbox,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var emoji = request.Emoji?.Trim() ?? string.Empty;
            if (!EmojiValidator.IsValid(emoji))
            {
                return Results.BadRequest(new { error = "InvalidEmoji", message = "Emoji is invalid or unsupported." });
            }

            var message = await FindMessageInChannelAsync(db, channel.Id, new MessageId(messageId), ct);
            if (message is null || message.DeletedAt is not null)
            {
                return Results.NotFound();
            }

            var existing = await db.Reactions.FirstOrDefaultAsync(
                x => x.MessageId == message.Id && x.UserId == profile.Id && x.Emoji == emoji,
                ct);

            var added = existing is null;
            if (existing is null)
            {
                db.Reactions.Add(new Reaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = channel.TenantId,
                    MessageId = message.Id,
                    UserId = profile.Id,
                    Emoji = emoji,
                    CreatedAt = clock.UtcNow
                });
            }
            else
            {
                db.Reactions.Remove(existing);
            }

            var snapshot = await BuildReactionSnapshotAsync(db, message.Id, profile.Id, emoji, added, ct);
            var toggledUserIds = snapshot.FirstOrDefault(x => x.Emoji == emoji)?.UserIds ?? [];
            var topUserIds = toggledUserIds.Take(3).Select(id => new UserId(id)).ToArray();
            var topProfiles = topUserIds.Length == 0
                ? []
                : await db.UserProfiles.AsNoTracking()
                    .Where(u => topUserIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.DisplayName })
                    .ToListAsync(ct);
            var topUsers = topUserIds
                .Select(id => topProfiles.FirstOrDefault(p => p.Id == id)?.DisplayName ?? "Membro")
                .ToArray();

            outbox.Add(new OutboxMessage
            {
                TenantId = channel.TenantId,
                Type = nameof(ReactionChangedEvent),
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = channel.TenantId.Value,
                    channelId,
                    conversationId = message.ConversationId.Value,
                    threadId = message.ThreadId,
                    messageId,
                    userId = profile.Id.Value,
                    emoji,
                    added,
                    occurredAt = clock.UtcNow,
                    topUsers,
                    reactions = snapshot.Select(x => new
                    {
                        emoji = x.Emoji,
                        count = x.Count,
                        userIds = x.UserIds
                    })
                })
            });
            await db.SaveChangesAsync(ct);

            var summaries = snapshot
                .Select(x => new ReactionSummaryResponse(x.Emoji, x.Count, x.UserIds.Contains(profile.Id.Value)))
                .ToArray();
            return Results.Ok(new ToggleReactionResponse(messageId, channelId, emoji, added, summaries));
        }).RequirePermission(Permissions.Message.React);

        v1.MapPost("/channels/{channelId:guid}/messages/{messageId:guid}/pin", async (
            Guid channelId,
            Guid messageId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IOutboxWriter outbox,
            IAuditWriter audit,
            IConversationSequenceStore sequences,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var message = await FindDirectChannelMessageAsync(db, channel.Id, new MessageId(messageId), ct);
            if (message is null)
            {
                return Results.BadRequest(new { error = "WrongChannel", message = "Message does not belong to this channel." });
            }

            if (message.DeletedAt is not null)
            {
                return Results.NotFound();
            }

            var existing = await db.PinnedMessages.FirstOrDefaultAsync(
                x => x.ChannelId == channel.Id && x.MessageId == message.Id,
                ct);
            if (existing is not null)
            {
                return Results.Ok(new PinMessageResponse(messageId, channelId, true, await CountPinsAsync(db, channel.Id, ct)));
            }

            var count = await CountPinsAsync(db, channel.Id, ct);
            if (count >= PinPolicies.MaxPinnedPerChannel)
            {
                return Results.BadRequest(new
                {
                    error = "PinLimitReached",
                    message = $"Pinned message limit of {PinPolicies.MaxPinnedPerChannel} reached. Unpin one before continuing.",
                    limit = PinPolicies.MaxPinnedPerChannel,
                    count
                });
            }

            db.PinnedMessages.Add(new PinnedMessage
            {
                Id = Guid.NewGuid(),
                TenantId = channel.TenantId,
                ChannelId = channel.Id,
                MessageId = message.Id,
                PinnedByUserId = profile.Id,
                PinnedAt = clock.UtcNow
            });

            await EmitPinChangedAsync(outbox, channel.TenantId, channel.Id, message.Id, profile.Id, pinned: true);
            audit.Add(new AuditEvent
            {
                TenantId = channel.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.MessagePin,
                EntityType = "Message",
                EntityId = message.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { channelId, messageId })
            });

            var actorName = profile.DisplayName;
            await AppendPinSystemEventAsync(
                db, sequences, outbox, clock, channel.TenantId, channel.Id, profile.Id, actorName, message.Id, pinned: true, ct);

            await db.SaveChangesAsync(ct);
            return Results.Ok(new PinMessageResponse(messageId, channelId, true, count + 1));
        }).RequirePermission(Permissions.Message.Pin);

        v1.MapDelete("/channels/{channelId:guid}/messages/{messageId:guid}/pin", async (
            Guid channelId,
            Guid messageId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IOutboxWriter outbox,
            IAuditWriter audit,
            IConversationSequenceStore sequences,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var message = await FindDirectChannelMessageAsync(db, channel.Id, new MessageId(messageId), ct);
            if (message is null)
            {
                return Results.BadRequest(new { error = "WrongChannel", message = "Message does not belong to this channel." });
            }

            var pin = await db.PinnedMessages.FirstOrDefaultAsync(
                x => x.ChannelId == channel.Id && x.MessageId == message.Id,
                ct);
            if (pin is null)
            {
                return Results.NoContent();
            }

            db.PinnedMessages.Remove(pin);
            await EmitPinChangedAsync(outbox, channel.TenantId, channel.Id, message.Id, profile.Id, pinned: false);
            audit.Add(new AuditEvent
            {
                TenantId = channel.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.MessageUnpin,
                EntityType = "Message",
                EntityId = message.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { channelId, messageId })
            });

            var actorName = profile.DisplayName;
            await AppendPinSystemEventAsync(
                db, sequences, outbox, clock, channel.TenantId, channel.Id, profile.Id, actorName, message.Id, pinned: false, ct);

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.Message.Pin);

        v1.MapGet("/channels/{channelId:guid}/pins", async (
            Guid channelId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var rawRows = await (
                from pin in db.PinnedMessages.AsNoTracking()
                join msg in db.Messages.AsNoTracking() on pin.MessageId equals msg.Id
                join author in db.UserProfiles.AsNoTracking() on msg.AuthorId equals author.Id
                join pinner in db.UserProfiles.AsNoTracking() on pin.PinnedByUserId equals pinner.Id
                where pin.ChannelId == channel.Id && msg.DeletedAt == null
                orderby pin.PinnedAt descending
                select new
                {
                    pin.MessageId,
                    msg.Sequence,
                    msg.Body,
                    AuthorName = author.DisplayName,
                    pin.PinnedByUserId,
                    PinnedByName = pinner.DisplayName,
                    pin.PinnedAt
                })
                .ToArrayAsync(ct);

            var rows = rawRows
                .Select(x => new PinnedMessageResponse(
                    x.MessageId.Value,
                    channel.Id.Value,
                    x.Sequence,
                    TruncateReplyPreview(x.Body),
                    x.AuthorName,
                    x.PinnedByUserId.Value,
                    x.PinnedByName,
                    x.PinnedAt))
                .ToArray();

            return Results.Ok(new ChannelPinsResponse(rows, rows.Length, PinPolicies.MaxPinnedPerChannel));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPost("/workspaces/{workspaceId:guid}/saved", async (
            Guid workspaceId,
            SaveMessageRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var noteError = ValidateSavedNote(request.Note, out var note);
            if (noteError is not null)
            {
                return Results.BadRequest(noteError);
            }

            var resolved = await ResolveReadableMessageAsync(db, tenant, workspace, profile.Id, new MessageId(request.MessageId), ct);
            if (resolved is null)
            {
                return Results.Forbid();
            }

            var (message, channel) = resolved.Value;
            if (message.DeletedAt is not null)
            {
                return Results.NotFound();
            }

            var existing = await db.SavedMessages.FirstOrDefaultAsync(
                x => x.UserId == profile.Id && x.MessageId == message.Id,
                ct);
            if (existing is not null)
            {
                if (note is not null && !string.Equals(existing.Note, note, StringComparison.Ordinal))
                {
                    existing.Note = note;
                    await db.SaveChangesAsync(ct);
                }

                return Results.Ok(await ToSavedMessageResponseAsync(db, existing, message, channel, profile.Id, ct));
            }

            var saved = new SavedMessage
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                UserId = profile.Id,
                MessageId = message.Id,
                ChannelId = channel.Id,
                Note = note,
                CompletedAt = null,
                CreatedAt = clock.UtcNow
            };
            db.SavedMessages.Add(saved);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await ToSavedMessageResponseAsync(db, saved, message, channel, profile.Id, ct));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPatch("/workspaces/{workspaceId:guid}/saved/{messageId:guid}", async (
            Guid workspaceId,
            Guid messageId,
            PatchSavedMessageRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var saved = await db.SavedMessages.FirstOrDefaultAsync(
                x => x.UserId == profile.Id && x.MessageId == new MessageId(messageId),
                ct);
            if (saved is null)
            {
                return Results.NotFound();
            }

            if (request.Note is not null)
            {
                var noteError = ValidateSavedNote(request.Note, out var note);
                if (noteError is not null)
                {
                    return Results.BadRequest(noteError);
                }

                saved.Note = note;
            }

            if (request.Completed.HasValue)
            {
                saved.CompletedAt = request.Completed.Value ? (saved.CompletedAt ?? clock.UtcNow) : null;
            }

            await db.SaveChangesAsync(ct);

            var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == saved.MessageId, ct);
            var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == saved.ChannelId, ct);
            if (message is null || channel is null)
            {
                var fallbackName = channel is null
                    ? string.Empty
                    : await ResolveSavedChannelDisplayNameAsync(db, channel, profile.Id, ct);
                return Results.Ok(new SavedMessageResponse(
                    saved.MessageId.Value,
                    saved.ChannelId.Value,
                    fallbackName,
                    channel?.Type.ToString() ?? "Public",
                    0,
                    Guid.Empty,
                    string.Empty,
                    "Mensagem removida",
                    saved.Note,
                    saved.CompletedAt,
                    saved.CreatedAt,
                    MessageRemoved: true));
            }

            return Results.Ok(await ToSavedMessageResponseAsync(db, saved, message, channel, profile.Id, ct));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapDelete("/workspaces/{workspaceId:guid}/saved/{messageId:guid}", async (
            Guid workspaceId,
            Guid messageId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var saved = await db.SavedMessages.FirstOrDefaultAsync(
                x => x.UserId == profile.Id && x.MessageId == new MessageId(messageId),
                ct);
            if (saved is null)
            {
                return Results.NoContent();
            }

            db.SavedMessages.Remove(saved);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.Message.Read);

        v1.MapGet("/workspaces/{workspaceId:guid}/saved", async (
            Guid workspaceId,
            bool? completed,
            int? limit,
            string? cursor,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var pageSize = Math.Clamp(limit ?? SavedMessagePolicies.DefaultPageSize, 1, SavedMessagePolicies.MaxPageSize);
            var wantCompleted = completed == true;
            if (!TryParseSavedCursor(cursor, out var cursorCreatedAt, out var cursorId))
            {
                return Results.BadRequest(new { error = "InvalidCursor" });
            }

            var pendingCount = await (
                from saved in db.SavedMessages.AsNoTracking()
                join channel in db.Channels.AsNoTracking() on saved.ChannelId equals channel.Id
                where saved.UserId == profile.Id
                    && saved.CompletedAt == null
                    && channel.WorkspaceId == workspace.Id
                    && (
                        (
                            (channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement)
                            && db.WorkspaceMembers.Any(wm =>
                                wm.TenantId == workspace.TenantId
                                && wm.WorkspaceId == channel.WorkspaceId
                                && wm.UserId == profile.Id)
                        )
                        || (
                            channel.Type != ChannelType.Public
                            && channel.Type != ChannelType.Announcement
                            && db.ChannelMembers.Any(cm =>
                                cm.TenantId == workspace.TenantId
                                && cm.ChannelId == channel.Id
                                && cm.UserId == profile.Id)
                        )
                    )
                select saved.Id).CountAsync(ct);

            var query =
                from saved in db.SavedMessages.AsNoTracking()
                join msg in db.Messages.AsNoTracking() on saved.MessageId equals msg.Id into msgJoin
                from msg in msgJoin.DefaultIfEmpty()
                join channel in db.Channels.AsNoTracking() on saved.ChannelId equals channel.Id into channelJoin
                from channel in channelJoin.DefaultIfEmpty()
                join author in db.UserProfiles.AsNoTracking() on msg.AuthorId equals author.Id into authorJoin
                from author in authorJoin.DefaultIfEmpty()
                where saved.UserId == profile.Id
                    && (wantCompleted ? saved.CompletedAt != null : saved.CompletedAt == null)
                    && channel != null
                    && channel.WorkspaceId == workspace.Id
                    && (
                        (
                            (channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement)
                            && db.WorkspaceMembers.Any(wm =>
                                wm.TenantId == workspace.TenantId
                                && wm.WorkspaceId == channel.WorkspaceId
                                && wm.UserId == profile.Id)
                        )
                        || (
                            channel.Type != ChannelType.Public
                            && channel.Type != ChannelType.Announcement
                            && db.ChannelMembers.Any(cm =>
                                cm.TenantId == workspace.TenantId
                                && cm.ChannelId == channel.Id
                                && cm.UserId == profile.Id)
                        )
                    )
                select new
                {
                    saved.Id,
                    saved.MessageId,
                    saved.ChannelId,
                    ChannelName = channel.Name,
                    ChannelType = channel.Type,
                    Sequence = msg != null ? msg.Sequence : 0L,
                    AuthorId = msg != null ? msg.AuthorId : default(UserId),
                    AuthorName = author != null ? author.DisplayName : string.Empty,
                    Body = msg != null ? msg.Body : string.Empty,
                    Deleted = msg == null || msg.DeletedAt != null,
                    saved.Note,
                    saved.CompletedAt,
                    saved.CreatedAt
                };

            if (cursorCreatedAt is not null && cursorId is not null)
            {
                var cAt = cursorCreatedAt.Value;
                var cId = cursorId.Value;
                query = query.Where(x =>
                    x.CreatedAt < cAt || (x.CreatedAt == cAt && x.Id < cId));
            }

            var rawRows = await query
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Take(pageSize + 1)
                .ToListAsync(ct);

            string? nextCursor = null;
            if (rawRows.Count > pageSize)
            {
                var last = rawRows[pageSize - 1];
                nextCursor = EncodeSavedCursor(last.CreatedAt, last.Id);
                rawRows = rawRows.Take(pageSize).ToList();
            }

            var channelStubs = rawRows
                .GroupBy(x => x.ChannelId.Value)
                .Select(g =>
                {
                    var first = g.First();
                    return new Channel { Id = first.ChannelId, Type = first.ChannelType, Name = first.ChannelName };
                })
                .ToArray();
            var peers = await ResolveDirectPeersAsync(channelStubs, profile.Id, db, ct);

            var items = rawRows
                .Select(x =>
                {
                    var channelName = x.ChannelName;
                    if (x.ChannelType == ChannelType.Direct)
                    {
                        channelName = peers.TryGetValue(x.ChannelId, out var peer) && !string.IsNullOrWhiteSpace(peer.DisplayName)
                            ? peer.DisplayName
                            : "DM";
                    }

                    return new SavedMessageResponse(
                        x.MessageId.Value,
                        x.ChannelId.Value,
                        channelName,
                        x.ChannelType.ToString(),
                        x.Sequence,
                        x.AuthorId.Value,
                        x.AuthorName,
                        x.Deleted ? "Mensagem removida" : TruncateReplyPreview(x.Body),
                        x.Note,
                        x.CompletedAt,
                        x.CreatedAt,
                        x.Deleted);
                })
                .ToArray();

            return Results.Ok(new SavedMessagesPageResponse(items, nextCursor, pendingCount));
        });

        v1.MapGet("/channels/{channelId:guid}/messages/{messageId:guid}/reactions/{emoji}/users", async (
            Guid channelId,
            Guid messageId,
            string emoji,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var normalizedEmoji = Uri.UnescapeDataString(emoji).Trim();
            if (!EmojiValidator.IsValid(normalizedEmoji))
            {
                return Results.BadRequest(new { error = "InvalidEmoji", message = "Emoji is invalid or unsupported." });
            }

            var message = await FindMessageInChannelAsync(db, channel.Id, new MessageId(messageId), ct);
            if (message is null || message.DeletedAt is not null)
            {
                return Results.NotFound();
            }

            var reactorIds = await db.Reactions.AsNoTracking()
                .Where(x => x.MessageId == message.Id && x.Emoji == normalizedEmoji)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.UserId)
                .ToListAsync(ct);

            var profiles = reactorIds.Count == 0
                ? []
                : await db.UserProfiles.AsNoTracking()
                    .Where(u => reactorIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.DisplayName })
                    .ToListAsync(ct);

            var users = reactorIds
                .Select(id =>
                {
                    var found = profiles.FirstOrDefault(p => p.Id == id);
                    return new ReactionUserResponse(id.Value, found?.DisplayName ?? "Membro");
                })
                .ToArray();

            return Results.Ok(new ReactionUsersResponse(normalizedEmoji, users, users.Length));
        }).RequirePermission(Permissions.Message.React);

        v1.MapDelete("/channels/{channelId:guid}/messages/{messageId:guid}", async (Guid channelId, Guid messageId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPermissionChecker permissions, IOutboxWriter outbox, IAuditWriter audit, IClock clock, CancellationToken ct) =>
        {
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

            if (message.DeletedAt is not null)
            {
                return Results.NoContent();
            }

            var canDeleteOwn = message.AuthorId == profile.Id
                && await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.DeleteOwn, ct);
            var canDeleteAny = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Message.DeleteAny, ct);
            if (!canDeleteOwn && !canDeleteAny)
            {
                return Results.Forbid();
            }

            message.DeletedAt = clock.UtcNow;
            message.DeletedBy = profile.Id;

            var pin = await db.PinnedMessages.FirstOrDefaultAsync(
                x => x.ChannelId == channel.Id && x.MessageId == message.Id,
                ct);
            if (pin is not null)
            {
                db.PinnedMessages.Remove(pin);
                await EmitPinChangedAsync(outbox, channel.TenantId, channel.Id, message.Id, profile.Id, pinned: false);
            }

            outbox.Add(new OutboxMessage
            {
                TenantId = channel.TenantId,
                Type = nameof(MessageDeletedEvent),
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = channel.TenantId.Value,
                    channelId,
                    conversationId = message.ConversationId.Value,
                    threadId = message.ThreadId,
                    messageId,
                    sequence = message.Sequence,
                    deletedAt = message.DeletedAt
                })
            });
            audit.Add(new AuditEvent { TenantId = channel.TenantId, ActorUserId = profile.Id, Action = AuditActions.MessageDelete, EntityType = "Message", EntityId = message.Id.ToString(), MetadataJson = JsonSerializer.Serialize(new { channelId, threadId = message.ThreadId, message.Sequence }) });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AllowPermissionGateExempt("conditional DeleteOwn/DeleteAny (B-023)");

        v1.MapDelete("/channels/{channelId:guid}/messages/{messageId:guid}/link-preview", async (
            Guid channelId,
            Guid messageId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IPermissionChecker permissions,
            IClock clock,
            CancellationToken ct) =>
        {
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

            var isAuthor = message.AuthorId == profile.Id;
            var isAdmin = await permissions.HasPermissionAsync(channel.TenantId, profile.Id, Permissions.Workspace.Admin, ct);
            if (!isAuthor && !isAdmin)
            {
                return Results.Forbid();
            }

            var links = await db.MessageLinkPreviews
                .Where(x => x.MessageId == message.Id && x.RemovedAt == null)
                .ToListAsync(ct);
            if (links.Count == 0)
            {
                return Results.NoContent();
            }

            var now = clock.UtcNow;
            foreach (var link in links)
            {
                link.RemovedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AllowPermissionGateExempt("author or workspace.admin (B-091)");

        v1.MapGet("/channels/{channelId:guid}/messages/{messageId:guid}/link-preview/image", async (
            Guid channelId,
            Guid messageId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IObjectStorage storage,
            FilesSettingsResolver filesSettings,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var message = await FindMessageInChannelAsync(db, channel.Id, new MessageId(messageId), ct);
            if (message is null || message.DeletedAt is not null)
            {
                return Results.NotFound();
            }

            var row = await (
                from link in db.MessageLinkPreviews.AsNoTracking()
                join preview in db.LinkPreviews.AsNoTracking() on link.LinkPreviewId equals preview.Id
                where link.MessageId == message.Id
                      && link.RemovedAt == null
                      && preview.Status == LinkPreviewStatus.Ready
                      && preview.ImageKey != null
                select preview
            ).FirstOrDefaultAsync(ct);

            if (row?.ImageKey is null)
            {
                return Results.NotFound();
            }

            var files = await filesSettings.ResolveAsync(channel.TenantId, ct);
            var downloadTtl = TimeSpan.FromSeconds(files.PresignDownloadTtlSeconds);
            var download = await storage.CreateDownloadUrlAsync(
                row.ImageKey,
                "preview",
                downloadTtl,
                ct);
            return Results.Ok(new
            {
                messageId,
                downloadUrl = download.Url.ToString(),
                expiresAt = download.ExpiresAt,
                contentType = row.ImageContentType ?? "image/jpeg"
            });
        });
    }
}
