using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Files;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

public sealed record ChatBackupOutcome(int Status, string? Error, object? Body, byte[]? File)
{
    public static ChatBackupOutcome Ok(object body) => new(200, null, body, null);

    public static ChatBackupOutcome Zip(byte[] file) => new(200, null, null, file);

    public static ChatBackupOutcome Fail(string error) => new(ChatBackupErrors.Status(error), error, null, null);
}

public sealed class ChatBackupService(
    VibeChatDbContext db,
    ITenantContext tenant,
    IOptions<ChatBackupOptions> options,
    IClock clock,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IConversationSequenceStore sequences,
    IObjectStorage storage,
    IMalwareScanner scanner,
    RuntimeSecretProtector protector,
    ChatBackupTransfer transfer,
    ILogger<ChatBackupService> logger)
{
    private readonly ChatBackupOptions _options = options.Value;

    public bool Enabled => _options.Enabled;

    public async Task<ChatBackupOutcome> ListAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var destinations = await db.ChatBackupDestinations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var jobs = await db.ChatBackupJobs.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var runs = await db.ChatBackupRuns.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderByDescending(x => x.StartedAt)
            .Take(ChatBackupLimits.MaxRunsListed)
            .ToListAsync(cancellationToken);
        var latest = runs.Where(x => x.JobId is not null).GroupBy(x => x.JobId!.Value).ToDictionary(x => x.Key, x => x.First());
        return ChatBackupOutcome.Ok(new
        {
            destinations = destinations.Select(View),
            jobs = jobs.Select(job => View(job, latest.GetValueOrDefault(job.Id))),
            runs = runs.Select(View),
            failed = latest.Values.Any(x => x.Status == ChatBackupRunStatuses.Failed)
        });
    }

    public async Task<ChatBackupOutcome> CreateDestinationAsync(
        Workspace workspace,
        UserId actor,
        string? idempotencyKey,
        string? name,
        string? kind,
        string? host,
        int port,
        string? remotePath,
        string? username,
        string? bucket,
        bool useSsl,
        string? secret,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var keyError = RequireKey(idempotencyKey, out var key);
        if (keyError is not null)
        {
            return ChatBackupOutcome.Fail(keyError);
        }

        var normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        var error = ValidateDestination(normalizedKind, name, host, remotePath, username, bucket, secret);
        if (error is not null)
        {
            return ChatBackupOutcome.Fail(error);
        }

        if (!protector.IsEncryptionAvailable)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.EncryptionUnavailable);
        }

        var hash = ChatBackupRules.RequestHash(name!.Trim(), normalizedKind, host!.Trim(), port.ToString(), ChatBackupRules.NormalizePath(remotePath), username?.Trim() ?? string.Empty, bucket?.Trim() ?? string.Empty, useSsl.ToString());
        var existing = await db.ChatBackupDestinations.FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            return existing.RequestHash == hash ? ChatBackupOutcome.Ok(View(existing)) : ChatBackupOutcome.Fail(ChatBackupErrors.IdempotencyConflict);
        }

        if (await db.ChatBackupDestinations.CountAsync(x => x.WorkspaceId == workspace.Id, cancellationToken) >= ChatBackupLimits.MaxDestinations)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Limit);
        }

        var now = clock.UtcNow;
        var row = new ChatBackupDestination
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            Name = name!.Trim(),
            Kind = normalizedKind,
            Host = host!.Trim(),
            Port = port > 0 ? port : ChatBackupKinds.DefaultPort(normalizedKind),
            RemotePath = ChatBackupRules.NormalizePath(remotePath),
            Username = username?.Trim() ?? string.Empty,
            Bucket = bucket?.Trim() ?? string.Empty,
            UseSsl = useSsl,
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = now,
            UpdatedAt = now
        };
        CopySecret(row, protector.Protect(secret!.Trim(), ChatBackupLimits.CredentialKind, workspace.TenantId, workspace.Id, row.Id.ToString(), now));
        db.ChatBackupDestinations.Add(row);
        audit.Add(Audit(workspace, actor, AuditActions.ChatBackupDestinationCreate, row.Id, new { row.Kind, row.Host }));
        await db.SaveChangesAsync(cancellationToken);
        return ChatBackupOutcome.Ok(View(row));
    }

    public async Task<ChatBackupOutcome> TestAsync(Workspace workspace, Guid destinationId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var row = await FindDestinationAsync(workspace, destinationId, cancellationToken);
        if (row is null)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.NotFound);
        }

        if (ChatBackupRules.IsOpen(row.CircuitOpenUntil, clock.UtcNow))
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.CircuitOpen);
        }

        var secret = Unprotect(row);
        try
        {
            await transfer.TestAsync(row, secret, _options, cancellationToken);
            row.ConsecutiveFailures = 0;
            row.CircuitOpenUntil = null;
            row.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return ChatBackupOutcome.Ok(new { ok = true });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NoteFailure(row);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Chat backup connection test failed for {DestinationId} ({Kind})", row.Id, row.Kind);
            return ChatBackupOutcome.Fail(Map(ex));
        }
    }

    public async Task<ChatBackupOutcome> RotateAsync(Workspace workspace, UserId actor, Guid destinationId, string? idempotencyKey, string? secret, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var keyError = RequireKey(idempotencyKey, out var key);
        if (keyError is not null || string.IsNullOrWhiteSpace(secret))
        {
            return ChatBackupOutcome.Fail(keyError ?? ChatBackupErrors.Invalid);
        }

        var row = await FindDestinationAsync(workspace, destinationId, cancellationToken);
        if (row is null)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.NotFound);
        }

        if (row.IdempotencyKey == key)
        {
            return ChatBackupOutcome.Ok(View(row));
        }

        var now = clock.UtcNow;
        CopySecret(row, protector.Protect(secret.Trim(), ChatBackupLimits.CredentialKind, workspace.TenantId, workspace.Id, row.Id.ToString(), now));
        row.IdempotencyKey = key;
        row.UpdatedAt = now;
        audit.Add(Audit(workspace, actor, AuditActions.ChatBackupDestinationRotate, row.Id, new { row.Kind }));
        await db.SaveChangesAsync(cancellationToken);
        return ChatBackupOutcome.Ok(View(row));
    }

    public async Task<ChatBackupOutcome> DeleteDestinationAsync(Workspace workspace, UserId actor, Guid destinationId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var row = await FindDestinationAsync(workspace, destinationId, cancellationToken);
        if (row is null)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.NotFound);
        }

        if (await db.ChatBackupJobs.AnyAsync(x => x.DestinationId == row.Id, cancellationToken))
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.InUse);
        }

        db.ChatBackupDestinations.Remove(row);
        audit.Add(Audit(workspace, actor, AuditActions.ChatBackupDestinationDelete, row.Id, new { row.Kind }));
        await db.SaveChangesAsync(cancellationToken);
        return ChatBackupOutcome.Ok(new { deleted = true });
    }

    public async Task<ChatBackupOutcome> CreateJobAsync(
        Workspace workspace,
        UserId actor,
        string? idempotencyKey,
        string? name,
        Guid destinationId,
        bool includeAttachments,
        int intervalMinutes,
        string? cron,
        int retentionCount,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var keyError = RequireKey(idempotencyKey, out var key);
        if (keyError is not null)
        {
            return ChatBackupOutcome.Fail(keyError);
        }

        if (ChatBackupRules.ValidateName(name) is not null || !ChatBackupRules.ScheduleValid(intervalMinutes, cron) || !ChatBackupRules.RetentionValid(retentionCount, retentionDays))
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Invalid);
        }

        var destination = await FindDestinationAsync(workspace, destinationId, cancellationToken);
        if (destination is null)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.NotFound);
        }

        var hash = ChatBackupRules.RequestHash(name!.Trim(), destinationId.ToString(), includeAttachments.ToString(), intervalMinutes.ToString(), cron ?? string.Empty, retentionCount.ToString(), retentionDays.ToString());
        var existing = await db.ChatBackupJobs.FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            return existing.RequestHash == hash ? ChatBackupOutcome.Ok(View(existing, null)) : ChatBackupOutcome.Fail(ChatBackupErrors.IdempotencyConflict);
        }

        if (await db.ChatBackupJobs.CountAsync(x => x.WorkspaceId == workspace.Id, cancellationToken) >= ChatBackupLimits.MaxJobs)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Limit);
        }

        var now = clock.UtcNow;
        var row = new ChatBackupJob
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            DestinationId = destination.Id,
            Name = name!.Trim(),
            IncludeAttachments = includeAttachments,
            IntervalMinutes = intervalMinutes,
            Cron = (cron ?? string.Empty).Trim(),
            RetentionCount = retentionCount,
            RetentionDays = retentionDays,
            IdempotencyKey = key,
            RequestHash = hash,
            NextRunAt = ChatBackupRules.NextRun(intervalMinutes, cron, now),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ChatBackupJobs.Add(row);
        audit.Add(Audit(workspace, actor, AuditActions.ChatBackupJobCreate, row.Id, new { row.DestinationId }));
        await db.SaveChangesAsync(cancellationToken);
        return ChatBackupOutcome.Ok(View(row, null));
    }

    public async Task<ChatBackupOutcome> UpdateJobAsync(
        Workspace workspace,
        UserId actor,
        Guid jobId,
        bool? paused,
        string? name,
        Guid? destinationId,
        bool? includeAttachments,
        int? intervalMinutes,
        string? cron,
        int? retentionCount,
        int? retentionDays,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var row = await db.ChatBackupJobs.FirstOrDefaultAsync(x => x.Id == jobId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (row is null)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.NotFound);
        }

        if (name is not null && ChatBackupRules.ValidateName(name) is not null)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Invalid);
        }

        if (destinationId is Guid target && await FindDestinationAsync(workspace, target, cancellationToken) is null)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.NotFound);
        }

        var nextInterval = intervalMinutes ?? row.IntervalMinutes;
        var nextCron = cron ?? row.Cron;
        var nextCount = retentionCount ?? row.RetentionCount;
        var nextDays = retentionDays ?? row.RetentionDays;
        if (!ChatBackupRules.ScheduleValid(nextInterval, nextCron) || !ChatBackupRules.RetentionValid(nextCount, nextDays))
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Invalid);
        }

        row.Name = name?.Trim() ?? row.Name;
        row.DestinationId = destinationId ?? row.DestinationId;
        row.IncludeAttachments = includeAttachments ?? row.IncludeAttachments;
        row.IntervalMinutes = nextInterval;
        row.Cron = nextCron.Trim();
        row.RetentionCount = nextCount;
        row.RetentionDays = nextDays;
        if (paused is bool pause)
        {
            row.Paused = pause;
        }

        row.NextRunAt = row.Paused ? row.NextRunAt : ChatBackupRules.NextRun(row.IntervalMinutes, row.Cron, clock.UtcNow);
        row.UpdatedAt = clock.UtcNow;
        audit.Add(Audit(workspace, actor, AuditActions.ChatBackupJobUpdate, row.Id, new { row.Paused }));
        await db.SaveChangesAsync(cancellationToken);
        return ChatBackupOutcome.Ok(View(row, null));
    }

    public Task<ChatBackupOutcome> RunAsync(Workspace workspace, UserId actor, Guid jobId, string? idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(workspace, actor, jobId, idempotencyKey, scheduled: false, cancellationToken);

    public Task<ChatBackupOutcome> RunScheduledAsync(Guid jobId, string idempotencyKey, CancellationToken cancellationToken)
    {
        return RunScheduledCoreAsync(jobId, idempotencyKey, cancellationToken);
    }

    public async Task<ChatBackupOutcome> ExportAsync(Workspace workspace, UserId actor, bool includeAttachments, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        try
        {
            var bytes = await BuildPackageAsync(workspace, includeAttachments, cancellationToken);
            audit.Add(Audit(workspace, actor, AuditActions.ChatBackupExport, workspace.Id.Value, new { includeAttachments, bytes = bytes.Length }));
            await db.SaveChangesAsync(cancellationToken);
            return ChatBackupOutcome.Zip(bytes);
        }
        catch (InvalidOperationException ex) when (ex.Message is ChatBackupErrors.TooLarge or ChatBackupErrors.UnsafePath)
        {
            return ChatBackupOutcome.Fail(ex.Message);
        }
    }

    public async Task<ChatBackupOutcome> ValidateImportAsync(Workspace workspace, UserId actor, byte[] package, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var read = ChatBackupPackage.Read(package);
        if (!read.Ok)
        {
            return ChatBackupOutcome.Fail(read.Error ?? ChatBackupErrors.PackageInvalid);
        }

        var plan = await PlanImportAsync(workspace, read, cancellationToken);
        audit.Add(Audit(workspace, actor, AuditActions.ChatBackupImport, read.PackageId, new { stage = "validate", plan.Insert, plan.Skip }));
        await db.SaveChangesAsync(cancellationToken);
        return ChatBackupOutcome.Ok(new { ok = true, packageId = read.PackageId, insert = plan.Insert, skip = plan.Skip, attachments = plan.Attachments });
    }

    public async Task<ChatBackupOutcome> ExecuteImportAsync(Workspace workspace, UserId actor, string? idempotencyKey, bool confirm, byte[] package, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        if (!confirm)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.ConfirmationRequired);
        }

        var keyError = RequireKey(idempotencyKey, out var key);
        if (keyError is not null)
        {
            return ChatBackupOutcome.Fail(keyError);
        }

        var prior = await db.ChatBackupRuns.AsNoTracking().FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (prior is not null)
        {
            return ChatBackupOutcome.Ok(new { idempotent = true, runId = prior.Id, status = prior.Status });
        }

        var read = ChatBackupPackage.Read(package);
        if (!read.Ok)
        {
            return ChatBackupOutcome.Fail(read.Error ?? ChatBackupErrors.PackageInvalid);
        }

        var plan = await PlanImportAsync(workspace, read, cancellationToken);
        var now = clock.UtcNow;
        var inserted = 0;
        var known = plan.Existing.ToHashSet();
        foreach (var message in read.Messages.OrderBy(x => x.CreatedAt).ThenBy(x => x.Sequence))
        {
            if (!plan.Accepted.Contains(message.Id) || known.Contains(message.Id))
            {
                continue;
            }

            if (message.Body.Length > ChatBackupLimits.MaxBodyLength)
            {
                continue;
            }

            var conversationId = new ChannelId(message.ConversationId);
            var sequence = await sequences.NextAsync(workspace.TenantId, conversationId, cancellationToken);
            var author = plan.Authors.Contains(message.AuthorId) ? new UserId(message.AuthorId) : actor;
            db.Messages.Add(new Message
            {
                Id = new MessageId(message.Id),
                TenantId = workspace.TenantId,
                ConversationId = conversationId,
                Sequence = sequence,
                AuthorId = author,
                Body = message.BodyRedacted ? string.Empty : message.Body,
                ReplyToMessageId = message.ReplyToMessageId is Guid reply && (known.Contains(reply) || plan.Accepted.Contains(reply))
                    ? new MessageId(reply)
                    : null,
                ThreadId = message.ThreadId,
                CreatedAt = message.CreatedAt,
                EditedAt = message.EditedAt,
                DeletedAt = message.DeletedAt,
                DeletedBy = message.DeletedBy is Guid deletedBy ? new UserId(deletedBy) : null
            });
            outbox.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                Type = nameof(MessageCreatedEvent),
                OccurredAt = now,
                Payload = JsonSerializer.Serialize(new
                {
                    tenantId = workspace.TenantId.Value,
                    channelId = plan.Channels.GetValueOrDefault(message.ConversationId, message.ConversationId),
                    conversationId = message.ConversationId,
                    threadId = message.ThreadId,
                    messageId = message.Id,
                    clientMessageId = message.Id,
                    authorId = author.Value,
                    sequence,
                    body = message.BodyRedacted ? string.Empty : message.Body,
                    createdAt = message.CreatedAt
                })
            });
            known.Add(message.Id);
            inserted++;
        }

        var includeBytes = read.IncludeAttachments && await ContentAuditEnabledAsync(workspace, cancellationToken);
        var stored = 0;
        if (includeBytes)
        {
            stored = await StoreAttachmentsAsync(workspace, actor, read, known, cancellationToken);
        }

        var run = new ChatBackupRun
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            Status = ChatBackupRunStatuses.Success,
            SizeBytes = package.Length,
            ChecksumSha256 = ChatBackupRules.Sha256Hex(package),
            IdempotencyKey = key,
            StartedAt = now,
            FinishedAt = clock.UtcNow
        };
        db.ChatBackupRuns.Add(run);
        audit.Add(Audit(workspace, actor, AuditActions.ChatBackupImport, read.PackageId, new { stage = "execute", inserted, stored }));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            DetachAdded();
            return ChatBackupOutcome.Fail(ChatBackupErrors.CrossTenant);
        }

        return ChatBackupOutcome.Ok(new { idempotent = false, runId = run.Id, inserted, attachments = stored });
    }

    private async Task<ChatBackupOutcome> RunScheduledCoreAsync(Guid jobId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var job = await db.ChatBackupJobs.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.NotFound);
        }

        tenant.SetTenant(job.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);
        var workspace = await db.Workspaces.FirstAsync(x => x.Id == job.WorkspaceId, cancellationToken);
        return await ExecuteAsync(workspace, UserId.Empty, jobId, idempotencyKey, scheduled: true, cancellationToken);
    }

    private async Task<ChatBackupOutcome> ExecuteAsync(Workspace workspace, UserId actor, Guid jobId, string? idempotencyKey, bool scheduled, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.Disabled);
        }

        var keyError = RequireKey(idempotencyKey, out var key);
        if (keyError is not null)
        {
            return ChatBackupOutcome.Fail(keyError);
        }

        var prior = await db.ChatBackupRuns.FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (prior is not null)
        {
            return ChatBackupOutcome.Ok(View(prior));
        }

        var job = await db.ChatBackupJobs.FirstOrDefaultAsync(x => x.Id == jobId && x.WorkspaceId == workspace.Id, cancellationToken);
        var destination = job is null ? null : await FindDestinationAsync(workspace, job.DestinationId, cancellationToken);
        if (job is null || destination is null)
        {
            return ChatBackupOutcome.Fail(ChatBackupErrors.NotFound);
        }

        var now = clock.UtcNow;
        var run = new ChatBackupRun
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            JobId = job.Id,
            DestinationId = destination.Id,
            IdempotencyKey = key,
            StartedAt = now
        };
        var secret = string.Empty;
        try
        {
            if (ChatBackupRules.IsOpen(destination.CircuitOpenUntil, now))
            {
                throw new InvalidOperationException(ChatBackupErrors.CircuitOpen);
            }

            var package = await BuildPackageAsync(workspace, job.IncludeAttachments, cancellationToken);
            run.SizeBytes = package.Length;
            run.ChecksumSha256 = ChatBackupRules.Sha256Hex(package);
            run.RemoteKey = ChatBackupRules.RemoteObjectKey(destination.RemotePath, run.Id);
            secret = Unprotect(destination);
            await transfer.PutAsync(destination, secret, _options, run.RemoteKey, package, cancellationToken);
            run.Status = ChatBackupRunStatuses.Success;
            run.FinishedAt = clock.UtcNow;
            destination.ConsecutiveFailures = 0;
            destination.CircuitOpenUntil = null;
            await RetainAsync(workspace, job, destination, secret, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = ChatBackupRunStatuses.Failed;
            run.ErrorCode = Map(ex);
            run.FinishedAt = clock.UtcNow;
            NoteFailure(destination);
            logger.LogWarning("Chat backup run {RunId} failed for destination {DestinationId} with {Error}", run.Id, destination.Id, run.ErrorCode);
        }

        if (scheduled)
        {
            job.NextRunAt = ChatBackupRules.NextRun(job.IntervalMinutes, job.Cron, clock.UtcNow);
        }

        destination.UpdatedAt = clock.UtcNow;
        job.UpdatedAt = clock.UtcNow;
        db.ChatBackupRuns.Add(run);
        audit.Add(Audit(workspace, actor == UserId.Empty ? null : actor, AuditActions.ChatBackupRun, run.Id, new { run.Status, run.SizeBytes, run.ErrorCode, destination.Kind }));
        VibeChatMetrics.ChatBackupRuns.Add(1);
        await db.SaveChangesAsync(cancellationToken);
        return run.Status == ChatBackupRunStatuses.Success
            ? ChatBackupOutcome.Ok(View(run))
            : new ChatBackupOutcome(200, null, View(run), null);
    }

    private async Task<byte[]> BuildPackageAsync(Workspace workspace, bool includeAttachments, CancellationToken cancellationToken)
    {
        var auditEnabled = await ContentAuditEnabledAsync(workspace, cancellationToken);
        var shipBytes = includeAttachments && auditEnabled;
        var channels = await db.Channels.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { id = x.Id.Value, name = x.Name, type = x.Type.ToString(), spaceId = x.SpaceId, createdAt = x.CreatedAt })
            .ToListAsync(cancellationToken);
        var channelIds = channels.Select(x => new ChannelId(x.id)).ToArray();
        var threads = await db.MessageThreads.AsNoTracking()
            .Where(x => channelIds.Contains(x.ChannelId))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { id = x.Id, channelId = x.ChannelId.Value, parentMessageId = x.ParentMessageId.Value, createdAt = x.CreatedAt })
            .ToListAsync(cancellationToken);
        var threadIds = threads.Select(x => new ChannelId(x.id)).ToArray();
        var messages = await db.Messages.AsNoTracking()
            .Where(x => channelIds.Contains(x.ConversationId) || threadIds.Contains(x.ConversationId))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Sequence)
            .ToListAsync(cancellationToken);
        if (messages.Count > ChatBackupLimits.MaxMessages)
        {
            throw new InvalidOperationException(ChatBackupErrors.TooLarge);
        }

        var messageIds = messages.Select(x => x.Id).ToArray();
        var versions = await db.MessageVersions.AsNoTracking()
            .Where(x => messageIds.Contains(x.MessageId))
            .OrderBy(x => x.MessageId)
            .ThenBy(x => x.VersionNumber)
            .Select(x => new
            {
                messageId = x.MessageId.Value,
                version = x.VersionNumber,
                body = auditEnabled ? x.Body : string.Empty,
                bodyRedacted = !auditEnabled,
                createdAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);
        var messageIdSet = messages.Select(x => x.Id.Value).ToHashSet();
        var attachmentRows = await db.Attachments.AsNoTracking()
            .Where(x => channelIds.Contains(x.ChannelId) && x.MessageId != null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["payload/workspace.json"] = Json(new { id = workspace.Id.Value, name = workspace.Name, slug = workspace.Slug }),
            ["payload/channels.json"] = Json(channels),
            ["payload/threads.json"] = Json(threads),
            [ChatBackupPackage.MessagesEntry] = Json(messages.Select(x => new ChatBackupMessageEntry(
                x.Id.Value,
                x.ConversationId.Value,
                x.Sequence,
                x.AuthorId.Value,
                auditEnabled ? x.Body : string.Empty,
                !auditEnabled,
                x.ReplyToMessageId?.Value,
                x.ThreadId,
                x.CreatedAt,
                x.EditedAt,
                x.DeletedAt,
                x.DeletedBy?.Value))),
            ["payload/message-versions.json"] = Json(versions)
        };
        var attachmentEntries = new List<ChatBackupAttachmentEntry>();
        foreach (var row in attachmentRows.Where(x => x.MessageId is { } id && messageIdSet.Contains(id.Value)))
        {
            var checksum = row.ChecksumSha256 ?? string.Empty;
            if (shipBytes)
            {
                var bytes = await ReadObjectAsync(row.StorageKey, cancellationToken);
                if (bytes is not null && (checksum.Length == 0 || string.Equals(checksum, ChatBackupRules.Sha256Hex(bytes), StringComparison.OrdinalIgnoreCase)))
                {
                    payload[ChatBackupPackage.ObjectName(row.Id)] = bytes;
                    checksum = ChatBackupRules.Sha256Hex(bytes);
                }
            }

            attachmentEntries.Add(new ChatBackupAttachmentEntry(
                row.Id,
                row.MessageId!.Value.Value,
                row.ChannelId.Value,
                row.FileName,
                row.ContentType,
                row.SizeBytes,
                checksum,
                row.CreatedAt));
        }

        payload[ChatBackupPackage.AttachmentsEntry] = Json(attachmentEntries);
        return ChatBackupPackage.Compose(Guid.NewGuid(), workspace.Id.Value, clock.UtcNow, shipBytes, auditEnabled, payload);
    }

    private async Task<ImportPlan> PlanImportAsync(Workspace workspace, ChatBackupPackageRead package, CancellationToken cancellationToken)
    {
        var channelRows = await db.Channels.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => x.Id.Value)
            .ToListAsync(cancellationToken);
        var channelIds = channelRows.Select(x => new ChannelId(x)).ToArray();
        var threads = await db.MessageThreads.AsNoTracking()
            .Where(x => channelIds.Contains(x.ChannelId))
            .Select(x => new { id = x.Id, channelId = x.ChannelId.Value })
            .ToListAsync(cancellationToken);
        var conversations = channelRows.ToHashSet();
        var channelOf = channelRows.ToDictionary(x => x, x => x);
        foreach (var thread in threads)
        {
            conversations.Add(thread.id);
            channelOf[thread.id] = thread.channelId;
        }

        var messageIds = package.Messages.Select(x => new MessageId(x.Id)).ToArray();
        var existing = messageIds.Length == 0
            ? []
            : await db.Messages.AsNoTracking()
                .Where(x => messageIds.Contains(x.Id))
                .Select(x => x.Id.Value)
                .ToListAsync(cancellationToken);
        var authorIds = package.Messages.Select(x => new UserId(x.AuthorId)).Distinct().ToArray();
        var authors = authorIds.Length == 0
            ? []
            : await db.UserProfiles.AsNoTracking()
                .Where(x => authorIds.Contains(x.Id))
                .Select(x => x.Id.Value)
                .ToListAsync(cancellationToken);
        var accepted = package.Messages
            .Where(x => conversations.Contains(x.ConversationId))
            .Select(x => x.Id)
            .ToHashSet();
        var existingSet = existing.ToHashSet();
        return new ImportPlan(
            accepted.Count(id => !existingSet.Contains(id)),
            package.Messages.Count - accepted.Count(id => !existingSet.Contains(id)),
            package.Attachments.Count,
            accepted,
            existingSet,
            authors.ToHashSet(),
            channelOf);
    }

    private async Task<int> StoreAttachmentsAsync(Workspace workspace, UserId actor, ChatBackupPackageRead package, HashSet<Guid> messages, CancellationToken cancellationToken)
    {
        var stored = 0;
        foreach (var attachment in package.Attachments)
        {
            if (!messages.Contains(attachment.MessageId) || !package.Objects.TryGetValue(ChatBackupPackage.ObjectName(attachment.Id), out var bytes))
            {
                continue;
            }

            if (bytes.Length == 0 || bytes.Length > ChatBackupLimits.MaxPackageBytes)
            {
                continue;
            }

            var checksum = ChatBackupRules.Sha256Hex(bytes);
            if (!string.Equals(checksum, attachment.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await db.Attachments.AnyAsync(x => x.Id == attachment.Id, cancellationToken))
            {
                continue;
            }

            await using var stream = new MemoryStream(bytes);
            var scan = await scanner.ScanAsync(stream, attachment.ContentType, cancellationToken);
            var key = $"chat-backup/{workspace.TenantId.Value:N}/{attachment.Id:N}";
            stream.Position = 0;
            await storage.PutObjectAsync(key, stream, attachment.ContentType, cancellationToken);
            var fileName = Path.GetFileName(attachment.FileName);
            db.Attachments.Add(new Attachment
            {
                Id = attachment.Id,
                TenantId = workspace.TenantId,
                ChannelId = new ChannelId(attachment.ChannelId),
                MessageId = new MessageId(attachment.MessageId),
                UploadedBy = actor,
                FileName = string.IsNullOrWhiteSpace(fileName) ? "file" : fileName,
                ContentType = attachment.ContentType,
                SizeBytes = bytes.Length,
                StorageKey = key,
                ChecksumSha256 = checksum,
                Status = scan.Status == AttachmentScanStatus.Clean ? AttachmentStatus.Ready : AttachmentStatus.Failed,
                ScanStatus = scan.Status,
                ScanEngine = scan.Engine,
                ScanSignatureVersion = scan.SignatureVersion,
                ScanReason = scan.Reason,
                CreatedAt = attachment.CreatedAt,
                ReadyAt = scan.Status == AttachmentScanStatus.Clean ? clock.UtcNow : null
            });
            stored++;
        }

        return stored;
    }

    private async Task RetainAsync(Workspace workspace, ChatBackupJob job, ChatBackupDestination destination, string secret, CancellationToken cancellationToken)
    {
        var runs = await db.ChatBackupRuns.AsNoTracking()
            .Where(x => x.JobId == job.Id && x.Status == ChatBackupRunStatuses.Success && x.RemoteKey != string.Empty)
            .Select(x => new { x.Id, x.RemoteKey, Finished = x.FinishedAt ?? x.StartedAt })
            .ToListAsync(cancellationToken);
        var expired = ChatBackupRules.ExpiredRuns(runs.Select(x => (x.Id, x.Finished)).ToArray(), job.RetentionCount, job.RetentionDays, clock.UtcNow);
        foreach (var run in runs.Where(x => expired.Contains(x.Id)))
        {
            try
            {
                await transfer.DeleteAsync(destination, secret, _options, run.RemoteKey, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Chat backup retention skipped {RunId}", run.Id);
            }
        }
    }

    private async Task<byte[]?> ReadObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        var stream = await storage.GetObjectAsync(storageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        await using (stream)
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            return memory.Length > ChatBackupLimits.MaxPackageBytes ? null : memory.ToArray();
        }
    }

    private async Task<bool> ContentAuditEnabledAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var stored = await db.ContentAuditSettings.AsNoTracking()
            .Where(x => x.TenantId == workspace.TenantId)
            .Select(x => (bool?)x.Enabled)
            .FirstOrDefaultAsync(cancellationToken);
        return ContentAuditRules.IsEnabled(stored);
    }

    private async Task<ChatBackupDestination?> FindDestinationAsync(Workspace workspace, Guid id, CancellationToken cancellationToken) =>
        await db.ChatBackupDestinations.FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace.Id, cancellationToken);

    private string Unprotect(ChatBackupDestination row) =>
        protector.Unprotect(ReadSecret(row), ChatBackupLimits.CredentialKind, row.TenantId, row.WorkspaceId, row.Id.ToString());

    private void NoteFailure(ChatBackupDestination row)
    {
        var (failures, openUntil) = ChatBackupRules.RecordFailure(row.ConsecutiveFailures, clock.UtcNow);
        row.ConsecutiveFailures = failures;
        row.CircuitOpenUntil = openUntil;
        row.UpdatedAt = clock.UtcNow;
    }

    private void DetachAdded()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State == EntityState.Added).ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    private AuditEvent Audit(Workspace workspace, UserId? actor, string action, Guid entityId, object metadata) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            ActorUserId = actor is { } value && value != UserId.Empty ? value : null,
            Action = action,
            EntityType = "ChatBackup",
            EntityId = entityId.ToString(),
            MetadataJson = JsonSerializer.Serialize(metadata),
            OccurredAt = clock.UtcNow
        };

    private object View(ChatBackupDestination row) => new
    {
        id = row.Id,
        name = row.Name,
        kind = row.Kind,
        host = row.Host,
        port = row.Port,
        remotePath = row.RemotePath,
        username = row.Username,
        bucket = row.Bucket,
        useSsl = row.UseSsl,
        configured = row.SecretCiphertext is { Length: > 0 },
        secretMask = SecretMasking.MaskFromSuffix(row.SecretMask),
        circuit = ChatBackupRules.Circuit(row.CircuitOpenUntil, clock.UtcNow)
    };

    private static object View(ChatBackupJob row, ChatBackupRun? last) => new
    {
        id = row.Id,
        name = row.Name,
        destinationId = row.DestinationId,
        paused = row.Paused,
        includeAttachments = row.IncludeAttachments,
        intervalMinutes = row.IntervalMinutes,
        cron = row.Cron,
        retentionCount = row.RetentionCount,
        retentionDays = row.RetentionDays,
        nextRunAt = row.NextRunAt,
        lastRun = last is null ? null : View(last)
    };

    private static object View(ChatBackupRun row) => new
    {
        id = row.Id,
        jobId = row.JobId,
        destinationId = row.DestinationId,
        status = row.Status,
        sizeBytes = row.SizeBytes,
        checksumSha256 = row.ChecksumSha256,
        remoteKey = row.RemoteKey,
        error = row.ErrorCode,
        startedAt = row.StartedAt,
        finishedAt = row.FinishedAt
    };

    private string? ValidateDestination(string kind, string? name, string? host, string? remotePath, string? username, string? bucket, string? secret)
    {
        if (!ChatBackupKinds.Known(kind) || ChatBackupRules.ValidateName(name) is not null || ChatBackupRules.ValidateHost(host) is not null || ChatBackupRules.ValidateRemotePath(remotePath) is not null)
        {
            return ChatBackupErrors.Invalid;
        }

        if (kind == ChatBackupKinds.Drive && !_options.DriveEnabled)
        {
            return ChatBackupErrors.DriveDisabled;
        }

        if (kind == ChatBackupKinds.S3 && string.IsNullOrWhiteSpace(bucket))
        {
            return ChatBackupErrors.Invalid;
        }

        if (kind != ChatBackupKinds.Drive && string.IsNullOrWhiteSpace(username))
        {
            return ChatBackupErrors.Invalid;
        }

        return string.IsNullOrWhiteSpace(secret) ? ChatBackupErrors.Invalid : null;
    }

    private static string? RequireKey(string? value, out string key)
    {
        key = (value ?? string.Empty).Trim();
        if (key.Length is 0 or > ChatBackupLimits.MaxIdempotencyKeyLength)
        {
            return ChatBackupErrors.IdempotencyRequired;
        }

        return null;
    }

    private static string Map(Exception exception) =>
        exception.Message is ChatBackupErrors.ConnectionFailed
            or ChatBackupErrors.DestinationFailed
            or ChatBackupErrors.CircuitOpen
            or ChatBackupErrors.DriveDisabled
            or ChatBackupErrors.TooLarge
            or ChatBackupErrors.UnsafePath
            ? exception.Message
            : ChatBackupErrors.DestinationFailed;

    private static void CopySecret(ChatBackupDestination row, EncryptedSecretEnvelope envelope)
    {
        row.SecretCiphertext = envelope.Ciphertext;
        row.SecretNonce = envelope.Nonce;
        row.SecretTag = envelope.Tag;
        row.SecretFormatVersion = envelope.FormatVersion ?? EncryptedSecretEnvelope.CurrentFormatVersion;
        row.SecretKeyVersion = envelope.KeyVersion;
        row.SecretMask = envelope.MaskSuffix ?? string.Empty;
    }

    private static EncryptedSecretEnvelope ReadSecret(ChatBackupDestination row) => new()
    {
        Ciphertext = row.SecretCiphertext,
        Nonce = row.SecretNonce,
        Tag = row.SecretTag,
        KeyVersion = row.SecretKeyVersion,
        FormatVersion = row.SecretFormatVersion,
        MaskSuffix = row.SecretMask
    };

    private static byte[] Json<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, ChatBackupPackage.Json);

    private sealed record ImportPlan(
        int Insert,
        int Skip,
        int Attachments,
        HashSet<Guid> Accepted,
        HashSet<Guid> Existing,
        HashSet<Guid> Authors,
        Dictionary<Guid, Guid> Channels);
}
