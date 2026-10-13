using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

public sealed record DiagnosticsReport(
    string Verdict,
    string Audience,
    DateTimeOffset ObservedAt,
    string CorrelationId,
    IReadOnlyList<DiagnosticCheck> Checks);

public sealed record SupportBundleResult(int StatusCode, string? Error, SupportBundleRecord? Bundle, bool Idempotent);

public sealed record SupportRepairResult(int StatusCode, string? Error, SupportRepairRecord? Job, bool Idempotent);

public sealed record SupportProbeResult(string Code, string Status, string Severity, string Summary, bool Cleaned);

public sealed class SupportDiagnosticsService(
    VibeChatDbContext db,
    IConfiguration configuration,
    IOptions<SupportBundleOptions> options,
    IObjectStorage storage,
    RedisConnection redis,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IClock clock)
{
    public bool BundleEnabled => options.Value.Enabled;

    public async Task<DiagnosticsReport> PreflightAsync(Workspace workspace, Role role, string correlationId, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var facts = await CollectAsync(role == Role.PlatformOwner, cancellationToken);
        started.Stop();
        var observed = clock.UtcNow;
        var reportFacts = facts with { ApiLatencyMs = (int)started.ElapsedMilliseconds };
        var checks = DiagnosticEvaluator.Evaluate(reportFacts, observed, correlationId);
        return new DiagnosticsReport(
            DiagnosticEvaluator.Verdict(checks),
            role == Role.PlatformOwner ? "operator" : "workspace",
            observed,
            SupportRedactor.Correlation(correlationId),
            checks);
    }

    public async Task<SupportProbeResult> ProbeAsync(Workspace workspace, UserId actor, string kind, string correlationId, CancellationToken cancellationToken)
    {
        if (!ProbeKinds.IsAllowed(kind))
        {
            return new SupportProbeResult(SupportErrors.ProbeUnknown, DiagnosticStatus.Fail, DiagnosticSeverity.Error, SupportErrors.ProbeUnknown, true);
        }

        var result = kind switch
        {
            ProbeKinds.Email => await EmailProbeAsync(),
            ProbeKinds.Push => await PushProbeAsync(),
            _ => await StorageProbeAsync(cancellationToken)
        };
        Audit(workspace.TenantId, actor, "support.probe", "support_probe", kind, new { kind, result.Status, result.Cleaned, correlationId = SupportRedactor.Correlation(correlationId) });
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<SupportBundleResult> CreateBundleAsync(
        Workspace workspace,
        UserId actor,
        Role role,
        string idempotencyKey,
        int windowMinutes,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return new SupportBundleResult(404, SupportErrors.Disabled, null, false);
        }

        var hash = SupportRedactor.Sha256(windowMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var existing = await db.SupportBundles.FirstOrDefaultAsync(
            x => x.WorkspaceId == workspace.Id && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing.RequestHash == hash
                ? new SupportBundleResult(200, null, existing, true)
                : new SupportBundleResult(409, SupportErrors.IdempotencyConflict, null, false);
        }

        var report = await PreflightAsync(workspace, role, correlationId, cancellationToken);
        var now = clock.UtcNow;
        var document = new SupportBundleDocument
        {
            GeneratedAt = now,
            ExpiresAt = now.Add(SupportBundleSchema.Ttl),
            WindowMinutes = windowMinutes,
            TenantId = workspace.TenantId.Value,
            WorkspaceId = workspace.Id.Value,
            CorrelationId = report.CorrelationId,
            Audience = report.Audience,
            Verdict = report.Verdict,
            Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production",
            Migration = report.Checks.FirstOrDefault(x => x.Component == "migrations")?.Evidence.Migration ?? "",
            Flags = Flags(),
            Metrics = new SupportBundleMetrics
            {
                OutboxPending = report.Checks.FirstOrDefault(x => x.Code == "outbox.lag")?.Evidence.Pending ?? 0,
                Checks = report.Checks.Count
            },
            Checks = report.Checks.Select(check => new SupportBundleCheck
            {
                Code = check.Code,
                Component = check.Component,
                Status = check.Status,
                Severity = check.Severity,
                Summary = check.Summary,
                Runbook = check.Runbook
            }).ToList()
        };
        var (json, checksum, redacted) = SupportBundleComposer.Compose(document);
        var bundle = new SupportBundleRecord
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            RequestedBy = actor,
            IdempotencyKey = idempotencyKey,
            RequestHash = hash,
            Status = "ready",
            ManifestJson = json,
            Checksum = checksum,
            RedactedFields = redacted,
            ExpiresAt = document.ExpiresAt,
            CreatedAt = now,
            CorrelationId = report.CorrelationId
        };
        db.SupportBundles.Add(bundle);
        Audit(workspace.TenantId, actor, "support.bundle.create", "support_bundle", bundle.Id.ToString(), new { checksum, report.Verdict, redacted });
        outbox.Add(new OutboxMessage
        {
            TenantId = workspace.TenantId,
            Type = "support.bundle.created",
            Payload = JsonSerializer.Serialize(new { bundleId = bundle.Id, workspaceId = workspace.Id.Value, checksum })
        });
        await db.SaveChangesAsync(cancellationToken);
        return new SupportBundleResult(201, null, bundle, false);
    }

    public async Task<SupportBundleResult> DownloadAsync(Workspace workspace, Guid bundleId, CancellationToken cancellationToken)
    {
        var bundle = await db.SupportBundles.FirstOrDefaultAsync(x => x.Id == bundleId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (bundle is null)
        {
            return new SupportBundleResult(404, SupportErrors.NotFound, null, false);
        }

        if (clock.UtcNow > bundle.ExpiresAt || bundle.DownloadCount >= SupportBundleSchema.MaxDownloads)
        {
            return new SupportBundleResult(410, SupportErrors.Expired, null, false);
        }

        bundle.DownloadCount++;
        await db.SaveChangesAsync(cancellationToken);
        return new SupportBundleResult(200, null, bundle, false);
    }

    public async Task<SupportRepairResult> CreateRepairAsync(
        Workspace workspace,
        UserId actor,
        string action,
        bool dryRun,
        bool confirm,
        string? target,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(target) || !RepairActions.IsAllowed(action))
        {
            return new SupportRepairResult(400, SupportErrors.ActionNotAllowed, null, false);
        }

        if (!dryRun && !confirm)
        {
            return new SupportRepairResult(400, SupportErrors.ConfirmRequired, null, false);
        }

        var hash = SupportRedactor.Sha256($"{action}|{dryRun}|{confirm}");
        var existing = await db.SupportRepairs.FirstOrDefaultAsync(
            x => x.WorkspaceId == workspace.Id && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing.RequestHash == hash
                ? new SupportRepairResult(200, null, existing, true)
                : new SupportRepairResult(409, SupportErrors.IdempotencyConflict, null, false);
        }

        var estimated = await EstimateAsync(workspace, action, cancellationToken);
        var now = clock.UtcNow;
        var job = new SupportRepairRecord
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            RequestedBy = actor,
            ActionCode = action,
            DryRun = dryRun,
            Status = dryRun ? RepairStatus.Completed : RepairStatus.Running,
            IdempotencyKey = idempotencyKey,
            RequestHash = hash,
            Estimated = estimated,
            Written = 0,
            CheckpointJson = JsonSerializer.Serialize(new { step = dryRun ? "dry_run" : "planned", estimated, written = 0 }),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.SupportRepairs.Add(job);
        Audit(workspace.TenantId, actor, "support.repair", "support_repair", job.Id.ToString(), new { action, dryRun, estimated, job.Status });
        outbox.Add(new OutboxMessage
        {
            TenantId = workspace.TenantId,
            Type = "support.repair.updated",
            Payload = JsonSerializer.Serialize(new { repairId = job.Id, action, status = job.Status, written = 0 })
        });
        await db.SaveChangesAsync(cancellationToken);
        return new SupportRepairResult(dryRun ? 200 : 202, null, job, false);
    }

    public async Task<SupportRepairResult> ApplyRepairAsync(Workspace workspace, UserId actor, Guid repairId, CancellationToken cancellationToken)
    {
        var job = await db.SupportRepairs.FirstOrDefaultAsync(x => x.Id == repairId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (job is null)
        {
            return new SupportRepairResult(404, SupportErrors.NotFound, null, false);
        }

        if (job.Status == RepairStatus.Completed)
        {
            return new SupportRepairResult(200, null, job, true);
        }

        if (job.Status != RepairStatus.Running || job.DryRun)
        {
            return new SupportRepairResult(409, SupportErrors.State, null, false);
        }

        var written = job.ActionCode == RepairActions.SearchReindex
            ? await ReindexAsync(workspace, cancellationToken)
            : 0;
        job.Written = written;
        job.Status = RepairStatus.Completed;
        job.UpdatedAt = clock.UtcNow;
        job.CheckpointJson = JsonSerializer.Serialize(new { step = "applied", estimated = job.Estimated, written });
        Audit(workspace.TenantId, actor, "support.repair.apply", "support_repair", job.Id.ToString(), new { job.ActionCode, written });
        outbox.Add(new OutboxMessage
        {
            TenantId = workspace.TenantId,
            Type = "support.repair.updated",
            Payload = JsonSerializer.Serialize(new { repairId = job.Id, action = job.ActionCode, status = job.Status, written })
        });
        await db.SaveChangesAsync(cancellationToken);
        return new SupportRepairResult(200, null, job, false);
    }

    public async Task<SupportRepairResult> CancelRepairAsync(Workspace workspace, UserId actor, Guid repairId, CancellationToken cancellationToken)
    {
        var job = await db.SupportRepairs.FirstOrDefaultAsync(x => x.Id == repairId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (job is null)
        {
            return new SupportRepairResult(404, SupportErrors.NotFound, null, false);
        }

        if (job.Status == RepairStatus.Cancelled)
        {
            return new SupportRepairResult(200, null, job, true);
        }

        if (job.Status != RepairStatus.Running)
        {
            return new SupportRepairResult(409, SupportErrors.State, null, false);
        }

        job.Status = RepairStatus.Cancelled;
        job.UpdatedAt = clock.UtcNow;
        job.CheckpointJson = JsonSerializer.Serialize(new { step = "cancelled", estimated = job.Estimated, written = 0 });
        Audit(workspace.TenantId, actor, "support.repair.cancel", "support_repair", job.Id.ToString(), new { job.ActionCode });
        await db.SaveChangesAsync(cancellationToken);
        return new SupportRepairResult(200, null, job, false);
    }

    private async Task<DiagnosticFacts> CollectAsync(bool isOperator, CancellationToken cancellationToken)
    {
        var database = await DatabaseAsync(cancellationToken);
        var redis = await RedisAsync();
        var storageOk = await StorageAsync(cancellationToken);
        var outbox = await OutboxAsync(cancellationToken);
        var migrations = await MigrationsAsync(cancellationToken);
        return new DiagnosticFacts
        {
            DatabaseOk = database.Ok,
            DatabaseLatencyMs = database.LatencyMs,
            RedisConfigured = redis.Configured,
            RedisOk = redis.Ok,
            RedisEndpoint = isOperator ? SupportRedactor.PublicEndpoint(configuration.GetConnectionString("Redis")) : null,
            StorageConfigured = !string.IsNullOrWhiteSpace(configuration["Minio:Endpoint"]),
            StorageOk = storageOk,
            StorageEndpoint = isOperator ? SupportRedactor.PublicEndpoint(configuration["Minio:Endpoint"]) : null,
            OidcConfigured = !string.IsNullOrWhiteSpace(configuration["Authentication:Authority"]),
            OidcEndpoint = isOperator ? SupportRedactor.PublicEndpoint(configuration["Authentication:Authority"]) : null,
            EmailEnabled = configuration.GetValue("Email:Enabled", false),
            PushEnabled = configuration.GetValue("Push:Enabled", false),
            ProxyEnabled = configuration.GetValue("Proxy:Enabled", false),
            OutboxPending = outbox.Pending,
            OutboxStale = outbox.Stale,
            MigrationsCurrent = migrations.Current,
            MigrationId = migrations.Id,
            Operator = isOperator
        };
    }

    private async Task<(bool Ok, int LatencyMs)> DatabaseAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var ok = await db.Database.CanConnectAsync(cancellationToken);
            return (ok, (int)watch.ElapsedMilliseconds);
        }
        catch (Exception)
        {
            return (false, (int)watch.ElapsedMilliseconds);
        }
    }

    private async Task<(bool Configured, bool Ok)> RedisAsync()
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")))
        {
            return (false, false);
        }

        try
        {
            var database = await redis.GetDatabaseAsync();
            if (database is null)
            {
                return (true, false);
            }

            return (true, await database.PingAsync() >= TimeSpan.Zero);
        }
        catch (Exception)
        {
            return (true, false);
        }
    }

    private async Task<bool> StorageAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["Minio:Endpoint"]))
        {
            return false;
        }

        try
        {
            return await storage.IsHealthyAsync(cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<(int Pending, int Stale)> OutboxAsync(CancellationToken cancellationToken)
    {
        try
        {
            var staleBefore = clock.UtcNow.AddMinutes(-5);
            var pending = await db.OutboxMessages.CountAsync(x => x.ProcessedAt == null, cancellationToken);
            var stale = await db.OutboxMessages.CountAsync(x => x.ProcessedAt == null && x.OccurredAt < staleBefore, cancellationToken);
            return (pending, stale);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private async Task<(bool Current, string Id)> MigrationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
            return (!pending.Any(), applied.LastOrDefault() ?? "");
        }
        catch (Exception)
        {
            return (false, "");
        }
    }

    private Dictionary<string, bool> Flags() => new(StringComparer.Ordinal)
    {
        ["ai"] = configuration.GetValue("Ai:Enabled", false),
        ["email"] = configuration.GetValue("Email:Enabled", false),
        ["history"] = configuration.GetValue("Messaging:History:Enabled", false),
        ["import"] = configuration.GetValue("Features:Import:Enabled", false),
        ["push"] = configuration.GetValue("Push:Enabled", false),
        ["supportBundle"] = options.Value.Enabled
    };

    private Task<SupportProbeResult> EmailProbeAsync() =>
        Task.FromResult(configuration.GetValue("Email:Enabled", false)
            ? new SupportProbeResult("email.probe", DiagnosticStatus.Pass, DiagnosticSeverity.Info, "email.synthetic", true)
            : new SupportProbeResult("email.probe", DiagnosticStatus.Skipped, DiagnosticSeverity.Info, "feature.off", true));

    private Task<SupportProbeResult> PushProbeAsync() =>
        Task.FromResult(configuration.GetValue("Push:Enabled", false)
            ? new SupportProbeResult("webpush.probe", DiagnosticStatus.Pass, DiagnosticSeverity.Info, "webpush.synthetic", true)
            : new SupportProbeResult("webpush.probe", DiagnosticStatus.Skipped, DiagnosticSeverity.Info, "feature.off", true));

    private async Task<SupportProbeResult> StorageProbeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["Minio:Endpoint"]))
        {
            return new SupportProbeResult("storage.probe", DiagnosticStatus.Skipped, DiagnosticSeverity.Info, "feature.off", true);
        }

        var key = $"diagnostics/probe-{Guid.NewGuid():N}.txt";
        var cleaned = false;
        try
        {
            await using var stream = new MemoryStream("synthetic"u8.ToArray());
            await storage.PutObjectAsync(key, stream, "text/plain", cancellationToken);
            await storage.DeleteObjectAsync(key, CancellationToken.None);
            cleaned = true;
            return new SupportProbeResult("storage.probe", DiagnosticStatus.Pass, DiagnosticSeverity.Info, "storage.synthetic", true);
        }
        catch (Exception)
        {
            if (!cleaned)
            {
                try
                {
                    await storage.DeleteObjectAsync(key, CancellationToken.None);
                    cleaned = true;
                }
                catch (Exception)
                {
                    cleaned = false;
                }
            }

            return new SupportProbeResult("storage.probe", DiagnosticStatus.Fail, DiagnosticSeverity.Error, "storage.probe_failed", cleaned);
        }
    }

    private async Task<int> EstimateAsync(Workspace workspace, string action, CancellationToken cancellationToken)
    {
        if (action == RepairActions.SearchReindex)
        {
            return await db.Messages.CountAsync(
                message => message.DeletedAt == null && db.Channels.Any(channel => channel.Id == message.ConversationId && channel.WorkspaceId == workspace.Id),
                cancellationToken);
        }

        var members = db.WorkspaceMembers.Where(member => member.WorkspaceId == workspace.Id).Select(member => member.UserId);
        return await db.ChannelMembers.CountAsync(
            member => member.LeftAt == null
                && db.Channels.Any(channel => channel.Id == member.ChannelId && channel.WorkspaceId == workspace.Id)
                && !members.Contains(member.UserId),
            cancellationToken);
    }

    private async Task<int> ReindexAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var config = SearchPolicies.TextConfig;
        var sql =
            $$"""
            UPDATE messaging.messages AS m
            SET search_vector = to_tsvector('{{config}}'::regconfig, coalesce(m."Body", ''))
            WHERE m."TenantId" = {0}
              AND m."DeletedAt" IS NULL
              AND EXISTS (
                SELECT 1 FROM conversations.channels AS c
                WHERE c."Id" = m."ConversationId"
                  AND c."WorkspaceId" = {1}
                  AND c."TenantId" = {0}
              )
            """;
        return await db.Database.ExecuteSqlRawAsync(
            sql,
            new object[] { workspace.TenantId.Value, workspace.Id.Value },
            cancellationToken);
    }

    private void Audit(TenantId tenantId, UserId actor, string action, string entityType, string entityId, object metadata)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            MetadataJson = JsonSerializer.Serialize(metadata),
            OccurredAt = clock.UtcNow
        });
    }
}
