using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;
using static VibeChat.Api.Endpoints.MessagingEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class SchedulingEndpoints
{
    internal static void MapScheduling(this RouteGroupBuilder v1)
    {
        v1.MapPost("/channels/{channelId:guid}/scheduled-messages", async (
            Guid channelId,
            CreateScheduledMessageRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            IAuditWriter audit,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var keyError = ValidateIdempotencyKey(request.IdempotencyKey, out var key);
            if (keyError is not null)
            {
                return keyError;
            }

            var existing = await db.ScheduledMessages.FirstOrDefaultAsync(
                x => x.AuthorId == profile.Id && x.ClientIdempotencyKey == key, ct);
            if (existing is not null)
            {
                return Results.Ok(await ToScheduledItemAsync(db, existing, profile.Id, ct));
            }

            var body = MessageBodyPolicies.Normalize(request.Body);
            if (MessageBodyPolicies.IsEmpty(body))
            {
                return Results.BadRequest(new { error = "MessageBodyRequired" });
            }

            if (!MessageBodyPolicies.IsWithinLimit(body))
            {
                return Results.BadRequest(MessageBodyPolicies.TooLongPayload());
            }

            if (!ScheduleTime.TryToUtc(request.SendAtLocal, request.TimeZone, out var sendAtUtc, out var timeError))
            {
                return Results.BadRequest(new { error = timeError });
            }

            var horizonError = ScheduleTime.ValidateHorizon(sendAtUtc, clock.UtcNow);
            if (horizonError is not null)
            {
                return Results.BadRequest(new { error = horizonError });
            }

            if (request.ThreadId is Guid threadId)
            {
                var thread = await db.MessageThreads.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == threadId && x.ChannelId == channel.Id, ct);
                if (thread is null)
                {
                    return Results.BadRequest(new { error = "ThreadNotFound" });
                }
            }

            if (request.ReplyToMessageId is Guid replyId)
            {
                var reply = await db.Messages.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == new MessageId(replyId), ct);
                if (reply is null || reply.DeletedAt is not null)
                {
                    return Results.BadRequest(new { error = "ReplyToNotFound" });
                }
            }

            var pending = await db.ScheduledMessages.CountAsync(
                x => x.AuthorId == profile.Id && (x.Status == ScheduleStatuses.Pending || x.Status == ScheduleStatuses.Claimed),
                ct);
            if (pending >= SchedulePolicies.MaxPendingPerUser)
            {
                return Results.BadRequest(new { error = "ScheduleLimitReached" });
            }

            var now = clock.UtcNow;
            var id = Guid.NewGuid();
            var scheduled = new ScheduledMessage
            {
                Id = id,
                TenantId = channel.TenantId,
                WorkspaceId = channel.WorkspaceId,
                AuthorId = profile.Id,
                ChannelId = channel.Id,
                ThreadId = request.ThreadId,
                ReplyToMessageId = request.ReplyToMessageId is Guid replyTo ? new MessageId(replyTo) : null,
                PlannedMessageId = MessageId.New(),
                Body = body,
                SendAtUtc = sendAtUtc,
                TimeZone = request.TimeZone.Trim(),
                Status = ScheduleStatuses.Pending,
                ClientIdempotencyKey = key,
                SendIdempotencyKey = $"sched:{id:N}",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.ScheduledMessages.Add(scheduled);
            audit.Add(new AuditEvent
            {
                TenantId = channel.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.ScheduleCreate,
                EntityType = "ScheduledMessage",
                EntityId = id.ToString(),
                MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    channelId = channel.Id.Value,
                    sendAtUtc,
                    timeZone = scheduled.TimeZone
                }),
                OccurredAt = now
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                var raced = await db.ScheduledMessages.AsNoTracking().FirstOrDefaultAsync(
                    x => x.AuthorId == profile.Id && x.ClientIdempotencyKey == key, ct);
                if (raced is not null)
                {
                    return Results.Ok(await ToScheduledItemAsync(db, raced, profile.Id, ct));
                }

                throw;
            }

            return Results.Ok(await ToScheduledItemAsync(db, scheduled, profile.Id, ct));
        }).RequirePermission(Permissions.Message.Send);

        v1.MapPatch("/workspaces/{workspaceId:guid}/scheduled-messages/{scheduledMessageId:guid}", async (
            Guid workspaceId,
            Guid scheduledMessageId,
            UpdateScheduledMessageRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            IAuditWriter audit,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var item = await db.ScheduledMessages.FirstOrDefaultAsync(
                x => x.Id == scheduledMessageId && x.AuthorId == profile.Id && x.WorkspaceId == workspace.Id, ct);
            if (item is null)
            {
                return Results.NotFound();
            }

            if (item.Status != ScheduleStatuses.Pending)
            {
                return item.Status == ScheduleStatuses.Cancelled
                    ? Results.Conflict(new { error = "ScheduleCancelled" })
                    : Results.Conflict(new { error = "ScheduleAlreadyClaimed" });
            }

            var body = request.Body is null ? item.Body : MessageBodyPolicies.Normalize(request.Body);
            if (MessageBodyPolicies.IsEmpty(body))
            {
                return Results.BadRequest(new { error = "MessageBodyRequired" });
            }

            if (!MessageBodyPolicies.IsWithinLimit(body))
            {
                return Results.BadRequest(MessageBodyPolicies.TooLongPayload());
            }

            var zone = string.IsNullOrWhiteSpace(request.TimeZone) ? item.TimeZone : request.TimeZone;
            DateTimeOffset sendAtUtc = item.SendAtUtc;
            if (request.SendAtLocal is not null || request.TimeZone is not null)
            {
                var local = request.SendAtLocal ?? FormatLocal(item.SendAtUtc, item.TimeZone);
                if (!ScheduleTime.TryToUtc(local, zone, out sendAtUtc, out var timeError))
                {
                    return Results.BadRequest(new { error = timeError });
                }

                var horizonError = ScheduleTime.ValidateHorizon(sendAtUtc, clock.UtcNow);
                if (horizonError is not null)
                {
                    return Results.BadRequest(new { error = horizonError });
                }
            }

            var now = clock.UtcNow;
            var updated = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE messaging.scheduled_messages
                SET "Body" = {body},
                    "SendAtUtc" = {sendAtUtc},
                    "TimeZone" = {zone.Trim()},
                    "UpdatedAt" = {now}
                WHERE "Id" = {item.Id}
                  AND "AuthorId" = {profile.Id.Value}
                  AND "Status" = 'Pending'
                """, ct);
            if (updated == 0)
            {
                return Results.Conflict(new { error = "ScheduleAlreadyClaimed" });
            }

            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.ScheduleUpdate,
                EntityType = "ScheduledMessage",
                EntityId = item.Id.ToString(),
                MetadataJson = "{}",
                OccurredAt = now
            });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            var fresh = await db.ScheduledMessages.AsNoTracking().FirstAsync(x => x.Id == item.Id, ct);
            return Results.Ok(await ToScheduledItemAsync(db, fresh, profile.Id, ct));
        }).RequirePermission(Permissions.Message.Send);

        v1.MapDelete("/workspaces/{workspaceId:guid}/scheduled-messages/{scheduledMessageId:guid}", async (
            Guid workspaceId,
            Guid scheduledMessageId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            IAuditWriter audit,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            return await CancelScheduledAsync(db, audit, clock, workspace.TenantId, profile.Id, scheduledMessageId, ct);
        }).RequirePermission(Permissions.Message.Send);

        v1.MapGet("/workspaces/{workspaceId:guid}/schedule", async (
            Guid workspaceId,
            int? limit,
            string? cursor,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            IChannelMembershipReader channels,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            if (!TryParseScheduleCursor(cursor, out var cursorAt, out var cursorId))
            {
                return Results.BadRequest(new { error = "InvalidCursor" });
            }

            var pageSize = Math.Clamp(limit ?? SchedulePolicies.DefaultPageSize, 1, SchedulePolicies.MaxPageSize);
            var scheduled = await db.ScheduledMessages.AsNoTracking()
                .Where(x => x.AuthorId == profile.Id && x.WorkspaceId == workspace.Id)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Take(200)
                .ToListAsync(ct);
            var reminders = await db.Reminders.AsNoTracking()
                .Where(x => x.UserId == profile.Id && x.WorkspaceId == workspace.Id)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Take(200)
                .ToListAsync(ct);

            var merged = new List<(DateTimeOffset CreatedAt, Guid Id, bool Scheduled, object Row)>();
            merged.AddRange(scheduled.Select(x => (x.CreatedAt, x.Id, true, (object)x)));
            merged.AddRange(reminders.Select(x => (x.CreatedAt, x.Id, false, (object)x)));
            var ordered = merged
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .ToList();
            if (cursorAt is not null && cursorId is not null)
            {
                ordered = ordered
                    .Where(x => x.CreatedAt < cursorAt || (x.CreatedAt == cursorAt && x.Id.CompareTo(cursorId.Value) < 0))
                    .ToList();
            }

            var page = ordered.Take(pageSize + 1).ToList();
            var hasMore = page.Count > pageSize;
            if (hasMore)
            {
                page.RemoveAt(page.Count - 1);
            }

            var items = new List<ScheduleItemResponse>(page.Count);
            foreach (var entry in page)
            {
                if (entry.Scheduled)
                {
                    items.Add(await ToScheduledItemAsync(db, (ScheduledMessage)entry.Row, profile.Id, ct));
                }
                else
                {
                    items.Add(await ToReminderItemAsync(db, channels, (Reminder)entry.Row, ct));
                }
            }

            string? next = null;
            if (hasMore && page.Count > 0)
            {
                var last = page[^1];
                next = EncodeScheduleCursor(last.CreatedAt, last.Id);
            }

            return Results.Ok(new SchedulePageResponse(items.ToArray(), next));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPost("/workspaces/{workspaceId:guid}/reminders", async (
            Guid workspaceId,
            CreateReminderRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            IAuditWriter audit,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var keyError = ValidateIdempotencyKey(request.IdempotencyKey, out var key);
            if (keyError is not null)
            {
                return keyError;
            }

            var existing = await db.Reminders.FirstOrDefaultAsync(
                x => x.UserId == profile.Id && x.ClientIdempotencyKey == key, ct);
            if (existing is not null)
            {
                return Results.Ok(await ToReminderItemAsync(db, http.RequestServices.GetRequiredService<IChannelMembershipReader>(), existing, ct));
            }

            var kind = request.TargetKind?.Trim() ?? string.Empty;
            if (kind is not (ReminderTargets.Time or ReminderTargets.Message or ReminderTargets.Thread))
            {
                return Results.BadRequest(new { error = "InvalidReminderTarget" });
            }

            var noteError = ValidateSavedNote(request.Note, out var note);
            if (noteError is not null)
            {
                return Results.BadRequest(noteError);
            }

            if (!ScheduleTime.TryToUtc(request.RemindAtLocal, request.TimeZone, out var remindAtUtc, out var timeError))
            {
                return Results.BadRequest(new { error = timeError });
            }

            var horizonError = ScheduleTime.ValidateHorizon(remindAtUtc, clock.UtcNow);
            if (horizonError is not null)
            {
                return Results.BadRequest(new { error = horizonError });
            }

            ChannelId? channelId = null;
            MessageId? messageId = null;
            Guid? threadId = null;
            if (kind == ReminderTargets.Message)
            {
                if (request.MessageId is not Guid rawMessageId)
                {
                    return Results.BadRequest(new { error = "MessageRequired" });
                }

                var resolved = await ResolveReadableMessageAsync(db, tenant, workspace, profile.Id, new MessageId(rawMessageId), ct);
                if (resolved is null || resolved.Value.Message.DeletedAt is not null)
                {
                    return Results.Forbid();
                }

                channelId = resolved.Value.Channel.Id;
                messageId = resolved.Value.Message.Id;
                threadId = resolved.Value.Message.ThreadId;
            }
            else if (kind == ReminderTargets.Thread)
            {
                if (request.ThreadId is not Guid rawThreadId)
                {
                    return Results.BadRequest(new { error = "ThreadRequired" });
                }

                var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rawThreadId, ct);
                if (thread is null)
                {
                    return Results.NotFound();
                }

                var channel = await ResolveChannelAsync(thread.ChannelId, profile.Id, db, tenant, ct);
                if (channel is null || channel.WorkspaceId != workspace.Id)
                {
                    return Results.Forbid();
                }

                channelId = channel.Id;
                threadId = thread.Id;
            }

            var pending = await db.Reminders.CountAsync(
                x => x.UserId == profile.Id && (x.Status == ScheduleStatuses.Pending || x.Status == ScheduleStatuses.Claimed),
                ct);
            if (pending >= SchedulePolicies.MaxPendingPerUser)
            {
                return Results.BadRequest(new { error = "ReminderLimitReached" });
            }

            var now = clock.UtcNow;
            var reminder = new Reminder
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                UserId = profile.Id,
                TargetKind = kind,
                ChannelId = channelId,
                MessageId = messageId,
                ThreadId = threadId,
                Note = note,
                RemindAtUtc = remindAtUtc,
                TimeZone = request.TimeZone.Trim(),
                Status = ScheduleStatuses.Pending,
                ClientIdempotencyKey = key,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Reminders.Add(reminder);
            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.ReminderCreate,
                EntityType = "Reminder",
                EntityId = reminder.Id.ToString(),
                MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { targetKind = kind, remindAtUtc }),
                OccurredAt = now
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                var raced = await db.Reminders.AsNoTracking().FirstOrDefaultAsync(
                    x => x.UserId == profile.Id && x.ClientIdempotencyKey == key, ct);
                if (raced is not null)
                {
                    return Results.Ok(await ToReminderItemAsync(db, http.RequestServices.GetRequiredService<IChannelMembershipReader>(), raced, ct));
                }

                throw;
            }

            return Results.Ok(await ToReminderItemAsync(db, http.RequestServices.GetRequiredService<IChannelMembershipReader>(), reminder, ct));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPatch("/workspaces/{workspaceId:guid}/reminders/{reminderId:guid}", async (
            Guid workspaceId,
            Guid reminderId,
            UpdateReminderRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            IAuditWriter audit,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var item = await db.Reminders.FirstOrDefaultAsync(
                x => x.Id == reminderId && x.UserId == profile.Id && x.WorkspaceId == workspace.Id, ct);
            if (item is null)
            {
                return Results.NotFound();
            }

            if (item.Status != ScheduleStatuses.Pending)
            {
                return Results.Conflict(new { error = "ReminderAlreadyClaimed" });
            }

            string? note = item.Note;
            if (request.Note is not null)
            {
                var noteError = ValidateSavedNote(request.Note, out note);
                if (noteError is not null)
                {
                    return Results.BadRequest(noteError);
                }
            }

            var zone = string.IsNullOrWhiteSpace(request.TimeZone) ? item.TimeZone : request.TimeZone;
            var remindAt = item.RemindAtUtc;
            if (request.RemindAtLocal is not null || request.TimeZone is not null)
            {
                var local = request.RemindAtLocal ?? FormatLocal(item.RemindAtUtc, item.TimeZone);
                if (!ScheduleTime.TryToUtc(local, zone, out remindAt, out var timeError))
                {
                    return Results.BadRequest(new { error = timeError });
                }

                var horizonError = ScheduleTime.ValidateHorizon(remindAt, clock.UtcNow);
                if (horizonError is not null)
                {
                    return Results.BadRequest(new { error = horizonError });
                }
            }

            var now = clock.UtcNow;
            var updated = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE messaging.reminders
                SET "Note" = {note},
                    "RemindAtUtc" = {remindAt},
                    "TimeZone" = {zone.Trim()},
                    "UpdatedAt" = {now}
                WHERE "Id" = {item.Id}
                  AND "UserId" = {profile.Id.Value}
                  AND "Status" = 'Pending'
                """, ct);
            if (updated == 0)
            {
                return Results.Conflict(new { error = "ReminderAlreadyClaimed" });
            }

            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.ReminderUpdate,
                EntityType = "Reminder",
                EntityId = item.Id.ToString(),
                MetadataJson = "{}",
                OccurredAt = now
            });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            var fresh = await db.Reminders.AsNoTracking().FirstAsync(x => x.Id == item.Id, ct);
            return Results.Ok(await ToReminderItemAsync(db, http.RequestServices.GetRequiredService<IChannelMembershipReader>(), fresh, ct));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapDelete("/workspaces/{workspaceId:guid}/reminders/{reminderId:guid}", async (
            Guid workspaceId,
            Guid reminderId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            IAuditWriter audit,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var now = clock.UtcNow;
            var updated = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE messaging.reminders
                SET "Status" = 'Cancelled', "UpdatedAt" = {now}
                WHERE "Id" = {reminderId}
                  AND "UserId" = {profile.Id.Value}
                  AND "WorkspaceId" = {workspace.Id.Value}
                  AND "Status" = 'Pending'
                """, ct);
            if (updated == 1)
            {
                audit.Add(new AuditEvent
                {
                    TenantId = workspace.TenantId,
                    ActorUserId = profile.Id,
                    Action = AuditActions.ReminderCancel,
                    EntityType = "Reminder",
                    EntityId = reminderId.ToString(),
                    MetadataJson = "{}",
                    OccurredAt = now
                });
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }

            var existing = await db.Reminders.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == reminderId && x.UserId == profile.Id, ct);
            if (existing is null)
            {
                return Results.NoContent();
            }

            if (existing.Status == ScheduleStatuses.Cancelled)
            {
                return Results.NoContent();
            }

            return Results.Conflict(new { error = "ReminderAlreadyClaimed" });
        }).RequirePermission(Permissions.Message.Read);
    }

    private static async Task<IResult> CancelScheduledAsync(
        VibeChatDbContext db,
        IAuditWriter audit,
        IClock clock,
        TenantId tenantId,
        UserId userId,
        Guid scheduledMessageId,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE messaging.scheduled_messages
            SET "Status" = 'Cancelled', "UpdatedAt" = {now}
            WHERE "Id" = {scheduledMessageId}
              AND "AuthorId" = {userId.Value}
              AND "Status" = 'Pending'
            """, ct);
        if (updated == 1)
        {
            audit.Add(new AuditEvent
            {
                TenantId = tenantId,
                ActorUserId = userId,
                Action = AuditActions.ScheduleCancel,
                EntityType = "ScheduledMessage",
                EntityId = scheduledMessageId.ToString(),
                MetadataJson = "{}",
                OccurredAt = now
            });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        var existing = await db.ScheduledMessages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == scheduledMessageId && x.AuthorId == userId, ct);
        if (existing is null || existing.Status == ScheduleStatuses.Cancelled)
        {
            return Results.NoContent();
        }

        return Results.Conflict(new { error = "ScheduleAlreadyClaimed" });
    }

    private static async Task<ScheduleItemResponse> ToScheduledItemAsync(
        VibeChatDbContext db,
        ScheduledMessage item,
        UserId viewerId,
        CancellationToken ct)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.ChannelId, ct);
        string? channelName = null;
        if (channel is not null)
        {
            channelName = await ResolveSavedChannelDisplayNameAsync(db, channel, viewerId, ct);
        }

        return new ScheduleItemResponse(
            "scheduled_message",
            item.Id,
            item.Status,
            item.SendAtUtc,
            item.TimeZone,
            item.Body,
            null,
            null,
            item.ChannelId.Value,
            channelName,
            item.SentMessageId?.Value,
            item.ThreadId,
            item.SentMessageId?.Value,
            item.FailureCode,
            channel is not null,
            item.CreatedAt);
    }

    private static async Task<ScheduleItemResponse> ToReminderItemAsync(
        VibeChatDbContext db,
        IChannelMembershipReader channels,
        Reminder item,
        CancellationToken ct)
    {
        var canOpen = item.ChannelId is null
            || await channels.CanAccessAsync(item.TenantId, item.ChannelId.Value, item.UserId, ct);
        string? channelName = null;
        if (canOpen && item.ChannelId is ChannelId channelId)
        {
            var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == channelId, ct);
            if (channel is not null)
            {
                channelName = await ResolveSavedChannelDisplayNameAsync(db, channel, item.UserId, ct);
            }
        }

        return new ScheduleItemResponse(
            "reminder",
            item.Id,
            item.Status,
            item.RemindAtUtc,
            item.TimeZone,
            null,
            item.Note,
            item.TargetKind,
            canOpen ? item.ChannelId?.Value : null,
            canOpen ? channelName : null,
            canOpen ? item.MessageId?.Value : null,
            canOpen ? item.ThreadId : null,
            null,
            item.FailureCode,
            canOpen,
            item.CreatedAt);
    }

    private static IResult? ValidateIdempotencyKey(string? value, out string key)
    {
        key = value?.Trim() ?? string.Empty;
        if (key.Length is 0 or > SchedulePolicies.MaxIdempotencyKeyLength)
        {
            return Results.BadRequest(new { error = "InvalidIdempotencyKey" });
        }

        return null;
    }

    private static string FormatLocal(DateTimeOffset utc, string timeZoneId)
    {
        if (!ScheduleTime.TryFindZone(timeZoneId, out var zone))
        {
            zone = TimeZoneInfo.Utc;
        }

        var local = TimeZoneInfo.ConvertTime(utc, zone);
        return local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static bool TryParseScheduleCursor(string? cursor, out DateTimeOffset? createdAt, out Guid? id)
    {
        createdAt = null;
        id = null;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2
                || !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                || !Guid.TryParse(parts[1], out var guid))
            {
                return false;
            }

            createdAt = at;
            id = guid;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string EncodeScheduleCursor(DateTimeOffset createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{createdAt:O}|{id}")));
}
