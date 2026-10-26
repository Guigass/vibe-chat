using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

public sealed class StorageLifecycleService(
    VibeChatDbContext db,
    ITenantContext tenantContext,
    IOptions<StorageLifecycleOptions> options,
    IClock clock,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IObjectStorage storage)
{
    public const string UploadKind = StorageObjectKinds.Attachment;

    private readonly StorageLifecycleOptions _options = options.Value;

    public bool Enabled => _options.Enabled;

    public StorageCommandResult Disabled() => StorageCommandResult.Fail(StorageLifecycleErrors.Disabled);

    public async Task<StorageCommandResult> UsageAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        var rows = await db.StorageLedgers.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.Kind)
            .ToListAsync(cancellationToken);
        var kinds = rows.Select(row => new
        {
            kind = row.Kind,
            reservedBytes = row.ReservedBytes,
            usedBytes = row.UsedBytes,
            previousUsedBytes = row.PreviousUsedBytes,
            softLimit = row.SoftLimit,
            hardLimit = row.HardLimit,
            trend = StorageLifecycleRules.Trend(row.UsedBytes, row.PreviousUsedBytes),
            action = StorageLifecycleRules.Action(row.UsedBytes, row.SoftLimit, row.HardLimit)
        }).ToArray();
        var largest = kinds.Length == 0
            ? null
            : kinds.OrderByDescending(x => x.usedBytes).ThenBy(x => x.kind, StringComparer.Ordinal).First().kind;
        return StorageCommandResult.Ok(new { kinds, largest });
    }

    public async Task<StorageCommandResult> SetLimitsAsync(
        Workspace workspace,
        UserId actorId,
        string? kind,
        long softLimit,
        long hardLimit,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (!StorageObjectKinds.IsKnown(kind))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.KindUnknown);
        }

        if (!StorageLifecycleRules.TryLimits(softLimit, hardLimit))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.InvalidLimit);
        }

        var ledger = await db.StorageLedgers.FirstOrDefaultAsync(
            x => x.WorkspaceId == workspace.Id && x.Kind == kind, cancellationToken);
        if (ledger is not null && ledger.UsedBytes + ledger.ReservedBytes > hardLimit)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.HardLimit);
        }

        if (ledger is null)
        {
            ledger = new StorageLedger
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Kind = kind!
            };
            db.StorageLedgers.Add(ledger);
        }

        ledger.SoftLimit = softLimit;
        ledger.HardLimit = hardLimit;
        audit.Add(Audit(workspace, actorId, AuditActions.StorageLimits, ledger.Id, new { kind, softLimit, hardLimit }));
        await db.SaveChangesAsync(cancellationToken);
        return StorageCommandResult.Ok(new { kind, softLimit, hardLimit });
    }

    public async Task<QuotaDecision> TryReserveUploadAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        Guid attachmentId,
        string storageKey,
        long bytes,
        UserId authorId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        if (!Enabled || bytes <= 0)
        {
            return QuotaDecision.Allow();
        }

        var reserved = await ReserveAsync(
            tenantId,
            workspaceId,
            UploadKind,
            bytes,
            attachmentId,
            storageKey,
            authorId.Value,
            channelId,
            referenced: false,
            clean: false,
            cancellationToken);
        return reserved.Error is null
            ? QuotaDecision.Allow(consumed: reserved.Consumed, used: reserved.Reserved)
            : QuotaDecision.Deny(reserved.Error);
    }

    public async Task<StorageCommandResult> ReserveAsync(
        Workspace workspace,
        string? kind,
        long bytes,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (!StorageObjectKinds.IsKnown(kind) || bytes <= 0)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.KindUnknown);
        }

        if (!await LedgerExistsAsync(workspace.TenantId, workspace.Id, kind!, cancellationToken))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.InvalidLimit);
        }

        var reserved = await ReserveAsync(
            workspace.TenantId,
            workspace.Id,
            kind!,
            bytes,
            attachmentId: null,
            storageKey: string.Empty,
            authorId: Guid.Empty,
            channelId: null,
            referenced: false,
            clean: false,
            cancellationToken);
        return reserved.Error is null
            ? StorageCommandResult.Ok(new
            {
                allowed = true,
                objectId = reserved.ObjectId,
                reservedBytes = reserved.Reserved
            })
            : StorageCommandResult.Fail(reserved.Error);
    }

    public async Task<StorageCommandResult> RegisterAsync(
        Workspace workspace,
        UserId actorId,
        string? kind,
        long bytes,
        bool referenced,
        bool clean,
        string? storageKey,
        Guid? channelId,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (!StorageObjectKinds.IsKnown(kind) || bytes <= 0)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.KindUnknown);
        }

        var key = NormalizeKey(storageKey);
        if (key is null)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.KindUnknown);
        }

        if (!await LedgerExistsAsync(workspace.TenantId, workspace.Id, kind!, cancellationToken))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.InvalidLimit);
        }

        var reserved = await ReserveAsync(
            workspace.TenantId,
            workspace.Id,
            kind!,
            bytes,
            attachmentId: null,
            key,
            actorId.Value,
            channelId,
            referenced,
            clean,
            cancellationToken);
        if (reserved.Error is not null)
        {
            return StorageCommandResult.Fail(reserved.Error);
        }

        return StorageCommandResult.Ok(new { objectId = reserved.ObjectId, kind, bytes, referenced, clean }, 201);
    }

    public async Task CommitUploadAsync(TenantId tenantId, Guid attachmentId, bool clean, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }

        var row = await db.StorageObjects.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.AttachmentId == attachmentId && !x.Referenced, cancellationToken);
        if (row is null)
        {
            return;
        }

        var moved = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE files.storage_ledgers
            SET "ReservedBytes" = "ReservedBytes" - {row.Bytes},
                "UsedBytes" = "UsedBytes" + {row.Bytes}
            WHERE "TenantId" = {tenantId.Value}
              AND "WorkspaceId" = {row.WorkspaceId.Value}
              AND "Kind" = {row.Kind}
              AND "ReservedBytes" >= {row.Bytes}
            """,
            cancellationToken);
        if (moved == 0)
        {
            return;
        }

        row.Referenced = true;
        row.Clean = clean;
        row.PurgeAfter = null;
        row.Version += 1;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseUploadAsync(TenantId tenantId, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }

        var row = await db.StorageObjects.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.AttachmentId == attachmentId && !x.Referenced, cancellationToken);
        if (row is null)
        {
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE files.storage_ledgers
            SET "ReservedBytes" = GREATEST(0, "ReservedBytes" - {row.Bytes})
            WHERE "TenantId" = {tenantId.Value}
              AND "WorkspaceId" = {row.WorkspaceId.Value}
              AND "Kind" = {row.Kind}
            """,
            cancellationToken);
        db.StorageObjects.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StorageCommandResult> ArchiveAsync(Workspace workspace, Guid objectId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        var row = await db.StorageObjects.FirstOrDefaultAsync(
            x => x.WorkspaceId == workspace.Id && x.Id == objectId, cancellationToken);
        if (row is null || row.State == StorageObjectStates.Purged)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.NotFound);
        }

        if (row.State != StorageObjectStates.Hot)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.State);
        }

        row.State = StorageObjectStates.Archived;
        row.Version += 1;
        Changed(workspace.TenantId, row);
        await db.SaveChangesAsync(cancellationToken);
        return StorageCommandResult.Ok(new { objectId = row.Id, state = row.State, version = row.Version });
    }

    public async Task<StorageCommandResult> PurgeAsync(
        Workspace workspace,
        UserId actorId,
        Guid objectId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (!TryKey(idempotencyKey, out var key))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.IdempotencyRequired);
        }

        var replay = await ReplayAsync(workspace, key, "purge", cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var row = await db.StorageObjects.FirstOrDefaultAsync(
            x => x.WorkspaceId == workspace.Id && x.Id == objectId, cancellationToken);
        if (row is null)
        {
            return await RememberAsync(workspace, key, "purge", StorageCommandResult.Fail(StorageLifecycleErrors.NotFound), cancellationToken);
        }

        if (await IsHeldAsync(row, cancellationToken))
        {
            return await RememberAsync(workspace, key, "purge", StorageCommandResult.Fail(StorageLifecycleErrors.Hold), cancellationToken);
        }

        if (row.Referenced)
        {
            return await RememberAsync(workspace, key, "purge", StorageCommandResult.Fail(StorageLifecycleErrors.Referenced), cancellationToken);
        }

        var now = clock.UtcNow;
        if (row.State == StorageObjectStates.Archived)
        {
            row.State = StorageObjectStates.PurgePending;
            row.PurgeAfter = now.AddHours(Math.Max(0, _options.GraceHours));
            row.Version += 1;
            Changed(workspace.TenantId, row);
            await db.SaveChangesAsync(cancellationToken);
            return await RememberAsync(
                workspace,
                key,
                "purge",
                StorageCommandResult.Ok(new { objectId = row.Id, state = row.State, purgeAfter = row.PurgeAfter }),
                cancellationToken);
        }

        if (!StorageLifecycleRules.CanPurge(row.Referenced, held: false, row.State, row.PurgeAfter, now))
        {
            var error = row.State == StorageObjectStates.PurgePending
                ? StorageLifecycleErrors.Grace
                : StorageLifecycleErrors.State;
            return await RememberAsync(workspace, key, "purge", StorageCommandResult.Fail(error), cancellationToken);
        }

        await DeleteObjectAsync(row, now, cancellationToken);
        audit.Add(Audit(workspace, actorId, AuditActions.StoragePurge, row.Id, new { kind = row.Kind, bytes = row.Bytes }));
        await db.SaveChangesAsync(cancellationToken);
        return await RememberAsync(
            workspace,
            key,
            "purge",
            StorageCommandResult.Ok(new { objectId = row.Id, state = StorageObjectStates.Purged }),
            cancellationToken);
    }

    public async Task<StorageCommandResult> CollectAsync(
        Workspace workspace,
        UserId actorId,
        bool dryRun,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (!TryKey(idempotencyKey, out var key))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.IdempotencyRequired);
        }

        var replay = await ReplayAsync(workspace, key, "gc", cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var now = clock.UtcNow;
        var rows = await db.StorageObjects
            .Where(x => x.WorkspaceId == workspace.Id && x.State != StorageObjectStates.Purged)
            .ToListAsync(cancellationToken);
        var candidates = new List<Guid>();
        var deleted = new List<Guid>();
        foreach (var row in rows)
        {
            var held = await IsHeldAsync(row, cancellationToken);
            if (!StorageLifecycleRules.IsGcCandidate(row.Referenced, held, row.State, row.PurgeAfter, now))
            {
                continue;
            }

            candidates.Add(row.Id);
            if (!dryRun)
            {
                await DeleteObjectAsync(row, now, cancellationToken);
                deleted.Add(row.Id);
            }
        }

        if (!dryRun && deleted.Count > 0)
        {
            audit.Add(Audit(workspace, actorId, AuditActions.StorageGc, workspace.Id.Value, new { count = deleted.Count, dryRun = false }));
            await db.SaveChangesAsync(cancellationToken);
        }

        return await RememberAsync(
            workspace,
            key,
            "gc",
            StorageCommandResult.Ok(new { dryRun, candidates, deleted }),
            cancellationToken);
    }

    public async Task<StorageCommandResult> ReconcileAsync(
        Workspace workspace,
        UserId actorId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (!TryKey(idempotencyKey, out var key))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.IdempotencyRequired);
        }

        var replay = await ReplayAsync(workspace, key, "reconcile", cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var objects = await db.StorageObjects.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => new { x.Kind, x.Referenced, x.State, x.Bytes })
            .ToListAsync(cancellationToken);
        var ledgers = await db.StorageLedgers
            .Where(x => x.WorkspaceId == workspace.Id)
            .ToListAsync(cancellationToken);
        var corrected = new List<object>();
        foreach (var ledger in ledgers)
        {
            var measured = StorageLifecycleRules.Measure(objects
                .Where(x => x.Kind == ledger.Kind)
                .Select(x => new StorageMeasure(x.Referenced, x.State, x.Bytes)));
            if (ledger.UsedBytes == measured.Used && ledger.ReservedBytes == measured.Reserved)
            {
                continue;
            }

            ledger.PreviousUsedBytes = ledger.UsedBytes;
            ledger.UsedBytes = measured.Used;
            ledger.ReservedBytes = measured.Reserved;
            corrected.Add(new { kind = ledger.Kind, usedBytes = measured.Used, reservedBytes = measured.Reserved });
        }

        audit.Add(Audit(workspace, actorId, AuditActions.StorageReconcile, workspace.Id.Value, new { count = corrected.Count }));
        await db.SaveChangesAsync(cancellationToken);
        return await RememberAsync(
            workspace,
            key,
            "reconcile",
            StorageCommandResult.Ok(new { corrected }),
            cancellationToken);
    }

    public async Task<StorageCommandResult> RestoreAsync(
        Workspace workspace,
        Guid objectId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (!TryKey(idempotencyKey, out var key))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.IdempotencyRequired);
        }

        var replay = await ReplayAsync(workspace, key, "restore", cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var tombstone = await db.StorageTombstones.AsNoTracking()
            .AnyAsync(x => x.TenantId == workspace.TenantId && x.ObjectId == objectId, cancellationToken);
        if (!StorageLifecycleRules.RestoreBlocked(tombstone))
        {
            return await RememberAsync(workspace, key, "restore", StorageCommandResult.Fail(StorageLifecycleErrors.NotFound), cancellationToken);
        }

        var live = await db.StorageObjects
            .Where(x => x.TenantId == workspace.TenantId && x.Id == objectId)
            .ToListAsync(cancellationToken);
        if (live.Count > 0)
        {
            foreach (var row in live)
            {
                await AdjustLedgerAsync(row, remove: true, cancellationToken);
            }

            db.StorageObjects.RemoveRange(live);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await RememberAsync(
            workspace,
            key,
            "restore",
            StorageCommandResult.Fail(StorageLifecycleErrors.AlreadyPurged),
            cancellationToken);
    }

    public async Task<StorageCommandResult> IssueCdnAsync(
        Workspace workspace,
        Guid objectId,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (!_options.CdnEnabled || !StorageLifecycleRules.CdnKeyUsable(_options.CdnKey))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.CdnDisabled);
        }

        var row = await db.StorageObjects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.Id == objectId, cancellationToken);
        if (row is null)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.NotFound);
        }

        if (!row.Clean || row.State is not (StorageObjectStates.Hot or StorageObjectStates.Archived))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.CdnUnavailable);
        }

        var ttl = Math.Clamp(_options.CdnTtlSeconds, 30, 300);
        var expires = clock.UtcNow.AddSeconds(ttl);
        var token = StorageLifecycleRules.IssueCdnToken(workspace.TenantId.Value, row.Id, expires, _options.CdnKey.Trim());
        var url = $"/api/v1/storage/cdn?token={Uri.EscapeDataString(token)}";
        if (StorageLifecycleRules.EnumeratesKey(url, row.StorageKey))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.CdnUnavailable);
        }

        return StorageCommandResult.Ok(new { url, expiresAt = expires });
    }

    public async Task<StorageCommandResult> ReadCdnAsync(
        string? token,
        UserId userId,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (!_options.CdnEnabled || !StorageLifecycleRules.CdnKeyUsable(_options.CdnKey))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.CdnDisabled);
        }

        var read = StorageLifecycleRules.ReadCdnToken(token, _options.CdnKey.Trim(), clock.UtcNow);
        if (read.Expired)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.CdnExpired);
        }

        if (!read.Ok)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.NotFound);
        }

        var tenantId = new TenantId(read.TenantId);
        tenantContext.SetTenant(tenantId);
        await RlsSession.EnsureAppliedAsync(db, tenantContext, cancellationToken);
        var row = await db.StorageObjects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == read.ObjectId, cancellationToken);
        if (row is null || !row.Clean)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.NotFound);
        }

        var member = await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == row.WorkspaceId && x.UserId == userId, cancellationToken);
        if (!member)
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.NotFound);
        }

        return StorageCommandResult.Ok(new { objectId = row.Id, expiresAt = read.ExpiresAt });
    }

    private async Task<ReserveOutcome> ReserveAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        string kind,
        long bytes,
        Guid? attachmentId,
        string storageKey,
        Guid authorId,
        Guid? channelId,
        bool referenced,
        bool clean,
        CancellationToken cancellationToken)
    {
        if (!await LedgerExistsAsync(tenantId, workspaceId, kind, cancellationToken))
        {
            return ReserveOutcome.Open();
        }

        var started = db.Database.CurrentTransaction is null;
        if (started)
        {
            await db.Database.BeginTransactionAsync(cancellationToken);
        }

        var updated = referenced
            ? await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE files.storage_ledgers
                SET "UsedBytes" = "UsedBytes" + {bytes}
                WHERE "TenantId" = {tenantId.Value}
                  AND "WorkspaceId" = {workspaceId.Value}
                  AND "Kind" = {kind}
                  AND "UsedBytes" + "ReservedBytes" + {bytes} <= "HardLimit"
                """,
                cancellationToken)
            : await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE files.storage_ledgers
                SET "ReservedBytes" = "ReservedBytes" + {bytes}
                WHERE "TenantId" = {tenantId.Value}
                  AND "WorkspaceId" = {workspaceId.Value}
                  AND "Kind" = {kind}
                  AND "UsedBytes" + "ReservedBytes" + {bytes} <= "HardLimit"
                """,
                cancellationToken);
        if (updated == 0)
        {
            if (started)
            {
                await db.Database.RollbackTransactionAsync(cancellationToken);
            }

            return ReserveOutcome.Deny(StorageLifecycleErrors.HardLimit);
        }

        var now = clock.UtcNow;
        var row = new StorageObjectRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Kind = kind,
            State = StorageObjectStates.Hot,
            Bytes = bytes,
            Version = 1,
            Referenced = referenced,
            Clean = clean,
            StorageKey = storageKey,
            AttachmentId = attachmentId,
            ChannelId = channelId,
            AuthorId = authorId,
            CreatedAt = now,
            PurgeAfter = referenced ? null : now.AddHours(Math.Max(0, _options.GraceHours))
        };
        db.StorageObjects.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        if (started)
        {
            await db.Database.CommitTransactionAsync(cancellationToken);
        }
        var counter = await db.StorageLedgers.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.Kind == kind)
            .Select(x => referenced ? x.UsedBytes : x.ReservedBytes)
            .FirstAsync(cancellationToken);
        return ReserveOutcome.Taken(row.Id, counter);
    }

    private async Task DeleteObjectAsync(StorageObjectRecord row, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(row.StorageKey))
        {
            await storage.DeleteObjectAsync(row.StorageKey, cancellationToken);
        }

        await AdjustLedgerAsync(row, remove: true, cancellationToken);
        var exists = await db.StorageTombstones.AnyAsync(
            x => x.TenantId == row.TenantId && x.ObjectId == row.Id, cancellationToken);
        if (!exists)
        {
            db.StorageTombstones.Add(new StorageTombstone
            {
                Id = Guid.NewGuid(),
                TenantId = row.TenantId,
                ObjectId = row.Id,
                PurgedAt = now
            });
        }

        Changed(row.TenantId, row, StorageObjectStates.Purged);
        db.StorageObjects.Remove(row);
    }

    private async Task AdjustLedgerAsync(StorageObjectRecord row, bool remove, CancellationToken cancellationToken)
    {
        var delta = remove ? -row.Bytes : row.Bytes;
        if (row.Referenced)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE files.storage_ledgers
                SET "UsedBytes" = GREATEST(0, "UsedBytes" + {delta})
                WHERE "TenantId" = {row.TenantId.Value}
                  AND "WorkspaceId" = {row.WorkspaceId.Value}
                  AND "Kind" = {row.Kind}
                """,
                cancellationToken);
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE files.storage_ledgers
            SET "ReservedBytes" = GREATEST(0, "ReservedBytes" + {delta})
            WHERE "TenantId" = {row.TenantId.Value}
              AND "WorkspaceId" = {row.WorkspaceId.Value}
              AND "Kind" = {row.Kind}
            """,
            cancellationToken);
    }

    private Task<bool> LedgerExistsAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        string kind,
        CancellationToken cancellationToken) =>
        db.StorageLedgers.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.Kind == kind, cancellationToken);

    private async Task<bool> IsHeldAsync(StorageObjectRecord row, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var holds = await db.LegalHolds.AsNoTracking()
            .Where(x => x.WorkspaceId == row.WorkspaceId && x.Status == LegalHoldStatus.Active)
            .ToListAsync(cancellationToken);
        var subject = new HoldSubject(
            row.WorkspaceId.Value,
            row.ChannelId ?? Guid.Empty,
            row.ChannelId ?? Guid.Empty,
            row.AuthorId,
            row.CreatedAt);
        return holds.Any(hold => LegalHoldRules.IsCovering(
            hold.Status,
            hold.ExpiresAt,
            hold.WorkspaceId.Value,
            hold.ScopeKind,
            hold.ScopeId,
            hold.IntervalStart,
            hold.IntervalEnd,
            subject,
            now));
    }

    private void Changed(TenantId tenantId, StorageObjectRecord row, string? state = null)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Type = StorageLifecycleRules.ChangedEvent,
            Payload = JsonSerializer.Serialize(new
            {
                objectId = row.Id,
                kind = row.Kind,
                state = state ?? row.State,
                version = row.Version
            }),
            OccurredAt = clock.UtcNow
        });
    }

    private async Task<StorageCommandResult?> ReplayAsync(
        Workspace workspace,
        string key,
        string action,
        CancellationToken cancellationToken)
    {
        var job = await db.StorageJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.IdempotencyKey == key, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (!string.Equals(job.Action, action, StringComparison.Ordinal))
        {
            return StorageCommandResult.Fail(StorageLifecycleErrors.IdempotencyConflict);
        }

        var body = JsonSerializer.Deserialize<JsonElement>(job.ResultJson);
        var status = body.TryGetProperty("status", out var code) && code.TryGetInt32(out var parsed) ? parsed : 200;
        var error = body.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String ? err.GetString() : null;
        var payload = body.TryGetProperty("body", out var stored) ? stored : default;
        return error is null
            ? StorageCommandResult.Ok(payload, status)
            : new StorageCommandResult(status, null, error);
    }

    private async Task<StorageCommandResult> RememberAsync(
        Workspace workspace,
        string key,
        string action,
        StorageCommandResult result,
        CancellationToken cancellationToken)
    {
        db.StorageJobs.Add(new StorageLifecycleJob
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            IdempotencyKey = key,
            Action = action,
            ResultJson = JsonSerializer.Serialize(new { status = result.Status, error = result.Error, body = result.Body }),
            CreatedAt = clock.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static bool TryKey(string? value, out string key)
    {
        key = (value ?? string.Empty).Trim();
        return key.Length is > 0 and <= StorageLifecycleLimits.MaxIdempotencyKeyLength;
    }

    private static string? NormalizeKey(string? storageKey)
    {
        var key = (storageKey ?? string.Empty).Trim();
        return key.Length <= StorageLifecycleLimits.MaxStorageKeyLength ? key : null;
    }

    private AuditEvent Audit(Workspace workspace, UserId actorId, string action, Guid entityId, object metadata) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            ActorUserId = actorId,
            Action = action,
            EntityType = "Storage",
            EntityId = entityId.ToString(),
            MetadataJson = JsonSerializer.Serialize(metadata),
            OccurredAt = clock.UtcNow
        };

    private readonly record struct ReserveOutcome(Guid? ObjectId, long Reserved, bool Consumed, string? Error)
    {
        public static ReserveOutcome Open() => new(null, 0, false, null);

        public static ReserveOutcome Deny(string error) => new(null, 0, false, error);

        public static ReserveOutcome Taken(Guid objectId, long reserved) => new(objectId, reserved, true, null);
    }
}
