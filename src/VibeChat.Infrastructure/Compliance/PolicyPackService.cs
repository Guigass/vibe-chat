using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.Files;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure.Compliance;

public sealed class PolicyPackService(
    VibeChatDbContext db,
    IClock clock,
    IOptions<PolicyPackOptions> options,
    IAuditWriter audit,
    FilesSettingsResolver files,
    IRuntimeSettingsCacheInvalidator cache)
{
    private static readonly Meter Meter = new("VibeChat.PolicyPack", "1.0.0");
    private static readonly Counter<long> Applies = Meter.CreateCounter<long>("vibechat.policy_pack.applies");

    public bool Enabled => options.Value.Enabled;

    public PolicyPackCommandResult Catalog()
    {
        if (!Enabled)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.Disabled);
        }

        return PolicyPackCommandResult.Ok(new
        {
            schema = PolicyPackSchema.Name,
            baseline = PolicyPackSchema.Baseline,
            certification = false,
            packs = PolicyPackRules.Catalog().Select(pack => new { id = pack.Id, version = pack.Version, label = pack.Label })
        });
    }

    public async Task<PolicyPackCommandResult> CurrentAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.Disabled);
        }

        var row = await FindAsync(workspace, cancellationToken);
        return row is null
            ? PolicyPackCommandResult.Ok(new { assigned = false, certification = false, baseline = PolicyPackSchema.Baseline })
            : PolicyPackCommandResult.Ok(Parse(row.EffectiveJson));
    }

    public PolicyPackCommandResult Preview(string? packId, int version, JsonElement overrides)
    {
        if (!Enabled)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.Disabled);
        }

        if (!PolicyPackRules.TryResolve(packId, version, overrides, CeilingMb(), out var canonical, out _, out var error))
        {
            return PolicyPackCommandResult.Fail(error);
        }

        return PolicyPackCommandResult.Ok(Parse(canonical));
    }

    public Task<PolicyPackCommandResult> ApplyAsync(
        Workspace workspace,
        UserId actor,
        string? packId,
        int version,
        JsonElement overrides,
        bool confirm,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ApplyResolvedAsync(workspace, actor, packId, version, overrides, confirm, idempotencyKey, AuditActions.PolicyPackApply, cancellationToken);

    public async Task<PolicyPackCommandResult> ImportAsync(
        Workspace workspace,
        UserId actor,
        JsonElement document,
        bool confirm,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.Disabled);
        }

        if (!PolicyPackRules.TryReadImport(document, out var packId, out var version, out var overrides, out var error))
        {
            return PolicyPackCommandResult.Fail(error);
        }

        return await ApplyResolvedAsync(workspace, actor, packId, version, overrides, confirm, idempotencyKey, AuditActions.PolicyPackImport, cancellationToken);
    }

    public async Task<PolicyPackCommandResult> RollbackAsync(
        Workspace workspace,
        UserId actor,
        bool confirm,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.Disabled);
        }

        if (!confirm)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.ConfirmationRequired);
        }

        var row = await FindTrackedAsync(workspace, cancellationToken);
        if (row?.PreviousPackId is null || row.PreviousPackVersion is null || row.PreviousOverridesJson is null || row.PreviousEffectiveJson is null)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.RollbackUnsafe);
        }

        var hash = PolicyPackRules.RollbackHash(row.PreviousPackId, row.PreviousPackVersion.Value, row.PreviousOverridesJson);
        if (row.IdempotencyKey == idempotencyKey)
        {
            return row.RequestHash == hash
                ? PolicyPackCommandResult.Ok(Parse(row.EffectiveJson))
                : PolicyPackCommandResult.Fail(PolicyPackErrors.IdempotencyConflict);
        }

        using var previousOverrides = JsonDocument.Parse(row.PreviousOverridesJson);
        if (!PolicyPackRules.TryResolve(
                row.PreviousPackId,
                row.PreviousPackVersion.Value,
                previousOverrides.RootElement,
                CeilingMb(),
                out var canonical,
                out var overridesCanonical,
                out _))
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.RollbackUnsafe);
        }

        var before = row.EffectiveJson;
        var previousPackId = row.PreviousPackId!;
        var previousVersion = row.PreviousPackVersion.Value;
        row.PreviousPackId = row.PackId;
        row.PreviousPackVersion = row.PackVersion;
        row.PreviousOverridesJson = row.OverridesJson;
        row.PreviousEffectiveJson = before;
        row.PackId = previousPackId;
        row.PackVersion = previousVersion;
        row.OverridesJson = overridesCanonical;
        row.EffectiveJson = canonical;
        row.IdempotencyKey = idempotencyKey;
        row.RequestHash = hash;
        row.AppliedBy = actor;
        row.AppliedAt = clock.UtcNow;
        await WriteThroughAsync(workspace, canonical, cancellationToken);
        WriteAudit(workspace, actor, AuditActions.PolicyPackRollback, before, canonical);
        await db.SaveChangesAsync(cancellationToken);
        Invalidate(workspace);
        Applies.Add(1);
        return PolicyPackCommandResult.Ok(Parse(canonical));
    }

    public async Task<PolicyPackCommandResult> ExportAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.Disabled);
        }

        var row = await FindAsync(workspace, cancellationToken);
        if (row is null)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.NotFound);
        }

        var node = new JsonObject
        {
            ["schema"] = PolicyPackSchema.Name,
            ["packId"] = row.PackId,
            ["version"] = row.PackVersion,
            ["overrides"] = JsonNode.Parse(row.OverridesJson),
            ["effective"] = JsonNode.Parse(row.EffectiveJson),
            ["certification"] = false
        };
        return PolicyPackCommandResult.Ok(Parse(PolicyPackRules.Redact(node)));
    }

    public async Task<PolicyPackCommandResult> DriftAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.Disabled);
        }

        var row = await FindAsync(workspace, cancellationToken);
        if (row is null)
        {
            return PolicyPackCommandResult.Ok(new
            {
                assigned = false,
                drifted = false,
                upgradeAvailable = false,
                certification = false,
                controls = Array.Empty<object>()
            });
        }

        var live = await LiveAsync(workspace, cancellationToken);
        var items = PolicyPackRules.Compare(row.EffectiveJson, live);
        var catalogVersion = PolicyPackRules.Catalog().First(x => x.Id == row.PackId).Version;
        return PolicyPackCommandResult.Ok(new
        {
            assigned = true,
            packId = row.PackId,
            appliedVersion = row.PackVersion,
            catalogVersion,
            upgradeAvailable = PolicyPackRules.UpgradeAvailable(row.PackVersion, catalogVersion),
            drifted = items.Any(x => x.Drifted),
            certification = false,
            controls = items.Select(x => new
            {
                key = x.Key,
                expected = x.Expected,
                actual = x.Actual,
                enforced = x.Enforced,
                drifted = x.Drifted
            })
        });
    }

    public async Task<bool> BlocksGuestsAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return false;
        }

        var json = await db.PolicyPackAssignments.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => x.EffectiveJson)
            .FirstOrDefaultAsync(cancellationToken);
        return json is not null && PolicyPackRules.BlocksGuests(json);
    }

    private async Task<PolicyPackCommandResult> ApplyResolvedAsync(
        Workspace workspace,
        UserId actor,
        string? packId,
        int version,
        JsonElement overrides,
        bool confirm,
        string idempotencyKey,
        string action,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.Disabled);
        }

        if (!confirm)
        {
            return PolicyPackCommandResult.Fail(PolicyPackErrors.ConfirmationRequired);
        }

        if (!PolicyPackRules.TryResolve(packId, version, overrides, CeilingMb(), out var canonical, out var overridesCanonical, out var error))
        {
            return PolicyPackCommandResult.Fail(error);
        }

        var hash = PolicyPackRules.RequestHash(packId!, version, overridesCanonical);
        var row = await FindTrackedAsync(workspace, cancellationToken);
        if (row is not null && row.IdempotencyKey == idempotencyKey)
        {
            return row.RequestHash == hash
                ? PolicyPackCommandResult.Ok(Parse(row.EffectiveJson))
                : PolicyPackCommandResult.Fail(PolicyPackErrors.IdempotencyConflict);
        }

        var before = row?.EffectiveJson ?? "{}";
        if (row is null)
        {
            row = new PolicyPackAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id
            };
            db.PolicyPackAssignments.Add(row);
        }
        else
        {
            row.PreviousPackId = row.PackId;
            row.PreviousPackVersion = row.PackVersion;
            row.PreviousOverridesJson = row.OverridesJson;
            row.PreviousEffectiveJson = row.EffectiveJson;
        }

        row.PackId = packId!;
        row.PackVersion = version;
        row.OverridesJson = overridesCanonical;
        row.EffectiveJson = canonical;
        row.IdempotencyKey = idempotencyKey;
        row.RequestHash = hash;
        row.AppliedBy = actor;
        row.AppliedAt = clock.UtcNow;
        await WriteThroughAsync(workspace, canonical, cancellationToken);
        WriteAudit(workspace, actor, action, before, canonical);
        await db.SaveChangesAsync(cancellationToken);
        Invalidate(workspace);
        Applies.Add(1);
        return PolicyPackCommandResult.Ok(Parse(canonical));
    }

    private async Task WriteThroughAsync(Workspace workspace, string canonical, CancellationToken cancellationToken)
    {
        var controls = PolicyPackRules.ReadControls(canonical).ToDictionary(x => x.Key, StringComparer.Ordinal);
        await WriteAiAsync(workspace, controls[PolicyPackControls.AiEnabled], cancellationToken);
        await WriteRetentionAsync(
            workspace,
            controls[PolicyPackControls.RetentionEnabled],
            controls[PolicyPackControls.RetentionDays],
            cancellationToken);
        await WriteAuditFlagAsync(workspace, controls[PolicyPackControls.AuditContentEnabled], cancellationToken);
        await WriteDlpAsync(workspace, controls[PolicyPackControls.DlpSimulation], cancellationToken);
        await WriteUploadsAsync(workspace, controls[PolicyPackControls.UploadsMaxMb], cancellationToken);
    }

    private async Task WriteAiAsync(Workspace workspace, ResolvedControl control, CancellationToken cancellationToken)
    {
        var row = await db.AiSettings.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id, cancellationToken);
        if (row is null)
        {
            if (!control.Flag)
            {
                return;
            }

            db.AiSettings.Add(new AiSettings
            {
                WorkspaceId = workspace.Id,
                TenantId = workspace.TenantId,
                Enabled = true,
                Provider = "Mock"
            });
            return;
        }

        if (row.Enabled != control.Flag)
        {
            row.Enabled = control.Flag;
        }
    }

    private async Task WriteRetentionAsync(
        Workspace workspace,
        ResolvedControl enabled,
        ResolvedControl days,
        CancellationToken cancellationToken)
    {
        var row = await db.MessageRetentionSettings.FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, cancellationToken);
        var nextDays = (int)days.Number;
        if (row is null)
        {
            if (!enabled.Flag && nextDays == MessageRetentionSettings.DefaultRetentionDays)
            {
                return;
            }

            db.MessageRetentionSettings.Add(new MessageRetentionSettings
            {
                TenantId = workspace.TenantId,
                Enabled = enabled.Flag,
                RetentionDays = nextDays,
                UpdatedAt = clock.UtcNow
            });
            return;
        }

        if (row.Enabled == enabled.Flag && row.RetentionDays == nextDays)
        {
            return;
        }

        row.Enabled = enabled.Flag;
        row.RetentionDays = nextDays;
        row.UpdatedAt = clock.UtcNow;
    }

    private async Task WriteAuditFlagAsync(Workspace workspace, ResolvedControl control, CancellationToken cancellationToken)
    {
        if (!control.Flag)
        {
            return;
        }

        var row = await db.ContentAuditSettings.FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, cancellationToken);
        if (row is null || row.Enabled)
        {
            return;
        }

        row.Enabled = true;
        row.UpdatedAt = clock.UtcNow;
    }

    private async Task WriteDlpAsync(Workspace workspace, ResolvedControl control, CancellationToken cancellationToken)
    {
        var row = await db.DlpPolicies.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id, cancellationToken);
        if (row is null)
        {
            if (control.Flag)
            {
                return;
            }

            db.DlpPolicies.Add(new DlpPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Simulation = false,
                Version = 1,
                UpdatedAt = clock.UtcNow
            });
            return;
        }

        if (row.Simulation == control.Flag)
        {
            return;
        }

        row.Simulation = control.Flag;
        row.Version += 1;
        row.UpdatedAt = clock.UtcNow;
    }

    private async Task WriteUploadsAsync(Workspace workspace, ResolvedControl control, CancellationToken cancellationToken)
    {
        var ceiling = files.ReadCeiling();
        var bytes = Math.Min(control.Number * 1024 * 1024, ceiling.MaxSizeBytes);
        var row = await db.TenantFilesSettings.FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, cancellationToken);
        if (row is null)
        {
            if (bytes == ceiling.MaxSizeBytes)
            {
                return;
            }

            db.TenantFilesSettings.Add(new TenantFilesSettings
            {
                TenantId = workspace.TenantId,
                MaxSizeBytes = bytes,
                MaxAttachmentsPerMessage = ceiling.MaxAttachmentsPerMessage,
                PresignUploadTtlSeconds = ceiling.PresignUploadTtlSeconds,
                PresignDownloadTtlSeconds = ceiling.PresignDownloadTtlSeconds,
                AllowedContentTypes = ceiling.AllowedContentTypes.ToArray(),
                AudioMaxSizeBytes = ceiling.AudioMaxSizeBytes,
                AudioMaxDurationMs = ceiling.AudioMaxDurationMs,
                UpdatedAt = clock.UtcNow
            });
            return;
        }

        if (row.MaxSizeBytes == bytes)
        {
            return;
        }

        row.MaxSizeBytes = bytes;
        row.UpdatedAt = clock.UtcNow;
    }

    private async Task<Dictionary<string, JsonElement>> LiveAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var ai = await db.AiSettings.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => (bool?)x.Enabled)
            .FirstOrDefaultAsync(cancellationToken);
        var retention = await db.MessageRetentionSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, cancellationToken);
        var auditRow = await db.ContentAuditSettings.AsNoTracking()
            .Where(x => x.TenantId == workspace.TenantId)
            .Select(x => (bool?)x.Enabled)
            .FirstOrDefaultAsync(cancellationToken);
        var dlp = await db.DlpPolicies.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => (bool?)x.Simulation)
            .FirstOrDefaultAsync(cancellationToken);
        var filesRow = await db.TenantFilesSettings.AsNoTracking()
            .Where(x => x.TenantId == workspace.TenantId)
            .Select(x => (long?)x.MaxSizeBytes)
            .FirstOrDefaultAsync(cancellationToken);
        var ceiling = files.ReadCeiling().MaxSizeBytes;
        var uploadMb = (int)((filesRow ?? ceiling) / (1024 * 1024));
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [PolicyPackControls.AiEnabled] = Json(ai ?? false),
            [PolicyPackControls.RetentionEnabled] = Json(retention?.Enabled ?? false),
            [PolicyPackControls.RetentionDays] = Json(retention?.RetentionDays ?? MessageRetentionSettings.DefaultRetentionDays),
            [PolicyPackControls.AuditContentEnabled] = Json(ContentAuditRules.IsEnabled(auditRow)),
            [PolicyPackControls.DlpSimulation] = Json(dlp ?? true),
            [PolicyPackControls.UploadsMaxMb] = Json(uploadMb)
        };
    }

    private static JsonElement Json(bool value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement Json(int value) => JsonSerializer.SerializeToElement(value);

    private void WriteAudit(Workspace workspace, UserId actor, string action, string before, string after)
    {
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "PolicyPackAssignment",
            EntityId = workspace.Id.Value.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                before = Summarize(before),
                after = Summarize(after)
            })
        });
    }

    private static Dictionary<string, object> Summarize(string canonical)
    {
        if (canonical == "{}")
        {
            return [];
        }

        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var control in PolicyPackRules.ReadControls(canonical))
        {
            map[control.Key] = control.Integer ? control.Number : control.Flag;
        }

        return map;
    }

    private int CeilingMb()
    {
        var mb = (int)(files.ReadCeiling().MaxSizeBytes / (1024 * 1024));
        return mb < 1 ? PolicyPackLimits.DefaultUploadCeilingMb : mb;
    }

    private void Invalidate(Workspace workspace)
    {
        cache.InvalidateTenant(workspace.TenantId);
        cache.InvalidateWorkspace(workspace.TenantId, workspace.Id);
    }

    private Task<PolicyPackAssignment?> FindAsync(Workspace workspace, CancellationToken cancellationToken) =>
        db.PolicyPackAssignments.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id, cancellationToken);

    private Task<PolicyPackAssignment?> FindTrackedAsync(Workspace workspace, CancellationToken cancellationToken) =>
        db.PolicyPackAssignments.FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id, cancellationToken);

    private static JsonElement Parse(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
