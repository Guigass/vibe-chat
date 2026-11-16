using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure.Ai;

public sealed record AiSkillCommandResult(int Status, string? Error, object? Body);

public sealed class AiSkillCatalogService(
    VibeChatDbContext db,
    ITenantContext tenant,
    IAuditWriter audit,
    IOutboxWriter outbox,
    IIdempotencyStore idempotency,
    IClock clock)
{
    public async Task<AiSkillCommandResult> ListAsync(Workspace workspace, CancellationToken ct)
    {
        var rows = await db.AiSkillDefinitions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var skillIds = rows.Select(x => x.Id).ToArray();
        var versions = await db.AiSkillVersions.AsNoTracking()
            .Where(x => skillIds.Contains(x.SkillId))
            .Select(x => new { x.Id, x.SkillId, x.VersionNumber })
            .ToListAsync(ct);
        return Ok(rows.Select(skill => new
        {
            id = skill.Id,
            slug = skill.Slug,
            name = skill.Name,
            description = skill.Description,
            status = skill.Status.ToString(),
            revision = skill.Revision,
            activeVersion = skill.ActiveVersionNumber,
            activeVersionId = versions.FirstOrDefault(x => x.SkillId == skill.Id && x.VersionNumber == skill.ActiveVersionNumber)?.Id
        }).ToArray());
    }

    public async Task<AiSkillCommandResult> GetAsync(Workspace workspace, Guid skillId, CancellationToken ct)
    {
        var skill = await FindAsync(workspace.Id, skillId, ct);
        return skill is null ? Fail(404, AiSkillErrors.SkillNotFound) : Ok(await DetailAsync(skill, ct));
    }

    public async Task<AiSkillCommandResult> DiffAsync(Workspace workspace, Guid skillId, int fromVersion, int toVersion, CancellationToken ct)
    {
        var skill = await FindAsync(workspace.Id, skillId, ct);
        if (skill is null)
        {
            return Fail(404, AiSkillErrors.SkillNotFound);
        }

        var left = await SnapshotAsync(skill, fromVersion, ct);
        var right = await SnapshotAsync(skill, toVersion, ct);
        if (left is null || right is null)
        {
            return Fail(404, AiSkillErrors.SkillVersionNotFound);
        }

        return Ok(new { fromVersion, toVersion, changes = AiSkillCatalogPolicies.Diff(left, right) });
    }

    public async Task<AiSkillCommandResult> ExportAsync(Workspace workspace, Guid skillId, int versionNumber, CancellationToken ct)
    {
        var skill = await FindAsync(workspace.Id, skillId, ct);
        if (skill is null)
        {
            return Fail(404, AiSkillErrors.SkillNotFound);
        }

        var number = versionNumber == 0 ? skill.ActiveVersionNumber ?? 0 : versionNumber;
        var version = await db.AiSkillVersions.AsNoTracking().FirstOrDefaultAsync(
            x => x.SkillId == skill.Id && x.VersionNumber == number, ct);
        if (version is null)
        {
            return Fail(404, AiSkillErrors.SkillVersionNotFound);
        }

        return Ok(JsonSerializer.Deserialize<JsonElement>(AiSkillCatalogPolicies.ExportManifest(version))!);
    }

    public async Task<AiSkillCommandResult> ComposeAsync(Workspace workspace, Guid[] skillVersionIds, CancellationToken ct)
    {
        var rows = await LoadVersionsAsync(workspace.Id, skillVersionIds, ct);
        if (rows is null)
        {
            return Fail(400, AiSkillErrors.SkillReferenceRejected);
        }

        var ordered = skillVersionIds.Select(id => rows[id]).ToArray();
        if (!AiSkillCatalogPolicies.TryCompose(
                ordered.Select(row => (row.Id, row.Instructions, row.InstructionsSha256)).ToArray(),
                out var layers,
                out var error))
        {
            return Fail(400, error!);
        }

        return Ok(new { layers, warning = "instruction-does-not-grant-access" });
    }

    public Task<AiSkillCommandResult> CreateAsync(
        Workspace workspace, UserId actor, AiSkillDraft draft, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var count = await db.AiSkillDefinitions.CountAsync(x => x.WorkspaceId == workspace.Id, ct);
            var quota = AiSkillCatalogPolicies.QuotaError(count, creating: true);
            if (quota is not null)
            {
                return Fail(409, quota);
            }

            if (await SlugExistsAsync(workspace.Id, draft.Slug, null, ct))
            {
                return Fail(409, AiSkillErrors.SkillSlugConflict);
            }

            var now = clock.UtcNow;
            var skill = NewDefinition(workspace, actor, draft, now);
            db.AiSkillDefinitions.Add(skill);
            Touch(skill, actor, "ai.skill.create", now, version: null);
            await db.SaveChangesAsync(ct);
            return Ok(201, await DetailAsync(skill, ct));
        }, ct);

    public Task<AiSkillCommandResult> UpdateAsync(
        Workspace workspace, UserId actor, Guid skillId, int expectedRevision, AiSkillDraft draft, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var skill = await FindTrackedAsync(workspace.Id, skillId, ct);
            if (skill is null)
            {
                return Fail(404, AiSkillErrors.SkillNotFound);
            }

            if (skill.Revision != expectedRevision)
            {
                return Fail(409, AiSkillErrors.SkillRevisionConflict);
            }

            if (await SlugExistsAsync(workspace.Id, draft.Slug, skill.Id, ct))
            {
                return Fail(409, AiSkillErrors.SkillSlugConflict);
            }

            Apply(skill, draft);
            skill.Revision++;
            skill.UpdatedAt = clock.UtcNow;
            Touch(skill, actor, "ai.skill.update", skill.UpdatedAt, version: null);
            await db.SaveChangesAsync(ct);
            return Ok(await DetailAsync(skill, ct));
        }, ct);

    public Task<AiSkillCommandResult> PublishAsync(
        Workspace workspace, UserId actor, Guid skillId, int expectedRevision, AiSkillDraft draft, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var skill = await FindTrackedAsync(workspace.Id, skillId, ct);
            if (skill is null)
            {
                return Fail(404, AiSkillErrors.SkillNotFound);
            }

            if (skill.Revision != expectedRevision)
            {
                return Fail(409, AiSkillErrors.SkillRevisionConflict);
            }

            if (await SlugExistsAsync(workspace.Id, draft.Slug, skill.Id, ct))
            {
                return Fail(409, AiSkillErrors.SkillSlugConflict);
            }

            Apply(skill, draft);
            var numbers = await db.AiSkillVersions.Where(x => x.SkillId == skill.Id).Select(x => x.VersionNumber).ToListAsync(ct);
            var number = AiSkillCatalogPolicies.NextVersion(numbers);
            var now = clock.UtcNow;
            db.AiSkillVersions.Add(Snapshot(skill, number, actor, now));
            skill.Status = AiSkillStatus.Published;
            skill.ActiveVersionNumber = number;
            skill.Revision++;
            skill.UpdatedAt = now;
            Emit(skill, actor, PublicEventCatalog.SkillPublished, number, skill.Instructions, skill.Slug, now);
            Touch(skill, actor, "ai.skill.publish", now, number);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Fail(409, AiSkillErrors.SkillRevisionConflict);
            }

            return Ok(await DetailAsync(skill, ct));
        }, ct);

    public Task<AiSkillCommandResult> DeprecateAsync(
        Workspace workspace, UserId actor, Guid skillId, int expectedRevision, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var skill = await FindTrackedAsync(workspace.Id, skillId, ct);
            if (skill is null)
            {
                return Fail(404, AiSkillErrors.SkillNotFound);
            }

            if (skill.Revision != expectedRevision)
            {
                return Fail(409, AiSkillErrors.SkillRevisionConflict);
            }

            if (skill.Status != AiSkillStatus.Published || skill.ActiveVersionNumber is null)
            {
                return Fail(409, AiSkillErrors.SkillNotPublished);
            }

            var now = clock.UtcNow;
            skill.Status = AiSkillStatus.Deprecated;
            skill.Revision++;
            skill.UpdatedAt = now;
            Emit(skill, actor, PublicEventCatalog.SkillDeprecated, skill.ActiveVersionNumber.Value, skill.Instructions, skill.Slug, now);
            Touch(skill, actor, "ai.skill.deprecate", now, skill.ActiveVersionNumber);
            await db.SaveChangesAsync(ct);
            return Ok(await DetailAsync(skill, ct));
        }, ct);

    public Task<AiSkillCommandResult> CloneAsync(
        Workspace workspace, UserId actor, Guid skillId, int versionNumber, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var count = await db.AiSkillDefinitions.CountAsync(x => x.WorkspaceId == workspace.Id, ct);
            var quota = AiSkillCatalogPolicies.QuotaError(count, creating: true);
            if (quota is not null)
            {
                return Fail(409, quota);
            }

            var source = await FindAsync(workspace.Id, skillId, ct);
            if (source is null)
            {
                return Fail(404, AiSkillErrors.SkillNotFound);
            }

            var snapshot = await SnapshotAsync(source, versionNumber, ct);
            if (snapshot is null)
            {
                return Fail(404, AiSkillErrors.SkillVersionNotFound);
            }

            var slug = await CloneSlugAsync(workspace.Id, snapshot.Slug, ct);
            if (slug is null)
            {
                return Fail(409, AiSkillErrors.SkillSlugConflict);
            }

            var copy = snapshot with { Slug = slug, Name = snapshot.Name };
            var now = clock.UtcNow;
            var skill = NewDefinition(workspace, actor, copy, now);
            db.AiSkillDefinitions.Add(skill);
            Touch(skill, actor, "ai.skill.clone", now, version: null);
            await db.SaveChangesAsync(ct);
            return Ok(201, await DetailAsync(skill, ct));
        }, ct);

    public Task<AiSkillCommandResult> RollbackAsync(
        Workspace workspace, UserId actor, Guid skillId, int expectedRevision, int versionNumber, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var skill = await FindTrackedAsync(workspace.Id, skillId, ct);
            if (skill is null)
            {
                return Fail(404, AiSkillErrors.SkillNotFound);
            }

            if (skill.Revision != expectedRevision)
            {
                return Fail(409, AiSkillErrors.SkillRevisionConflict);
            }

            var version = await db.AiSkillVersions.FirstOrDefaultAsync(
                x => x.SkillId == skill.Id && x.VersionNumber == versionNumber, ct);
            if (version is null)
            {
                return Fail(404, AiSkillErrors.SkillVersionNotFound);
            }

            if (skill.Status == AiSkillStatus.Published && skill.ActiveVersionNumber == versionNumber)
            {
                return Ok(await DetailAsync(skill, ct));
            }

            var before = await db.AiSkillVersions.Where(x => x.SkillId == skill.Id).Select(x => x.VersionNumber).ToListAsync(ct);
            Apply(skill, AiSkillCatalogPolicies.FromVersion(version));
            var now = clock.UtcNow;
            skill.Status = AiSkillStatus.Published;
            skill.ActiveVersionNumber = versionNumber;
            skill.Revision++;
            skill.UpdatedAt = now;
            Touch(skill, actor, "ai.skill.rollback", now, versionNumber);
            await db.SaveChangesAsync(ct);
            var after = await db.AiSkillVersions.Where(x => x.SkillId == skill.Id).Select(x => x.VersionNumber).ToListAsync(ct);
            if (!AiSkillCatalogPolicies.KeepsHistory(before, after))
            {
                return Fail(500, AiSkillErrors.SkillRevisionConflict);
            }

            return Ok(await DetailAsync(skill, ct));
        }, ct);

    public Task<AiSkillCommandResult> ImportAsync(
        Workspace workspace, UserId actor, AiSkillImport imported, string key, string hash, CancellationToken ct) =>
        MutateAsync(workspace, key, hash, async () =>
        {
            var existing = await db.AiSkillDefinitions.FirstOrDefaultAsync(
                x => x.WorkspaceId == workspace.Id && x.Slug == imported.Slug, ct);
            if (existing is not null)
            {
                var collision = await db.AiSkillVersions.AnyAsync(
                    x => x.SkillId == existing.Id && x.VersionNumber == imported.Version, ct);
                if (collision)
                {
                    return Fail(409, AiSkillErrors.SkillSlugVersionConflict);
                }
            }
            else
            {
                var count = await db.AiSkillDefinitions.CountAsync(x => x.WorkspaceId == workspace.Id, ct);
                var quota = AiSkillCatalogPolicies.QuotaError(count, creating: true);
                if (quota is not null)
                {
                    return Fail(409, quota);
                }
            }

            var now = clock.UtcNow;
            var draft = new AiSkillDraft(
                imported.Slug, imported.Name, imported.Description, imported.Instructions, imported.ArgumentsSchemaJson,
                imported.ExamplesJson, imported.Compatibility, imported.Classification, imported.KnowledgeGrantIds, imported.McpToolGrantIds);
            var skill = existing ?? NewDefinition(workspace, actor, draft, now);
            if (existing is null)
            {
                db.AiSkillDefinitions.Add(skill);
            }

            var becomesActive = skill.ActiveVersionNumber is null || imported.Version >= skill.ActiveVersionNumber;
            if (becomesActive)
            {
                Apply(skill, draft);
                skill.ActiveVersionNumber = imported.Version;
                skill.Status = AiSkillStatus.Published;
            }

            var version = Snapshot(skill, imported.Version, actor, now);
            version.Instructions = imported.Instructions;
            version.InstructionsSha256 = AiSkillCatalogPolicies.InstructionsHash(imported.Instructions);
            version.Name = imported.Name;
            version.Description = imported.Description;
            version.ArgumentsSchemaJson = imported.ArgumentsSchemaJson;
            version.ExamplesJson = imported.ExamplesJson;
            version.Compatibility = imported.Compatibility;
            version.Classification = imported.Classification;
            version.KnowledgeGrantIds = imported.KnowledgeGrantIds.ToArray();
            version.McpToolGrantIds = imported.McpToolGrantIds.ToArray();
            db.AiSkillVersions.Add(version);
            skill.Revision++;
            skill.UpdatedAt = now;
            Emit(skill, actor, PublicEventCatalog.SkillPublished, imported.Version, imported.Instructions, imported.Slug, now);
            Touch(skill, actor, "ai.skill.import", now, imported.Version);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Fail(409, AiSkillErrors.SkillSlugVersionConflict);
            }

            return Ok(existing is null ? 201 : 200, await DetailAsync(skill, ct));
        }, ct);

    private async Task<AiSkillCommandResult> MutateAsync(
        Workspace workspace, string key, string hash, Func<Task<AiSkillCommandResult>> action, CancellationToken ct)
    {
        tenant.SetTenant(workspace.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        var storeKey = $"aiskill:{workspace.Id.Value:N}:{key}";
        var existing = await idempotency.FindAsync(workspace.TenantId, storeKey, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            {
                return Fail(409, AiSkillErrors.IdempotencyConflict);
            }

            var envelope = JsonSerializer.Deserialize<StoredBody>(existing.ResultJson);
            return envelope is null
                ? Fail(409, AiSkillErrors.IdempotencyConflict)
                : new AiSkillCommandResult(envelope.Status, null, envelope.Body);
        }

        var result = await action();
        if (result.Error is null && result.Body is not null)
        {
            await idempotency.StoreAsync(new IdempotencyRecord(
                workspace.TenantId,
                storeKey,
                hash,
                JsonSerializer.Serialize(new StoredBody(result.Status, JsonSerializer.SerializeToElement(result.Body))),
                clock.UtcNow), ct);
            await db.SaveChangesAsync(ct);
        }

        if (result.Error is null)
        {
            await RlsSession.CommitAsync(db, ct);
        }

        return result;
    }

    private async Task<Dictionary<Guid, AiSkillVersion>?> LoadVersionsAsync(WorkspaceId workspaceId, Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, AiSkillVersion>();
        }

        var rows = await db.AiSkillVersions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && ids.Contains(x.Id))
            .ToListAsync(ct);
        if (rows.Count != ids.Length)
        {
            return null;
        }

        var skillIds = rows.Select(x => x.SkillId).Distinct().ToArray();
        var definitions = await db.AiSkillDefinitions.AsNoTracking().Where(x => skillIds.Contains(x.Id)).ToListAsync(ct);
        if (definitions.Count != skillIds.Length || definitions.Any(x => x.Status != AiSkillStatus.Published))
        {
            return null;
        }

        return rows.ToDictionary(x => x.Id);
    }

    private async Task<bool> SlugExistsAsync(WorkspaceId workspaceId, string slug, Guid? exceptId, CancellationToken ct) =>
        await db.AiSkillDefinitions.AnyAsync(x => x.WorkspaceId == workspaceId && x.Slug == slug && x.Id != exceptId, ct);

    private async Task<string?> CloneSlugAsync(WorkspaceId workspaceId, string slug, CancellationToken ct)
    {
        var candidate = TrimSlug(slug + "-copy");
        if (!await SlugExistsAsync(workspaceId, candidate, null, ct))
        {
            return candidate;
        }

        for (var n = 2; n <= 5; n++)
        {
            candidate = TrimSlug($"{slug}-copy-{n}");
            if (!await SlugExistsAsync(workspaceId, candidate, null, ct))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string TrimSlug(string slug) =>
        slug.Length <= AiSkillCatalogPolicies.MaxSlugLength ? slug : slug[..AiSkillCatalogPolicies.MaxSlugLength].Trim('-');

    private async Task<AiSkillDefinition?> FindAsync(WorkspaceId workspaceId, Guid skillId, CancellationToken ct) =>
        await db.AiSkillDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == skillId, ct);

    private async Task<AiSkillDefinition?> FindTrackedAsync(WorkspaceId workspaceId, Guid skillId, CancellationToken ct) =>
        await db.AiSkillDefinitions.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == skillId, ct);

    private async Task<AiSkillDraft?> SnapshotAsync(AiSkillDefinition skill, int version, CancellationToken ct)
    {
        if (version == AiSkillCatalogPolicies.DraftVersion)
        {
            return Current(skill);
        }

        var row = await db.AiSkillVersions.AsNoTracking().FirstOrDefaultAsync(
            x => x.SkillId == skill.Id && x.VersionNumber == version, ct);
        return row is null ? null : AiSkillCatalogPolicies.FromVersion(row);
    }

    private async Task<object> DetailAsync(AiSkillDefinition skill, CancellationToken ct)
    {
        var versions = await db.AiSkillVersions.AsNoTracking()
            .Where(x => x.SkillId == skill.Id)
            .OrderBy(x => x.VersionNumber)
            .Select(x => new { x.Id, x.VersionNumber, x.PublishedAt, x.InstructionsSha256 })
            .ToListAsync(ct);
        return new
        {
            id = skill.Id,
            slug = skill.Slug,
            name = skill.Name,
            status = skill.Status.ToString(),
            revision = skill.Revision,
            activeVersion = skill.ActiveVersionNumber,
            ownerUserId = skill.OwnerUserId.Value,
            draft = DraftBody(skill),
            versions
        };
    }

    private void Emit(AiSkillDefinition skill, UserId actor, string eventType, int version, string instructions, string slug, DateTimeOffset now)
    {
        var payload = AiSkillCatalogPolicies.EventDocument(
            eventType, skill.Id, skill.WorkspaceId.Value, skill.TenantId.Value, version, actor.Value,
            AiSkillCatalogPolicies.InstructionsHash(instructions), slug);
        var decision = PublicEventCatalog.Validate(eventType, 1, payload);
        if (!decision.Accepted)
        {
            throw new InvalidOperationException(decision.Reason);
        }

        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = skill.TenantId,
            Type = eventType,
            Payload = payload,
            OccurredAt = now
        });
    }

    private void Touch(AiSkillDefinition skill, UserId actor, string action, DateTimeOffset now, int? version)
    {
        audit.Add(new AuditEvent
        {
            TenantId = skill.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "AiSkill",
            EntityId = skill.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { version, status = skill.Status.ToString(), slug = skill.Slug }),
            OccurredAt = now
        });
    }

    private static AiSkillDefinition NewDefinition(Workspace workspace, UserId actor, AiSkillDraft draft, DateTimeOffset now)
    {
        var skill = new AiSkillDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            OwnerUserId = actor,
            Status = AiSkillStatus.Draft,
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        Apply(skill, draft);
        return skill;
    }

    private static void Apply(AiSkillDefinition skill, AiSkillDraft draft)
    {
        skill.Slug = draft.Slug;
        skill.Name = draft.Name;
        skill.Description = draft.Description;
        skill.Instructions = draft.Instructions;
        skill.ArgumentsSchemaJson = draft.ArgumentsSchemaJson;
        skill.ExamplesJson = draft.ExamplesJson;
        skill.Compatibility = draft.Compatibility;
        skill.Classification = draft.Classification;
        skill.KnowledgeGrantIds = draft.KnowledgeGrantIds;
        skill.McpToolGrantIds = draft.McpToolGrantIds;
    }

    private static AiSkillVersion Snapshot(AiSkillDefinition skill, int number, UserId actor, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = skill.TenantId,
        WorkspaceId = skill.WorkspaceId,
        SkillId = skill.Id,
        VersionNumber = number,
        Slug = skill.Slug,
        Name = skill.Name,
        Description = skill.Description,
        Instructions = skill.Instructions,
        InstructionsSha256 = AiSkillCatalogPolicies.InstructionsHash(skill.Instructions),
        ArgumentsSchemaJson = skill.ArgumentsSchemaJson,
        ExamplesJson = skill.ExamplesJson,
        OwnerUserId = skill.OwnerUserId,
        Compatibility = skill.Compatibility,
        Classification = skill.Classification,
        KnowledgeGrantIds = skill.KnowledgeGrantIds.ToArray(),
        McpToolGrantIds = skill.McpToolGrantIds.ToArray(),
        PublishedBy = actor,
        PublishedAt = now
    };

    private static AiSkillDraft Current(AiSkillDefinition skill) => new(
        skill.Slug, skill.Name, skill.Description, skill.Instructions, skill.ArgumentsSchemaJson, skill.ExamplesJson,
        skill.Compatibility, skill.Classification, skill.KnowledgeGrantIds.ToArray(), skill.McpToolGrantIds.ToArray());

    private static object DraftBody(AiSkillDefinition skill) => new
    {
        slug = skill.Slug,
        name = skill.Name,
        description = skill.Description,
        instructions = skill.Instructions,
        argumentsSchema = JsonSerializer.Deserialize<JsonElement>(skill.ArgumentsSchemaJson),
        examples = JsonSerializer.Deserialize<JsonElement>(skill.ExamplesJson),
        compatibility = skill.Compatibility,
        classification = skill.Classification,
        knowledgeGrantIds = skill.KnowledgeGrantIds,
        mcpToolGrantIds = skill.McpToolGrantIds
    };

    private static AiSkillCommandResult Ok(object body) => new(200, null, body);

    private static AiSkillCommandResult Ok(int status, object body) => new(status, null, body);

    private static AiSkillCommandResult Fail(int status, string error) => new(status, error, null);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException postgres && postgres.SqlState == "23505";

    private sealed record StoredBody(int Status, JsonElement Body);
}
