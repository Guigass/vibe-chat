using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

/// <summary>
/// Claims due scheduled messages and personal reminders (B-113).
/// Claim is its own transaction; delivery uses the canonical <see cref="IMessageWriter"/> with a stable idempotency key.
/// </summary>
public sealed class ScheduleDispatchProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduleDispatchProcessor> logger)
{
    public const string JobRole = "schedule";

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var scheduled = await ClaimAsync("scheduled_messages", "SendAtUtc", cancellationToken);
        var reminders = await ClaimAsync("reminders", "RemindAtUtc", cancellationToken);
        var delivered = 0;
        foreach (var row in scheduled)
        {
            if (await DeliverScheduledAsync(row, cancellationToken))
            {
                delivered++;
            }
        }

        foreach (var row in reminders)
        {
            if (await DeliverReminderAsync(row, cancellationToken))
            {
                delivered++;
            }
        }

        if (delivered > 0)
        {
            logger.LogInformation("Delivered {Count} scheduled items", delivered);
        }

        return delivered;
    }

    private async Task<List<ClaimedScheduleRow>> ClaimAsync(
        string table,
        string dueColumn,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;
        var lease = now - SchedulePolicies.ClaimLease;

        tenant.SetJobRole(JobRole);
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

        var rows = new List<ClaimedScheduleRow>();
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            $"""
            WITH due AS (
                SELECT "Id"
                FROM messaging.{table}
                WHERE (
                    "Status" = 'Pending'
                    OR ("Status" = 'Claimed' AND "ClaimedAt" IS NOT NULL AND "ClaimedAt" < @lease)
                )
                AND "{dueColumn}" <= @now
                AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= @now)
                ORDER BY "{dueColumn}"
                LIMIT 20
                FOR UPDATE SKIP LOCKED
            )
            UPDATE messaging.{table} AS s
            SET "Status" = 'Claimed',
                "ClaimedAt" = @now,
                "AttemptCount" = s."AttemptCount" + 1,
                "UpdatedAt" = @now
            FROM due
            WHERE s."Id" = due."Id"
            RETURNING s."Id", s."TenantId"
            """;

        var nowParameter = command.CreateParameter();
        nowParameter.ParameterName = "now";
        nowParameter.Value = now;
        command.Parameters.Add(nowParameter);

        var leaseParameter = command.CreateParameter();
        leaseParameter.ParameterName = "lease";
        leaseParameter.Value = lease;
        command.Parameters.Add(leaseParameter);

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ClaimedScheduleRow(reader.GetGuid(0), reader.GetGuid(1)));
            }
        }

        await RlsSession.CommitAsync(db, cancellationToken);
        return rows;
    }

    private async Task<bool> DeliverScheduledAsync(ClaimedScheduleRow claimed, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var writer = scope.ServiceProvider.GetRequiredService<IMessageWriter>();
        var channels = scope.ServiceProvider.GetRequiredService<IChannelMembershipReader>();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        tenant.SetTenant(new TenantId(claimed.TenantId));
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

        var item = await db.ScheduledMessages.FirstOrDefaultAsync(x => x.Id == claimed.Id, cancellationToken);
        if (item is null || item.Status != ScheduleStatuses.Claimed)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return false;
        }

        tenant.SetUser(item.AuthorId);
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

        try
        {
            var canSend = await channels.CanAccessAsync(item.TenantId, item.ChannelId, item.AuthorId, cancellationToken)
                && await permissions.HasPermissionAsync(item.TenantId, item.AuthorId, Permissions.Message.Send, cancellationToken);
            if (!canSend)
            {
                await MarkScheduledTerminalAsync(
                    db, outbox, audit, item, ScheduleStatuses.MembershipRevoked, "MembershipRevoked", clock.UtcNow, reveal: false, sentMessageId: null, cancellationToken);
                return true;
            }

            var result = await writer.SendAsync(new SendMessageCommand(
                item.TenantId,
                item.AuthorId,
                item.ChannelId,
                item.PlannedMessageId,
                item.SendIdempotencyKey,
                item.Body,
                item.ReplyToMessageId,
                item.ThreadId), cancellationToken);

            item.Status = ScheduleStatuses.Sent;
            item.SentMessageId = result.MessageId;
            item.FailureCode = null;
            item.UpdatedAt = clock.UtcNow;
            AddScheduledDue(outbox, item, "sent", reveal: true, result.MessageId);
            await db.SaveChangesAsync(cancellationToken);
            await RlsSession.CommitAsync(db, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException)
        {
            await MarkScheduledTerminalAsync(
                db, outbox, audit, item, ScheduleStatuses.MembershipRevoked, "MembershipRevoked", clock.UtcNow, reveal: false, sentMessageId: null, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await MarkScheduledTerminalAsync(
                db, outbox, audit, item, ScheduleStatuses.Failed, ex.Message, clock.UtcNow, reveal: true, sentMessageId: null, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scheduled message {ScheduledMessageId} delivery failed", item.Id);
            await RlsSession.RollbackAsync(db, CancellationToken.None);
            db.ChangeTracker.Clear();
            await MarkScheduledRetryAsync(claimed, ex.Message, cancellationToken);
            return false;
        }
    }

    private async Task<bool> DeliverReminderAsync(ClaimedScheduleRow claimed, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var channels = scope.ServiceProvider.GetRequiredService<IChannelMembershipReader>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        tenant.SetTenant(new TenantId(claimed.TenantId));
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

        var item = await db.Reminders.FirstOrDefaultAsync(x => x.Id == claimed.Id, cancellationToken);
        if (item is null || item.Status != ScheduleStatuses.Claimed)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return false;
        }

        try
        {
            var reveal = await CanRevealReminderAsync(channels, item, cancellationToken);
            var now = clock.UtcNow;
            item.Status = ScheduleStatuses.Delivered;
            item.FailureCode = null;
            item.UpdatedAt = now;
            outbox.Add(new OutboxMessage
            {
                TenantId = item.TenantId,
                Type = ScheduleEventTypes.ReminderDue,
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = item.TenantId.Value,
                    userId = item.UserId.Value,
                    reminderId = item.Id,
                    status = "delivered",
                    targetKind = item.TargetKind,
                    note = item.Note,
                    reveal,
                    channelId = reveal ? item.ChannelId?.Value : null,
                    messageId = reveal ? item.MessageId?.Value : null,
                    threadId = reveal ? item.ThreadId : null
                })
            });
            audit.Add(new AuditEvent
            {
                TenantId = item.TenantId,
                ActorUserId = null,
                Action = AuditActions.ReminderDeliver,
                EntityType = "Reminder",
                EntityId = item.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { reveal, actor = "system" }),
                OccurredAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await RlsSession.CommitAsync(db, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reminder {ReminderId} delivery failed", item.Id);
            await RlsSession.RollbackAsync(db, CancellationToken.None);
            db.ChangeTracker.Clear();
            await MarkReminderRetryAsync(claimed, ex.Message, cancellationToken);
            return false;
        }
    }

    private static async Task<bool> CanRevealReminderAsync(
        IChannelMembershipReader channels,
        Reminder item,
        CancellationToken cancellationToken)
    {
        if (item.TargetKind == ReminderTargets.Time || item.ChannelId is null)
        {
            return true;
        }

        return await channels.CanAccessAsync(item.TenantId, item.ChannelId.Value, item.UserId, cancellationToken);
    }

    private static async Task MarkScheduledTerminalAsync(
        VibeChatDbContext db,
        IOutboxWriter outbox,
        IAuditWriter audit,
        ScheduledMessage item,
        string status,
        string failureCode,
        DateTimeOffset now,
        bool reveal,
        MessageId? sentMessageId,
        CancellationToken cancellationToken)
    {
        item.Status = status;
        item.FailureCode = failureCode.Length > 64 ? failureCode[..64] : failureCode;
        item.SentMessageId = sentMessageId;
        item.UpdatedAt = now;
        var reason = status == ScheduleStatuses.MembershipRevoked ? "membership_revoked" : "failed";
        AddScheduledDue(outbox, item, reason, reveal && status != ScheduleStatuses.MembershipRevoked, sentMessageId);
        if (status == ScheduleStatuses.MembershipRevoked)
        {
            audit.Add(new AuditEvent
            {
                TenantId = item.TenantId,
                ActorUserId = null,
                Action = AuditActions.ScheduleRevoke,
                EntityType = "ScheduledMessage",
                EntityId = item.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { actor = "system" }),
                OccurredAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await RlsSession.CommitAsync(db, cancellationToken);
    }

    private static void AddScheduledDue(
        IOutboxWriter outbox,
        ScheduledMessage item,
        string status,
        bool reveal,
        MessageId? messageId)
    {
        outbox.Add(new OutboxMessage
        {
            TenantId = item.TenantId,
            Type = ScheduleEventTypes.ScheduledMessageDue,
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = item.TenantId.Value,
                userId = item.AuthorId.Value,
                scheduledMessageId = item.Id,
                status,
                reveal,
                channelId = reveal ? item.ChannelId.Value : (Guid?)null,
                messageId = reveal ? messageId?.Value : null
            })
        });
    }

    private async Task MarkScheduledRetryAsync(ClaimedScheduleRow claimed, string error, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        tenant.SetTenant(new TenantId(claimed.TenantId));
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);
        var item = await db.ScheduledMessages.FirstOrDefaultAsync(x => x.Id == claimed.Id, cancellationToken);
        if (item is null)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return;
        }

        var now = clock.UtcNow;
        var code = error.Length > 64 ? error[..64] : error;
        if (item.AttemptCount >= SchedulePolicies.MaxAttempts)
        {
            item.Status = ScheduleStatuses.Failed;
            item.FailureCode = code;
            item.UpdatedAt = now;
            AddScheduledDue(outbox, item, "failed", reveal: true, messageId: null);
        }
        else
        {
            item.Status = ScheduleStatuses.Pending;
            item.FailureCode = code;
            item.ClaimedAt = null;
            item.NextAttemptAt = now + SchedulePolicies.RetryDelay(item.AttemptCount);
            item.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await RlsSession.CommitAsync(db, cancellationToken);
    }

    private async Task MarkReminderRetryAsync(ClaimedScheduleRow claimed, string error, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        tenant.SetTenant(new TenantId(claimed.TenantId));
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);
        var item = await db.Reminders.FirstOrDefaultAsync(x => x.Id == claimed.Id, cancellationToken);
        if (item is null)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return;
        }

        var now = clock.UtcNow;
        var code = error.Length > 64 ? error[..64] : error;
        if (item.AttemptCount >= SchedulePolicies.MaxAttempts)
        {
            item.Status = ScheduleStatuses.Failed;
            item.FailureCode = code;
        }
        else
        {
            item.Status = ScheduleStatuses.Pending;
            item.FailureCode = code;
            item.ClaimedAt = null;
            item.NextAttemptAt = now + SchedulePolicies.RetryDelay(item.AttemptCount);
        }

        item.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await RlsSession.CommitAsync(db, cancellationToken);
    }
}

public readonly record struct ClaimedScheduleRow(Guid Id, Guid TenantId);

public sealed class ScheduleDispatchDispatcher(
    ScheduleDispatchProcessor processor,
    ILogger<ScheduleDispatchDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Schedule dispatch batch failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>In-app (user group) and Web Push for schedule/reminder due events. Never fans out to a channel.</summary>
public static class ScheduleNoticeFanout
{
    public static async Task DispatchAsync(IServiceProvider services, OutboxMessage outbox, CancellationToken cancellationToken)
    {
        var root = JsonNode.Parse(outbox.Payload)?.AsObject()
            ?? throw new InvalidOperationException("Invalid schedule notice payload");
        var tenantId = new TenantId(root["tenantId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Schedule notice missing tenantId"));
        var userId = new UserId(root["userId"]?.GetValue<Guid>()
            ?? throw new InvalidOperationException("Schedule notice missing userId"));
        var reveal = root["reveal"]?.GetValue<bool>() ?? false;
        var status = root["status"]?.GetValue<string>() ?? string.Empty;

        var db = services.GetRequiredService<VibeChatDbContext>();
        var locale = await db.UserProfiles.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => x.Locale)
            .FirstOrDefaultAsync(cancellationToken);

        string title;
        string body;
        string tag;
        string url;
        if (outbox.Type == ScheduleEventTypes.ReminderDue)
        {
            var note = root["note"]?.GetValue<string>();
            var pref = await LoadPreferenceAsync(db, userId, cancellationToken);
            (title, body) = ScheduleNoticeCopy.Reminder(locale, note, pref?.HidePreview == true);
            tag = root["reminderId"]?.GetValue<Guid>().ToString("D") ?? outbox.Id.ToString("D");
            url = reveal && root["channelId"]?.GetValue<Guid>() is Guid channelId
                ? $"/app?channel={channelId:D}"
                : "/app";
            await PublishUserAsync(services, "ReminderDue", tenantId, userId, root, cancellationToken);
            var now = services.GetRequiredService<IClock>().UtcNow;
            if (pref is not null && !AllowsPush(pref, now))
            {
                return;
            }

            await services.GetRequiredService<PushDispatcher>().TryDispatchUserNoticeAsync(
                tenantId, userId, title, body, url, tag, cancellationToken);
            return;
        }

        var copy = status == "membership_revoked"
            ? ScheduleNoticeCopy.ScheduledRevoked(locale)
            : ScheduleNoticeCopy.ScheduledFailed(locale);
        title = copy.Title;
        body = copy.Body;
        tag = root["scheduledMessageId"]?.GetValue<Guid>().ToString("D") ?? outbox.Id.ToString("D");
        url = "/app";
        await PublishUserAsync(services, "ScheduledMessageDue", tenantId, userId, root, cancellationToken);
        if (status == "sent")
        {
            return;
        }

        var schedulePref = await LoadPreferenceAsync(db, userId, cancellationToken);
        if (schedulePref is not null && !AllowsPush(schedulePref, services.GetRequiredService<IClock>().UtcNow))
        {
            return;
        }

        if (schedulePref?.HidePreview == true)
        {
            body = string.Empty;
        }

        await services.GetRequiredService<PushDispatcher>().TryDispatchUserNoticeAsync(
            tenantId, userId, title, body, url, tag, cancellationToken);
    }

    private static async Task<NotificationPreference?> LoadPreferenceAsync(
        VibeChatDbContext db,
        UserId userId,
        CancellationToken cancellationToken) =>
        await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    private static bool AllowsPush(NotificationPreference pref, DateTimeOffset nowUtc)
    {
        if (!PushDispatchPolicies.IsPushEnabled(pref.PushEnabled))
        {
            return false;
        }

        return !PushDispatchPolicies.IsWithinDnd(
            pref.DndEnabled,
            pref.DndStart,
            pref.DndEnd,
            pref.DndDays,
            pref.TimeZone,
            nowUtc);
    }

    private static async Task PublishUserAsync(
        IServiceProvider services,
        string eventName,
        TenantId tenantId,
        UserId userId,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var hub = services.GetService<IHubContext<ChatHub>>();
        if (hub is not null)
        {
            await hub.Clients.Group(ChatHub.UserGroup(tenantId, userId))
                .SendAsync(eventName, payload, cancellationToken);
            return;
        }

        var redis = services.GetRequiredService<RedisConnection>();
        var subscriber = await redis.GetSubscriberAsync();
        if (subscriber is null)
        {
            return;
        }

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannelChatPublisher.ChannelName),
            JsonSerializer.Serialize(new RedisRealtimeEnvelope(eventName, tenantId.Value, Guid.Empty, payload, userId.Value)));
    }
}
