using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using VibeChat.Search;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public static class DigestScheduleSync
{
    public static async Task MirrorEnabledAsync(
        VibeChatDbContext db,
        NotificationPreference preference,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = db.DigestSchedules.Local.FirstOrDefault(item => item.UserId == preference.UserId);
        if (row is null)
        {
            row = await db.DigestSchedules.FirstOrDefaultAsync(item => item.UserId == preference.UserId, cancellationToken);
        }

        if (row is null)
        {
            if (!preference.DigestEnabled)
            {
                return;
            }

            db.DigestSchedules.Add(CreateDefault(preference, now));
            return;
        }

        row.Enabled = preference.DigestEnabled;
        if (DigestPolicies.TryTimeZone(preference.TimeZone, out _))
        {
            row.TimeZone = preference.TimeZone!.Trim();
        }

        row.UpdatedAt = now;
    }

    public static DigestSchedule CreateDefault(NotificationPreference preference, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = preference.TenantId,
        UserId = preference.UserId,
        Enabled = preference.DigestEnabled,
        Cadence = DigestCadence.Daily,
        LocalTime = DigestPolicies.DefaultLocalTime,
        Weekday = 1,
        TimeZone = DigestPolicies.TryTimeZone(preference.TimeZone, out _) ? preference.TimeZone!.Trim() : "UTC",
        Delivery = DigestDelivery.InApp,
        IncludeAi = false,
        ChannelIds = [],
        UpdatedAt = now,
    };
}

public static class DigestSelector
{
    public static async Task<IReadOnlyList<DigestSource>> SelectAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        UserId userId,
        bool isWorkspaceMember,
        Guid[] channelFilter,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var channels = await LoadChannelsAsync(db, workspaceId, userId, isWorkspaceMember, cancellationToken);
        if (channelFilter.Length > 0)
        {
            var allowed = channelFilter.ToHashSet();
            channels = channels.Where(channel => allowed.Contains(channel.Id.Value)).ToList();
        }

        if (channels.Count == 0)
        {
            return [];
        }

        var byId = channels.ToDictionary(channel => channel.Id);
        var inbox = await InboxReader.CollectAsync(db, workspaceId, userId, isWorkspaceMember, now, cancellationToken);
        var items = new List<DigestSource>();
        var cited = new HashSet<Guid>();

        foreach (var candidate in inbox.Where(item => item.Kind == InboxItemKind.Announcement))
        {
            if (!byId.ContainsKey(new ChannelId(candidate.ChannelId)) || candidate.MessageId is not Guid messageId || !cited.Add(messageId))
            {
                continue;
            }

            items.Add(new DigestSource(
                DigestItemKind.Announcement, messageId, candidate.ChannelId, messageId, candidate.Sequence, candidate.SortAt));
            if (Count(items, DigestItemKind.Announcement) >= DigestPolicies.MaxPerKind)
            {
                break;
            }
        }

        foreach (var candidate in inbox.Where(item => item.Kind == InboxItemKind.Mention))
        {
            if (!byId.ContainsKey(new ChannelId(candidate.ChannelId)) || candidate.MessageId is not Guid messageId || !cited.Add(messageId))
            {
                continue;
            }

            items.Add(new DigestSource(
                DigestItemKind.Mention, messageId, candidate.ChannelId, messageId, candidate.Sequence, candidate.SortAt));
            if (Count(items, DigestItemKind.Mention) >= DigestPolicies.MaxPerKind)
            {
                break;
            }
        }

        items.AddRange(await UnreadAsync(db, channels, cited, cancellationToken));
        items.AddRange(await CommitmentsAsync(db, workspaceId, userId, byId, cancellationToken));
        return items;
    }

    private static int Count(List<DigestSource> items, string kind) => items.Count(item => item.Kind == kind);

    private static async Task<List<ChannelAccess>> LoadChannelsAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        UserId userId,
        bool isWorkspaceMember,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from channel in db.Channels.AsNoTracking()
            where channel.WorkspaceId == workspaceId
            join member in db.ChannelMembers.AsNoTracking().Where(row => row.UserId == userId && row.LeftAt == null)
                on channel.Id equals member.ChannelId into members
            from member in members.DefaultIfEmpty()
            where ((channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement) && isWorkspaceMember)
                || member != null
            select new
            {
                channel.Id,
                channel.Name,
                JoinedSeq = member != null ? member.JoinedSeq : 0L,
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var cursors = await db.ReadCursors.AsNoTracking()
            .Where(row => row.UserId == userId && ids.Contains(row.ChannelId))
            .ToDictionaryAsync(row => row.ChannelId, row => row.LastReadSequence, cancellationToken);
        var sequences = await db.ConversationSequences.AsNoTracking()
            .Where(row => ids.Contains(row.ConversationId))
            .ToDictionaryAsync(row => row.ConversationId, row => row.LastSequence, cancellationToken);

        return rows.Select(row => new ChannelAccess(
            row.Id,
            row.Name,
            row.JoinedSeq,
            cursors.TryGetValue(row.Id, out var cursor) ? cursor : 0L,
            sequences.TryGetValue(row.Id, out var sequence) ? sequence : 0L)).ToList();
    }

    private static async Task<List<DigestSource>> UnreadAsync(
        VibeChatDbContext db,
        IReadOnlyList<ChannelAccess> channels,
        HashSet<Guid> cited,
        CancellationToken cancellationToken)
    {
        var pending = channels.Where(channel => channel.LastSequence > channel.LastRead && channel.LastSequence > channel.JoinedSeq).ToList();
        if (pending.Count == 0)
        {
            return [];
        }

        var ids = pending.Select(channel => channel.Id).ToArray();
        var rows = await db.Messages.AsNoTracking()
            .Where(message => ids.Contains(message.ConversationId) && message.DeletedAt == null && message.ThreadId == null)
            .OrderByDescending(message => message.Sequence)
            .Select(message => new { message.Id, message.ConversationId, message.Sequence, message.CreatedAt })
            .Take(400)
            .ToListAsync(cancellationToken);

        var items = new List<DigestSource>();
        foreach (var channel in pending)
        {
            var row = rows.FirstOrDefault(message =>
                message.ConversationId == channel.Id
                && message.Sequence > channel.LastRead
                && message.Sequence > channel.JoinedSeq);
            if (row is null || !cited.Add(row.Id.Value))
            {
                continue;
            }

            items.Add(new DigestSource(
                DigestItemKind.Unread, row.Id.Value, channel.Id.Value, row.Id.Value, row.Sequence, row.CreatedAt));
            if (items.Count >= DigestPolicies.MaxPerKind)
            {
                break;
            }
        }

        return items;
    }

    private static async Task<List<DigestSource>> CommitmentsAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        UserId userId,
        IReadOnlyDictionary<ChannelId, ChannelAccess> channels,
        CancellationToken cancellationToken)
    {
        var ids = channels.Keys.ToArray();
        var rows = await db.Commitments.AsNoTracking()
            .Where(row => row.WorkspaceId == workspaceId && row.State == CommitmentState.Open && ids.Contains(row.ChannelId))
            .OrderByDescending(row => row.UpdatedAt)
            .Select(row => new
            {
                row.Id,
                row.Kind,
                row.ChannelId,
                row.SourceMessageId,
                row.UpdatedAt,
                row.OwnerUserId,
                row.CreatedBy,
                row.Visibility,
            })
            .Take(200)
            .ToListAsync(cancellationToken);

        var decisions = new List<DigestSource>();
        var tasks = new List<DigestSource>();
        foreach (var row in rows)
        {
            if (!channels.TryGetValue(row.ChannelId, out _))
            {
                continue;
            }

            var visible = row.Visibility == CommitmentVisibility.Channel
                || row.OwnerUserId == userId
                || row.CreatedBy == userId;
            if (!visible)
            {
                continue;
            }

            var kind = row.Kind == CommitmentKind.Decision ? DigestItemKind.Decision : DigestItemKind.Task;
            var target = kind == DigestItemKind.Decision ? decisions : tasks;
            if (target.Count >= DigestPolicies.MaxPerKind)
            {
                continue;
            }

            target.Add(new DigestSource(
                kind, row.Id, row.ChannelId.Value, row.SourceMessageId.Value, null, row.UpdatedAt));
        }

        decisions.AddRange(tasks);
        return decisions;
    }

    private sealed record ChannelAccess(ChannelId Id, string Name, long JoinedSeq, long LastRead, long LastSequence);
}

public sealed class DigestGenerator(
    VibeChatDbContext db,
    IClock clock,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IEmailSender email,
    IPushSender pushSender,
    PushDispatcher push,
    IAiCompletionProvider ai,
    IOptions<SemanticSearchOptions> semantic)
{
    public async Task<DigestSchedule> GetScheduleAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken)
    {
        var row = await db.DigestSchedules.FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        var preference = await db.NotificationPreferences.FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        preference ??= new NotificationPreference
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
        };
        if (db.Entry(preference).State == EntityState.Detached)
        {
            db.NotificationPreferences.Add(preference);
        }

        row = DigestScheduleSync.CreateDefault(preference, clock.UtcNow);
        row.Enabled = preference.DigestEnabled;
        db.DigestSchedules.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<DigestRun> GenerateAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        UserId userId,
        bool isWorkspaceMember,
        string cadence,
        string periodKey,
        string delivery,
        bool includeAi,
        Guid[] channelFilter,
        bool emitNew,
        CancellationToken cancellationToken)
    {
        var existing = await db.DigestRuns.FirstOrDefaultAsync(
            row => row.UserId == userId && row.WorkspaceId == workspaceId && row.PeriodKey == periodKey,
            cancellationToken);
        if (existing is not null
            && existing.DeliveredAt is not null
            && (existing.AiStatus != DigestAiStatus.Failed || existing.AttemptCount >= DigestPolicies.MaxAttempts))
        {
            return existing;
        }

        var now = clock.UtcNow;
        var sources = existing is null
            ? await DigestSelector.SelectAsync(db, workspaceId, userId, isWorkspaceMember, channelFilter, now, cancellationToken)
            : await StoredSourcesAsync(existing.Id, cancellationToken);
        var preference = await db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var withinDnd = preference is not null && PushDispatchPolicies.IsWithinDnd(
            preference.DndEnabled,
            preference.DndStart,
            preference.DndEnd,
            preference.DndDays,
            preference.TimeZone,
            now);
        var aiOutcome = await ResolveAiAsync(existing, includeAi, tenantId, workspaceId, sources, cancellationToken);
        var deferred = DigestPolicies.ShouldDeferExternal(delivery, sources.Count, withinDnd);
        var draft = DigestPolicies.Draft(sources, aiOutcome, deferred);
        var created = existing is null;
        var run = existing ?? new DigestRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            WorkspaceId = workspaceId,
            PeriodKey = periodKey,
            Cadence = cadence,
            Delivery = delivery,
            CreatedAt = now,
        };
        run.Status = draft.Status;
        run.AiStatus = draft.AiStatus;
        run.Summary = draft.Summary;
        run.Delivery = delivery;
        run.AttemptCount = Math.Min(DigestPolicies.MaxAttempts, run.AttemptCount + 1);
        run.NextRetryAt = deferred ? now.AddMinutes(15) : null;
        if (created)
        {
            db.DigestRuns.Add(run);
            foreach (var source in sources)
            {
                db.DigestItems.Add(new DigestItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    RunId = run.Id,
                    Kind = source.Kind,
                    SourceId = source.SourceId,
                    ChannelId = source.ChannelId,
                    MessageId = source.MessageId,
                    Sequence = source.Sequence,
                    SortAt = source.SortAt,
                });
            }

            if (emitNew)
            {
                WriteAudit(run, sources);
            }
        }

        var transportOn = delivery == DigestDelivery.Email
            ? email.IsEnabled
            : delivery == DigestDelivery.Push && pushSender.IsEnabled;
        var send = DigestPolicies.ShouldSendExternal(delivery, sources.Count, withinDnd, transportOn);
        if (!deferred)
        {
            run.DeliveredAt ??= now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (created)
        {
            db.ChangeTracker.Clear();
            var winner = await db.DigestRuns.FirstAsync(
                row => row.UserId == userId && row.WorkspaceId == workspaceId && row.PeriodKey == periodKey,
                cancellationToken);
            return winner;
        }

        if (!send || run.DeliveryCount > 0)
        {
            return run;
        }

        try
        {
            await SendExternalAsync(run, preference, sources, cancellationToken);
            run.DeliveryCount = 1;
            run.DeliveredAt = now;
            run.NextRetryAt = null;
            if (run.AiStatus != DigestAiStatus.Failed)
            {
                run.Status = DigestRunStatus.Ready;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = DigestRunStatus.Partial;
            run.DeliveredAt = null;
            run.NextRetryAt = now.AddMinutes(15);
            await db.SaveChangesAsync(cancellationToken);
        }

        return run;
    }

    public async Task<IReadOnlyList<DigestVisibleItem>> VisibleItemsAsync(
        DigestRun run,
        UserId userId,
        bool isWorkspaceMember,
        CancellationToken cancellationToken)
    {
        var stored = await db.DigestItems.AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .OrderBy(item => item.Kind)
            .ThenByDescending(item => item.SortAt)
            .ToListAsync(cancellationToken);
        if (stored.Count == 0)
        {
            return [];
        }

        var channels = await AccessibleChannelsAsync(db, run.WorkspaceId, userId, isWorkspaceMember, cancellationToken);
        var names = channels.ToDictionary(channel => channel.Id.Value, channel => channel.Name);
        var joined = channels.ToDictionary(channel => channel.Id.Value, channel => channel.JoinedSeq);
        var messageIds = stored.Where(item => item.MessageId is not null).Select(item => new MessageId(item.MessageId!.Value)).ToArray();
        var liveMessages = messageIds.Length == 0
            ? []
            : await db.Messages.AsNoTracking()
                .Where(message => messageIds.Contains(message.Id) && message.DeletedAt == null)
                .Select(message => new { message.Id, message.ConversationId, message.Sequence })
                .ToListAsync(cancellationToken);
        var liveById = liveMessages.ToDictionary(message => message.Id.Value);
        var commitmentIds = stored
            .Where(item => item.Kind is DigestItemKind.Decision or DigestItemKind.Task)
            .Select(item => item.SourceId)
            .ToArray();
        var liveCommitments = commitmentIds.Length == 0
            ? []
            : await db.Commitments.AsNoTracking()
                .Where(row => commitmentIds.Contains(row.Id))
                .Select(row => row.Id)
                .ToListAsync(cancellationToken);
        var commitmentSet = liveCommitments.ToHashSet();
        var visible = new List<DigestVisibleItem>();
        foreach (var item in stored)
        {
            if (!names.TryGetValue(item.ChannelId, out var name) || !joined.TryGetValue(item.ChannelId, out var joinedSeq))
            {
                continue;
            }

            if (item.MessageId is Guid messageId)
            {
                if (!liveById.TryGetValue(messageId, out var message)
                    || message.ConversationId.Value != item.ChannelId
                    || message.Sequence <= joinedSeq)
                {
                    continue;
                }
            }

            if (item.Kind is DigestItemKind.Decision or DigestItemKind.Task && !commitmentSet.Contains(item.SourceId))
            {
                continue;
            }

            visible.Add(new DigestVisibleItem(item.Kind, item.SourceId, item.ChannelId, name, item.MessageId, item.Sequence));
        }

        return visible;
    }

    private async Task<IReadOnlyList<DigestSource>> StoredSourcesAsync(Guid runId, CancellationToken cancellationToken)
    {
        var rows = await db.DigestItems.AsNoTracking()
            .Where(item => item.RunId == runId)
            .ToListAsync(cancellationToken);
        return rows.Select(item => new DigestSource(item.Kind, item.SourceId, item.ChannelId, item.MessageId, item.Sequence, item.SortAt)).ToArray();
    }

    private async Task<DigestAiOutcome> ResolveAiAsync(
        DigestRun? existing,
        bool includeAi,
        TenantId tenantId,
        WorkspaceId workspaceId,
        IReadOnlyList<DigestSource> sources,
        CancellationToken cancellationToken)
    {
        if (existing?.AiStatus == DigestAiStatus.Ready)
        {
            return DigestAiOutcome.Ready(existing.Summary);
        }

        if (!includeAi || !semantic.Value.Enabled)
        {
            return DigestAiOutcome.Off();
        }

        var optedIn = await db.SemanticSettings.AsNoTracking().AnyAsync(
            row => row.TenantId == tenantId && row.WorkspaceId == workspaceId && row.Enabled,
            cancellationToken);
        if (!optedIn)
        {
            return DigestAiOutcome.Off();
        }

        var counts = Count(sources);
        var prompt = DigestPolicies.AiPrompt(counts.Unread, counts.Mention, counts.Announcement, counts.Decision, counts.Task);
        return await DigestAiGate.RunAsync(
            async token =>
            {
                var response = await ai.CompleteAsync(
                    new AiCompletionRequest(
                        "Summarize digest counts only. Do not invent or repeat message text.",
                        prompt),
                    token);
                return response.Text;
            },
            enabled: true,
            cancellationToken);
    }

    private async Task SendExternalAsync(
        DigestRun run,
        NotificationPreference? preference,
        IReadOnlyList<DigestSource> sources,
        CancellationToken cancellationToken)
    {
        var locale = await db.UserProfiles.AsNoTracking()
            .Where(profile => profile.Id == run.UserId)
            .Select(profile => profile.Locale)
            .FirstOrDefaultAsync(cancellationToken);
        var counts = Count(sources);
        var title = DigestPolicies.ExternalTitle(locale);
        var body = DigestPolicies.ExternalNotice(locale, counts.Unread, counts.Mention, counts.Announcement, counts.Decision, counts.Task);
        if (run.Delivery == DigestDelivery.Email)
        {
            var address = await db.UserProfiles.AsNoTracking()
                .Where(profile => profile.Id == run.UserId)
                .Select(profile => profile.Email)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            await email.SendAsync(new EmailMessage(address, title, body, TenantId: run.TenantId.Value), cancellationToken);
            return;
        }

        if (preference is not null && !preference.PushEnabled)
        {
            return;
        }

        await push.TryDispatchUserNoticeAsync(
            run.TenantId,
            run.UserId,
            title,
            body,
            $"/app/catch-up?digest={run.Id:D}",
            run.Id.ToString("D"),
            cancellationToken);
    }

    private void WriteAudit(DigestRun run, IReadOnlyList<DigestSource> sources)
    {
        var counts = Count(sources);
        var metadata = JsonSerializer.Serialize(new
        {
            digestId = run.Id,
            workspaceId = run.WorkspaceId.Value,
            status = run.Status,
            ai = run.AiStatus,
            delivery = run.Delivery,
            unread = counts.Unread,
            mention = counts.Mention,
            announcement = counts.Announcement,
            decision = counts.Decision,
            task = counts.Task,
        });
        audit.Add(new AuditEvent
        {
            TenantId = run.TenantId,
            ActorUserId = run.UserId,
            Action = AuditActions.DigestGenerated,
            EntityType = "digest",
            EntityId = run.Id.ToString("D"),
            MetadataJson = metadata,
        });
        outbox.Add(new OutboxMessage
        {
            TenantId = run.TenantId,
            Type = DigestEventTypes.Generated,
            Payload = metadata,
        });
        VibeChatMetrics.DigestsGenerated.Add(1);
    }

    internal static async Task<List<(ChannelId Id, string Name, long JoinedSeq)>> AccessibleChannelsAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        UserId userId,
        bool isWorkspaceMember,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from channel in db.Channels.AsNoTracking()
            where channel.WorkspaceId == workspaceId
            join member in db.ChannelMembers.AsNoTracking().Where(row => row.UserId == userId && row.LeftAt == null)
                on channel.Id equals member.ChannelId into members
            from member in members.DefaultIfEmpty()
            where ((channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement) && isWorkspaceMember)
                || member != null
            select new
            {
                channel.Id,
                channel.Name,
                JoinedSeq = member != null ? member.JoinedSeq : 0L,
            }).ToListAsync(cancellationToken);
        return rows.Select(row => (row.Id, row.Name, row.JoinedSeq)).ToList();
    }

    private static DigestCounts Count(IReadOnlyList<DigestSource> sources) => new(
        sources.Count(item => item.Kind == DigestItemKind.Unread),
        sources.Count(item => item.Kind == DigestItemKind.Mention),
        sources.Count(item => item.Kind == DigestItemKind.Announcement),
        sources.Count(item => item.Kind == DigestItemKind.Decision),
        sources.Count(item => item.Kind == DigestItemKind.Task));

    private readonly record struct DigestCounts(int Unread, int Mention, int Announcement, int Decision, int Task);
}

public sealed class DigestDispatchProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<DigestOptions> options,
    ILogger<DigestDispatchProcessor> logger)
{
    public const string JobRole = "digest";

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return 0;
        }

        var claimed = await ClaimAsync(cancellationToken);
        var generated = 0;
        foreach (var row in claimed)
        {
            if (await GenerateDueAsync(row, cancellationToken))
            {
                generated++;
            }
        }

        if (generated > 0)
        {
            logger.LogInformation("Generated {Count} digests", generated);
        }

        return generated;
    }

    private async Task<List<ClaimedDigestRow>> ClaimAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;
        tenant.SetJobRole(JobRole);
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);

        var rows = new List<ClaimedDigestRow>();
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            WITH due AS (
                SELECT "Id"
                FROM notifications.digest_schedules
                WHERE "Enabled"
                  AND ("NextCheckAt" IS NULL OR "NextCheckAt" <= @now)
                ORDER BY "UpdatedAt"
                LIMIT 20
                FOR UPDATE SKIP LOCKED
            )
            UPDATE notifications.digest_schedules AS s
            SET "NextCheckAt" = @lease
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
        leaseParameter.Value = now.AddMinutes(2);
        command.Parameters.Add(leaseParameter);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ClaimedDigestRow(reader.GetGuid(0), reader.GetGuid(1)));
            }
        }

        await RlsSession.CommitAsync(db, cancellationToken);
        return rows;
    }

    private async Task<bool> GenerateDueAsync(ClaimedDigestRow claimed, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeChatDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var generator = scope.ServiceProvider.GetRequiredService<DigestGenerator>();
        tenant.SetTenant(new TenantId(claimed.TenantId));
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);
        var schedule = await db.DigestSchedules.FirstOrDefaultAsync(row => row.Id == claimed.Id, cancellationToken);
        if (schedule is null || !schedule.Enabled)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return false;
        }

        var now = clock.UtcNow;
        tenant.SetUser(schedule.UserId);
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);
        var preference = await db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == schedule.UserId, cancellationToken);
        var withinDnd = preference is not null && PushDispatchPolicies.IsWithinDnd(
            preference.DndEnabled, preference.DndStart, preference.DndEnd, preference.DndDays, preference.TimeZone, now);
        if (!DigestPolicies.IsDue(schedule, now) || withinDnd)
        {
            schedule.NextCheckAt = DigestPolicies.NextCheckUtc(schedule, now, withinDnd);
            schedule.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            await RlsSession.CommitAsync(db, cancellationToken);
            return false;
        }

        if (!DigestPolicies.TryTimeZone(schedule.TimeZone, out var zone))
        {
            schedule.NextCheckAt = now.AddHours(1);
            await db.SaveChangesAsync(cancellationToken);
            await RlsSession.CommitAsync(db, cancellationToken);
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var periodKey = DigestPolicies.PeriodKey(schedule.Cadence, local, null);
        var workspaces = await db.WorkspaceMembers.AsNoTracking()
            .Where(member => member.UserId == schedule.UserId)
            .Select(member => member.WorkspaceId)
            .ToListAsync(cancellationToken);
        foreach (var workspaceId in workspaces)
        {
            await generator.GenerateAsync(
                schedule.TenantId,
                workspaceId,
                schedule.UserId,
                isWorkspaceMember: true,
                schedule.Cadence,
                periodKey,
                schedule.Delivery,
                schedule.IncludeAi,
                schedule.ChannelIds ?? [],
                emitNew: true,
                cancellationToken);
        }

        schedule.NextCheckAt = DigestPolicies.NextCheckUtc(schedule, now, deferredForDnd: false);
        schedule.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await RlsSession.CommitAsync(db, cancellationToken);
        return workspaces.Count > 0;
    }

    private readonly record struct ClaimedDigestRow(Guid Id, Guid TenantId);
}

public sealed class DigestDispatchDispatcher(
    DigestDispatchProcessor processor,
    HaJobGate jobs,
    ILogger<DigestDispatchDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await jobs.WaitTurnAsync(stoppingToken))
            {
                continue;
            }

            try
            {
                await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Digest dispatch batch failed");
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

public static class DigestNoticeFanout
{
    public static async Task DispatchAsync(IServiceProvider services, OutboxMessage outbox, CancellationToken cancellationToken)
    {
        var root = JsonNode.Parse(outbox.Payload)?.AsObject();
        if (root is null || root["userId"]?.GetValue<Guid>() is not Guid userId)
        {
            return;
        }

        var tenantId = outbox.TenantId;
        var hub = services.GetService<IHubContext<ChatHub>>();
        var payload = JsonSerializer.SerializeToNode(new
        {
            digestId = root["digestId"]?.GetValue<Guid>(),
            workspaceId = root["workspaceId"]?.GetValue<Guid>(),
            status = root["status"]?.GetValue<string>(),
            ai = root["ai"]?.GetValue<string>(),
        })!;
        if (hub is not null)
        {
            await hub.Clients.Group(ChatHub.UserGroup(tenantId, new UserId(userId)))
                .SendAsync("DigestGenerated", payload, cancellationToken);
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
            JsonSerializer.Serialize(new RedisRealtimeEnvelope("DigestGenerated", tenantId.Value, Guid.Empty, payload, userId)));
    }
}

internal static class DigestServiceCollectionExtensions
{
    internal static IServiceCollection AddDigestInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DigestOptions>(configuration.GetSection(DigestOptions.SectionName));
        services.AddScoped<DigestGenerator>();
        services.AddSingleton<DigestDispatchProcessor>();
        return services;
    }
}
