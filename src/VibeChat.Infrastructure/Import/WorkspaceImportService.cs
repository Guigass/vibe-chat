using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Conversations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

public sealed record ImportCommandResult(int StatusCode, string? Error, ImportJobRecord? Job, bool Idempotent);

public sealed class WorkspaceImportService(
    VibeChatDbContext db,
    IAuditWriter audit,
    IClock clock,
    IObjectStorage storage)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ImportCommandResult> ValidateAsync(
        Workspace workspace,
        UserId actor,
        string adapter,
        string rawDocument,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var documentBytes = Encoding.UTF8.GetBytes(rawDocument);
        if (ImportPolicies.LooksLikeZipBomb(documentBytes.AsSpan(0, Math.Min(documentBytes.Length, 64)), documentBytes.Length))
        {
            return Fail(400, ImportErrors.ZipBomb);
        }

        var parsed = ImportAdapters.Parse(adapter, rawDocument);
        if (!parsed.Ok || parsed.Document is null)
        {
            return Fail(parsed.Error == ImportErrors.SchemaUnknown ? 400 : 422, parsed.Error ?? ImportErrors.DocumentInvalid);
        }

        var inspected = ImportPolicies.Inspect(parsed.Document);
        if (!inspected.Ok || inspected.Document is null)
        {
            var status = inspected.Error == ImportErrors.Quota ? 413 : 422;
            return Fail(status, inspected.Error ?? ImportErrors.DocumentInvalid);
        }

        foreach (var principal in inspected.Document.Principals)
        {
            if (principal.MappedUserId is not Guid mapped)
            {
                continue;
            }

            var known = await db.WorkspaceMembers.AsNoTracking().AnyAsync(
                x => x.WorkspaceId == workspace.Id && x.UserId == new UserId(mapped),
                cancellationToken);
            if (!known)
            {
                return Fail(422, ImportErrors.MappingUnknown);
            }
        }

        var hash = Hash(adapter, rawDocument);
        var existing = await db.ImportJobs.FirstOrDefaultAsync(
            x => x.WorkspaceId == workspace.Id && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.DocumentHash, hash, StringComparison.Ordinal))
            {
                return Fail(409, ImportErrors.IdempotencyConflict);
            }

            return new ImportCommandResult(200, null, existing, true);
        }

        var now = clock.UtcNow;
        var job = new ImportJobRecord
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            CreatedBy = actor,
            Adapter = adapter.Trim().ToLowerInvariant(),
            Status = ImportStatus.Validated,
            IdempotencyKey = idempotencyKey,
            DocumentHash = hash,
            CanonicalJson = JsonSerializer.Serialize(inspected.Document, Json),
            ReportJson = JsonSerializer.Serialize(inspected.Report, Json),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ImportJobs.Add(job);
        Audit(workspace.TenantId, actor, AuditActions.ImportValidate, job, inspected.Report);
        await db.SaveChangesAsync(cancellationToken);
        return new ImportCommandResult(201, null, job, false);
    }

    public async Task<ImportCommandResult> PlanAsync(Workspace workspace, UserId actor, Guid importId, CancellationToken cancellationToken)
    {
        var job = await FindJobAsync(workspace, importId, cancellationToken);
        if (job is null)
        {
            return Fail(404, ImportErrors.NotFound);
        }

        if (!ImportCommands.Allowed(job.Status, ImportCommands.Plan))
        {
            return Fail(409, ImportErrors.State);
        }

        var document = Deserialize(job);
        var report = DeserializeReport(job);
        var channels = await db.Channels.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => new { x.Name, x.Type })
            .ToListAsync(cancellationToken);
        var existing = channels.ToDictionary(
            x => x.Name,
            x => x.Type == ChannelType.Private ? "private" : "public",
            StringComparer.Ordinal);
        ImportPolicies.ApplyNameConflicts(document, existing, report);
        job.ReportJson = JsonSerializer.Serialize(report, Json);
        job.Status = ImportStatus.Planned;
        job.UpdatedAt = clock.UtcNow;
        Audit(workspace.TenantId, actor, AuditActions.ImportPlan, job, report);
        await db.SaveChangesAsync(cancellationToken);
        return new ImportCommandResult(200, null, job, false);
    }

    public async Task<ImportCommandResult> ExecuteAsync(Workspace workspace, UserId actor, Guid importId, CancellationToken cancellationToken)
    {
        var job = await FindJobAsync(workspace, importId, cancellationToken);
        if (job is null)
        {
            return Fail(404, ImportErrors.NotFound);
        }

        if (job.Status == ImportStatus.Staged)
        {
            return new ImportCommandResult(200, null, job, true);
        }

        if (!ImportCommands.Allowed(job.Status, ImportCommands.Execute))
        {
            return Fail(409, ImportErrors.State);
        }

        var report = DeserializeReport(job);
        if (report.Blocking)
        {
            return Fail(409, ImportErrors.Conflict);
        }

        var document = Deserialize(job);
        Stage(job, document);
        job.Status = ImportStatus.Staged;
        job.UpdatedAt = clock.UtcNow;
        Audit(workspace.TenantId, actor, AuditActions.ImportExecute, job, report);
        await db.SaveChangesAsync(cancellationToken);
        return new ImportCommandResult(200, null, job, false);
    }

    public async Task<ImportCommandResult> PauseAsync(Workspace workspace, Guid importId, CancellationToken cancellationToken)
    {
        var job = await FindJobAsync(workspace, importId, cancellationToken);
        if (job is null)
        {
            return Fail(404, ImportErrors.NotFound);
        }

        if (job.Status == ImportStatus.Paused)
        {
            return new ImportCommandResult(200, null, job, true);
        }

        if (!ImportCommands.Allowed(job.Status, ImportCommands.Pause))
        {
            return Fail(409, ImportErrors.State);
        }

        job.PauseFrom = job.Status;
        job.Status = ImportStatus.Paused;
        job.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new ImportCommandResult(200, null, job, false);
    }

    public async Task<ImportCommandResult> ResumeAsync(Workspace workspace, Guid importId, CancellationToken cancellationToken)
    {
        var job = await FindJobAsync(workspace, importId, cancellationToken);
        if (job is null)
        {
            return Fail(404, ImportErrors.NotFound);
        }

        if (!ImportCommands.Allowed(job.Status, ImportCommands.Resume))
        {
            return Fail(409, ImportErrors.State);
        }

        job.Status = job.PauseFrom is ImportStatus.Staged or ImportStatus.Planned ? job.PauseFrom : ImportStatus.Planned;
        job.PauseFrom = null;
        job.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new ImportCommandResult(200, null, job, false);
    }

    public Task<ImportCommandResult> PublishAsync(Workspace workspace, UserId actor, Guid importId, CancellationToken cancellationToken) =>
        new ImportPublisher(db, storage, clock).PublishAsync(workspace, actor, importId, Audit, cancellationToken);

    public Task<ImportCommandResult> RollbackAsync(Workspace workspace, UserId actor, Guid importId, bool confirm, CancellationToken cancellationToken) =>
        new ImportPublisher(db, storage, clock).RollbackAsync(workspace, actor, importId, confirm, Audit, cancellationToken);

    public Task<ImportJobRecord?> FindAsync(Workspace workspace, Guid importId, CancellationToken cancellationToken) =>
        FindJobAsync(workspace, importId, cancellationToken);

    public async Task<IReadOnlyList<ImportJobRecord>> ListAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        return await db.ImportJobs.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
    }

    private async Task<ImportJobRecord?> FindJobAsync(Workspace workspace, Guid importId, CancellationToken cancellationToken) =>
        await db.ImportJobs.FirstOrDefaultAsync(x => x.Id == importId && x.WorkspaceId == workspace.Id, cancellationToken);

    private void Stage(ImportJobRecord job, CanonicalImport document)
    {
        foreach (var principal in document.Principals)
        {
            AddMap(job, ImportResourceTypes.Principal, principal.ExternalId, principal.Historical ? ImportDispositions.Historical : ImportDispositions.Staged);
        }

        foreach (var space in document.Spaces)
        {
            AddMap(job, ImportResourceTypes.Space, space.ExternalId, ImportDispositions.Staged);
        }

        foreach (var channel in document.Channels.Where(x => !x.Ignored))
        {
            AddMap(job, ImportResourceTypes.Channel, channel.ExternalId, ImportDispositions.Staged);
        }

        foreach (var thread in document.Threads)
        {
            AddMap(job, ImportResourceTypes.Thread, thread.ExternalId, ImportDispositions.Staged);
        }

        foreach (var message in document.Messages.Where(x => !x.Ignored))
        {
            AddMap(job, ImportResourceTypes.Message, message.ExternalId, ImportDispositions.Staged);
        }

        foreach (var attachment in document.Attachments)
        {
            var disposition = attachment.Quarantined || string.IsNullOrEmpty(attachment.PayloadBase64)
                ? attachment.Quarantined ? ImportDispositions.Quarantined : ImportDispositions.Ignored
                : ImportDispositions.Staged;
            AddMap(job, ImportResourceTypes.Attachment, attachment.ExternalId, disposition);
        }
    }

    private void AddMap(ImportJobRecord job, string type, string externalId, string disposition)
    {
        db.ImportIdMaps.Add(new ImportIdMapRecord
        {
            Id = Guid.NewGuid(),
            TenantId = job.TenantId,
            ImportJobId = job.Id,
            ResourceType = type,
            ExternalId = externalId,
            Disposition = disposition
        });
    }

    private void Audit(TenantId tenantId, UserId actor, string action, ImportJobRecord job, ImportReport report)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "import",
            EntityId = job.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                report.Principals,
                report.Channels,
                report.Messages,
                report.Quarantined,
                report.Ignored,
                report.Redacted,
                report.Blocking
            }, Json),
            OccurredAt = clock.UtcNow
        });
    }

    public static CanonicalImport Deserialize(ImportJobRecord job) =>
        JsonSerializer.Deserialize<CanonicalImport>(job.CanonicalJson, Json) ?? new CanonicalImport();

    public static ImportReport DeserializeReport(ImportJobRecord job) =>
        JsonSerializer.Deserialize<ImportReport>(job.ReportJson, Json) ?? new ImportReport();

    private static ImportCommandResult Fail(int status, string error) => new(status, error, null, false);

    private static string Hash(string adapter, string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(adapter.Trim().ToLowerInvariant() + "\n" + raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
