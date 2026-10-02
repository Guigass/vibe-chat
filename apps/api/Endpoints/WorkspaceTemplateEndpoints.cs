using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class WorkspaceTemplateEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void MapWorkspaceTemplates(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/templates", List)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/templates/validate", Validate)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/templates/preview", Preview)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/templates/apply", Apply)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/templates/import", Import)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/templates/export", ExportWorkspace)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/templates/{templateId}/export", ExportTemplate)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/onboarding", GetOnboarding)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPut("/admin/workspaces/{workspaceId:guid}/onboarding", UpdateOnboarding)
            .RequirePermission(Permissions.Workspace.Admin);
    }

    private static async Task<IResult> List(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var scope = await RequireAdminAsync(workspaceId, http, db, tenant, clock, ct);
        if (scope is null)
        {
            return Results.Forbid();
        }

        var custom = await db.WorkspaceTemplateRecords.AsNoTracking()
            .Where(x => x.WorkspaceId == scope.Value.Workspace.Id)
            .OrderBy(x => x.TemplateId)
            .ToListAsync(ct);
        var customSummaries = new List<TemplateSummaryResponse>();
        foreach (var row in custom)
        {
            if (WorkspaceTemplateRules.TryParse(row.ManifestJson, out var document, out _, out _) && document is not null)
            {
                customSummaries.Add(Summarize(document, false));
            }
        }

        return Results.Ok(new TemplateCatalogResponse(
            WorkspaceTemplateRules.Builtins.Select(x => Summarize(x, true)).ToArray(),
            customSummaries.ToArray()));
    }

    private static async Task<IResult> Validate(
        Guid workspaceId,
        JsonElement body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var scope = await RequireAdminAsync(workspaceId, http, db, tenant, clock, ct);
        if (scope is null)
        {
            return Results.Forbid();
        }

        if (!WorkspaceTemplateRules.TryParse(body.GetRawText(), out var document, out var error, out var path) || document is null)
        {
            return Fail(StatusCodes.Status400BadRequest, error, path);
        }

        return Results.Ok(new TemplateValidationResponse(
            document.Id,
            document.Version,
            document.Spaces.Count,
            document.Spaces.Sum(x => x.Channels.Count)));
    }

    private static async Task<IResult> Preview(
        Guid workspaceId,
        JsonElement body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var scope = await RequireAdminAsync(workspaceId, http, db, tenant, clock, ct);
        if (scope is null)
        {
            return Results.Forbid();
        }

        if (!TryReadSelection(body, false, out var templateId, out var manifestJson, out _, out var error, out var path))
        {
            return Fail(StatusCodes.Status400BadRequest, error, path);
        }

        var resolved = await ResolveDocumentAsync(scope.Value.Workspace, templateId, manifestJson, db, ct);
        if (resolved.Error is not null || resolved.Document is null)
        {
            return resolved.Error!;
        }

        var plan = await BuildPlanAsync(scope.Value.Workspace, resolved.Document, db, ct);
        return Results.Ok(new TemplatePreviewResponse(
            resolved.Document.Id,
            resolved.Document.Version,
            WorkspaceTemplateRules.HasConflicts(plan.Items),
            true,
            ToResponses(plan.Items, plan.Ids)));
    }

    private static async Task<IResult> Apply(
        Guid workspaceId,
        JsonElement body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var scope = await RequireAdminAsync(workspaceId, http, db, tenant, clock, ct);
        if (scope is null)
        {
            return Results.Forbid();
        }

        if (!TryReadSelection(body, true, out var templateId, out var manifestJson, out var dryRun, out var error, out var path))
        {
            return Fail(StatusCodes.Status400BadRequest, error, path);
        }

        var resolved = await ResolveDocumentAsync(scope.Value.Workspace, templateId, manifestJson, db, ct);
        if (resolved.Error is not null || resolved.Document is null)
        {
            return resolved.Error!;
        }

        var document = resolved.Document;
        if (!dryRun)
        {
            if (!TryIdempotencyKey(http.Request, out var key))
            {
                return Fail(StatusCodes.Status400BadRequest, WorkspaceTemplateRules.InvalidIdempotencyKey, null);
            }

            var prior = await db.TemplateApplications.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkspaceId == scope.Value.Workspace.Id && x.IdempotencyKey == key, ct);
            if (prior is not null)
            {
                if (prior.TemplateId != document.Id || prior.TemplateVersion != document.Version)
                {
                    return Fail(StatusCodes.Status409Conflict, WorkspaceTemplateRules.IdempotencyReused, null);
                }

                var replay = JsonSerializer.Deserialize<TemplateApplyResponse>(prior.ResultJson, Json);
                if (replay is null)
                {
                    return Fail(StatusCodes.Status400BadRequest, WorkspaceTemplateRules.Invalid, null);
                }

                return Results.Ok(replay with { Idempotent = true });
            }

            var plan = await BuildPlanAsync(scope.Value.Workspace, document, db, ct);
            if (WorkspaceTemplateRules.HasConflicts(plan.Items))
            {
                return Fail(StatusCodes.Status409Conflict, WorkspaceTemplateRules.Conflict, null);
            }

            var ids = await PersistAsync(scope.Value, document, plan, key, db, audit, clock, ct);
            var created = new TemplateApplyResponse(
                document.Id,
                document.Version,
                false,
                false,
                false,
                ToResponses(plan.Items, ids));
            return Results.Ok(created);
        }

        var dryPlan = await BuildPlanAsync(scope.Value.Workspace, document, db, ct);
        return Results.Ok(new TemplateApplyResponse(
            document.Id,
            document.Version,
            WorkspaceTemplateRules.HasConflicts(dryPlan.Items),
            true,
            false,
            ToResponses(dryPlan.Items, dryPlan.Ids)));
    }

    private static async Task<IResult> Import(
        Guid workspaceId,
        JsonElement body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var scope = await RequireAdminAsync(workspaceId, http, db, tenant, clock, ct);
        if (scope is null)
        {
            return Results.Forbid();
        }

        if (!WorkspaceTemplateRules.TryParse(body.GetRawText(), out var document, out var error, out var path) || document is null)
        {
            return Fail(StatusCodes.Status400BadRequest, error, path);
        }

        if (WorkspaceTemplateRules.IsBuiltin(document.Id))
        {
            return Fail(StatusCodes.Status409Conflict, WorkspaceTemplateRules.Reserved, "$.id");
        }

        var existing = await db.WorkspaceTemplateRecords
            .FirstOrDefaultAsync(x => x.WorkspaceId == scope.Value.Workspace.Id && x.TemplateId == document.Id, ct);
        if (existing is null)
        {
            var count = await db.WorkspaceTemplateRecords.CountAsync(x => x.WorkspaceId == scope.Value.Workspace.Id, ct);
            if (count >= WorkspaceTemplateRules.MaxCustomTemplates)
            {
                return Fail(StatusCodes.Status409Conflict, WorkspaceTemplateRules.LimitReached, null);
            }

            existing = new WorkspaceTemplateRecord
            {
                Id = Guid.NewGuid(),
                TenantId = scope.Value.Workspace.TenantId,
                WorkspaceId = scope.Value.Workspace.Id,
                TemplateId = document.Id,
                CreatedBy = scope.Value.Profile.Id,
                CreatedAt = clock.UtcNow
            };
            db.WorkspaceTemplateRecords.Add(existing);
        }

        existing.Version = document.Version;
        existing.ManifestJson = document.CanonicalJson;
        existing.UpdatedAt = clock.UtcNow;
        audit.Add(new AuditEvent
        {
            TenantId = scope.Value.Workspace.TenantId,
            ActorUserId = scope.Value.Profile.Id,
            Action = AuditActions.TemplateImport,
            EntityType = "WorkspaceTemplate",
            EntityId = existing.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { templateId = document.Id, version = document.Version })
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(Summarize(document, false));
    }

    private static async Task<IResult> ExportWorkspace(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var scope = await RequireAdminAsync(workspaceId, http, db, tenant, clock, ct);
        if (scope is null)
        {
            return Results.Forbid();
        }

        var snapshot = await LoadStructureAsync(scope.Value.Workspace, db, ct);
        if (!WorkspaceTemplateRules.TryExport(snapshot.Spaces, snapshot.Channels, snapshot.Policy, out var json, out var error) || json is null)
        {
            return Fail(StatusCodes.Status400BadRequest, error, null);
        }

        audit.Add(new AuditEvent
        {
            TenantId = scope.Value.Workspace.TenantId,
            ActorUserId = scope.Value.Profile.Id,
            Action = AuditActions.TemplateExport,
            EntityType = "Workspace",
            EntityId = scope.Value.Workspace.Id.Value.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { templateId = WorkspaceTemplateRules.ExportId })
        });
        await db.SaveChangesAsync(ct);
        return Results.Content(json, "application/json");
    }

    private static async Task<IResult> ExportTemplate(
        Guid workspaceId,
        string templateId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var scope = await RequireAdminAsync(workspaceId, http, db, tenant, clock, ct);
        if (scope is null)
        {
            return Results.Forbid();
        }

        string? json = null;
        if (WorkspaceTemplateRules.TryGetBuiltin(templateId, out var builtin) && builtin is not null)
        {
            json = builtin.CanonicalJson;
        }
        else
        {
            var row = await db.WorkspaceTemplateRecords.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkspaceId == scope.Value.Workspace.Id && x.TemplateId == templateId, ct);
            json = row?.ManifestJson;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail(StatusCodes.Status404NotFound, WorkspaceTemplateRules.NotFound, null);
        }

        audit.Add(new AuditEvent
        {
            TenantId = scope.Value.Workspace.TenantId,
            ActorUserId = scope.Value.Profile.Id,
            Action = AuditActions.TemplateExport,
            EntityType = "WorkspaceTemplate",
            EntityId = templateId,
            MetadataJson = JsonSerializer.Serialize(new { templateId })
        });
        await db.SaveChangesAsync(ct);
        return Results.Content(json, "application/json");
    }

    private static async Task<IResult> GetOnboarding(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var scope = await RequireAdminAsync(workspaceId, http, db, tenant, clock, ct);
        if (scope is null)
        {
            return Results.Forbid();
        }

        var row = await db.WorkspaceOnboardings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == scope.Value.Workspace.Id, ct);
        return Results.Ok(row is null
            ? new OnboardingResponse(OnboardingRules.Pending, null, null, DefaultItems())
            : ToOnboarding(row));
    }

    private static async Task<IResult> UpdateOnboarding(
        Guid workspaceId,
        JsonElement body,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var scope = await RequireAdminAsync(workspaceId, http, db, tenant, clock, ct);
        if (scope is null)
        {
            return Results.Forbid();
        }

        if (!TryReadOnboarding(body, out var status, out var items, out var error, out var path))
        {
            return Fail(StatusCodes.Status400BadRequest, error, path);
        }

        var row = await db.WorkspaceOnboardings
            .FirstOrDefaultAsync(x => x.WorkspaceId == scope.Value.Workspace.Id, ct);
        var current = row is null ? DefaultItems().ToList() : ParseItems(row.ItemsJson);
        if (items is not null)
        {
            foreach (var item in items)
            {
                var match = current.FindIndex(x => x.Key == item.Key);
                if (match < 0)
                {
                    return Fail(StatusCodes.Status400BadRequest, OnboardingRules.Invalid, "$.items");
                }

                current[match] = item;
            }
        }

        if (row is null)
        {
            row = new WorkspaceOnboarding
            {
                Id = Guid.NewGuid(),
                TenantId = scope.Value.Workspace.TenantId,
                WorkspaceId = scope.Value.Workspace.Id
            };
            db.WorkspaceOnboardings.Add(row);
        }

        if (status is not null)
        {
            row.Status = status;
        }
        else if (row.Status == OnboardingRules.Pending)
        {
            row.Status = OnboardingRules.InProgress;
        }

        row.ItemsJson = JsonSerializer.Serialize(current, Json);
        row.UpdatedAt = clock.UtcNow;
        audit.Add(new AuditEvent
        {
            TenantId = scope.Value.Workspace.TenantId,
            ActorUserId = scope.Value.Profile.Id,
            Action = AuditActions.OnboardingUpdate,
            EntityType = "WorkspaceOnboarding",
            EntityId = row.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { status = row.Status })
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToOnboarding(row));
    }

    private static async Task<Dictionary<string, Guid>> PersistAsync(
        AdminScope scope,
        WorkspaceTemplateDocument document,
        PlanSnapshot plan,
        string idempotencyKey,
        VibeChatDbContext db,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var ids = new Dictionary<string, Guid>(plan.Ids, StringComparer.Ordinal);
        var maxOrder = await db.Spaces.Where(x => x.WorkspaceId == scope.Workspace.Id).Select(x => (int?)x.Order).MaxAsync(ct) ?? -1;
        foreach (var space in document.Spaces)
        {
            var item = plan.Items.First(x => x.Kind == TemplatePlanKinds.Space && x.Key == space.Key);
            if (item.Action != TemplatePlanActions.Create)
            {
                continue;
            }

            maxOrder++;
            var created = new Space
            {
                Id = Guid.NewGuid(),
                TenantId = scope.Workspace.TenantId,
                WorkspaceId = scope.Workspace.Id,
                Name = space.Name,
                Order = maxOrder,
                CreatedAt = clock.UtcNow
            };
            db.Spaces.Add(created);
            ids[SpaceIdKey(space.Key)] = created.Id;
            audit.Add(new AuditEvent
            {
                TenantId = scope.Workspace.TenantId,
                ActorUserId = scope.Profile.Id,
                Action = AuditActions.SpaceCreate,
                EntityType = "Space",
                EntityId = created.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { templateId = document.Id, key = space.Key, name = space.Name })
            });
        }

        foreach (var space in document.Spaces)
        {
            var spaceId = ids[SpaceIdKey(space.Key)];
            foreach (var channel in space.Channels)
            {
                var item = plan.Items.First(x => x.Kind == TemplatePlanKinds.Channel && x.Key == channel.Key && x.SpaceKey == space.Key);
                if (item.Action != TemplatePlanActions.Create)
                {
                    continue;
                }

                var created = new Channel
                {
                    Id = ChannelId.New(),
                    TenantId = scope.Workspace.TenantId,
                    WorkspaceId = scope.Workspace.Id,
                    SpaceId = spaceId,
                    Name = channel.Name,
                    Topic = channel.Topic,
                    Type = Enum.Parse<ChannelType>(channel.Type),
                    CreatedAt = clock.UtcNow,
                    CreatedBy = scope.Profile.Id
                };
                db.Channels.Add(created);
                db.ChannelMembers.Add(new ChannelMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = scope.Workspace.TenantId,
                    ChannelId = created.Id,
                    UserId = scope.Profile.Id,
                    JoinedAt = clock.UtcNow
                });
                ids[ChannelIdKey(space.Key, channel.Key)] = created.Id.Value;
                audit.Add(new AuditEvent
                {
                    TenantId = scope.Workspace.TenantId,
                    ActorUserId = scope.Profile.Id,
                    Action = AuditActions.ChannelCreate,
                    EntityType = "Channel",
                    EntityId = created.Id.Value.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        templateId = document.Id,
                        key = channel.Key,
                        name = channel.Name,
                        type = channel.Type
                    })
                });
            }
        }

        var policyItem = plan.Items.FirstOrDefault(x => x.Kind == TemplatePlanKinds.Policy);
        if (policyItem?.Action == TemplatePlanActions.Create && document.Policy is { } policy)
        {
            db.MessageLifecyclePolicies.Add(new MessageLifecyclePolicy
            {
                TenantId = scope.Workspace.TenantId,
                EditEnabled = policy.EditEnabled,
                EditWindowMinutes = policy.EditWindowMinutes,
                EditRolesRestricted = policy.EditRolesRestricted,
                EditRoles = policy.EditRoles,
                EditAllowModeratorOverride = policy.EditAllowModeratorOverride,
                DeleteEnabled = policy.DeleteEnabled,
                DeleteWindowMinutes = policy.DeleteWindowMinutes,
                DeleteRolesRestricted = policy.DeleteRolesRestricted,
                DeleteRoles = policy.DeleteRoles,
                DeleteAllowModeratorOverride = policy.DeleteAllowModeratorOverride,
                UpdatedAt = clock.UtcNow
            });
            audit.Add(new AuditEvent
            {
                TenantId = scope.Workspace.TenantId,
                ActorUserId = scope.Profile.Id,
                Action = AuditActions.SettingsChange,
                EntityType = "MessageLifecyclePolicy",
                EntityId = scope.Workspace.TenantId.Value.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { source = "template", templateId = document.Id })
            });
        }

        var onboarding = await db.WorkspaceOnboardings
            .FirstOrDefaultAsync(x => x.WorkspaceId == scope.Workspace.Id, ct);
        var checklist = onboarding is null ? DefaultItems().ToList() : ParseItems(onboarding.ItemsJson);
        var merged = document.Checklist
            .Select(key => checklist.FirstOrDefault(x => x.Key == key) ?? new OnboardingItemResponse(key, OnboardingRules.Open))
            .ToArray();
        if (onboarding is null)
        {
            onboarding = new WorkspaceOnboarding
            {
                Id = Guid.NewGuid(),
                TenantId = scope.Workspace.TenantId,
                WorkspaceId = scope.Workspace.Id
            };
            db.WorkspaceOnboardings.Add(onboarding);
        }

        onboarding.Status = OnboardingRules.InProgress;
        onboarding.TemplateId = document.Id;
        onboarding.TemplateVersion = document.Version;
        onboarding.ItemsJson = JsonSerializer.Serialize(merged, Json);
        onboarding.UpdatedAt = clock.UtcNow;

        var response = new TemplateApplyResponse(
            document.Id,
            document.Version,
            false,
            false,
            false,
            ToResponses(plan.Items, ids));
        db.TemplateApplications.Add(new TemplateApplication
        {
            Id = Guid.NewGuid(),
            TenantId = scope.Workspace.TenantId,
            WorkspaceId = scope.Workspace.Id,
            IdempotencyKey = idempotencyKey,
            TemplateId = document.Id,
            TemplateVersion = document.Version,
            ResultJson = JsonSerializer.Serialize(response, Json),
            ActorUserId = scope.Profile.Id,
            CreatedAt = clock.UtcNow
        });
        audit.Add(new AuditEvent
        {
            TenantId = scope.Workspace.TenantId,
            ActorUserId = scope.Profile.Id,
            Action = AuditActions.TemplateApply,
            EntityType = "WorkspaceTemplate",
            EntityId = document.Id,
            MetadataJson = JsonSerializer.Serialize(new
            {
                templateId = document.Id,
                version = document.Version,
                created = plan.Items.Count(x => x.Action == TemplatePlanActions.Create)
            })
        });
        await db.SaveChangesAsync(ct);
        return ids;
    }

    private static async Task<PlanSnapshot> BuildPlanAsync(
        Workspace workspace,
        WorkspaceTemplateDocument document,
        VibeChatDbContext db,
        CancellationToken ct)
    {
        var snapshot = await LoadStructureAsync(workspace, db, ct);
        var items = WorkspaceTemplateRules.Plan(document, snapshot.Spaces, snapshot.Channels, snapshot.Policy);
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var space in document.Spaces)
        {
            var existing = snapshot.Spaces.FirstOrDefault(x => x.Name.Equals(space.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                ids[SpaceIdKey(space.Key)] = existing.Id;
            }

            foreach (var channel in space.Channels)
            {
                var match = snapshot.Channels.FirstOrDefault(x => x.Name.Equals(channel.Name, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    ids[ChannelIdKey(space.Key, channel.Key)] = match.Id;
                }
            }
        }

        return new PlanSnapshot(items, ids, snapshot.Spaces, snapshot.Channels, snapshot.Policy);
    }

    private static async Task<StructureSnapshot> LoadStructureAsync(
        Workspace workspace,
        VibeChatDbContext db,
        CancellationToken ct)
    {
        var spaces = await db.Spaces.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => new TemplateExistingSpace(x.Id, x.Name))
            .ToListAsync(ct);
        var channelRows = await db.Channels.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => new { Id = x.Id.Value, x.SpaceId, x.Name, x.Type, x.Topic })
            .ToListAsync(ct);
        var channels = channelRows
            .Select(x => new TemplateExistingChannel(x.Id, x.SpaceId, x.Name, x.Type.ToString(), x.Topic))
            .ToList();
        var policyRow = await db.MessageLifecyclePolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == workspace.TenantId, ct);
        TemplateExistingPolicy? policy = policyRow is null
            ? null
            : new TemplateExistingPolicy(
                policyRow.EditEnabled,
                policyRow.EditWindowMinutes,
                policyRow.EditRolesRestricted,
                policyRow.EditRoles,
                policyRow.EditAllowModeratorOverride,
                policyRow.DeleteEnabled,
                policyRow.DeleteWindowMinutes,
                policyRow.DeleteRolesRestricted,
                policyRow.DeleteRoles,
                policyRow.DeleteAllowModeratorOverride);
        return new StructureSnapshot(spaces, channels, policy);
    }

    private static async Task<(WorkspaceTemplateDocument? Document, IResult? Error)> ResolveDocumentAsync(
        Workspace workspace,
        string? templateId,
        string? manifestJson,
        VibeChatDbContext db,
        CancellationToken ct)
    {
        if (manifestJson is not null)
        {
            if (!WorkspaceTemplateRules.TryParse(manifestJson, out var parsed, out var error, out var path) || parsed is null)
            {
                return (null, Fail(StatusCodes.Status400BadRequest, error, path));
            }

            return (parsed, null);
        }

        if (WorkspaceTemplateRules.TryGetBuiltin(templateId, out var builtin) && builtin is not null)
        {
            return (builtin, null);
        }

        var row = await db.WorkspaceTemplateRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.TemplateId == templateId, ct);
        if (row is null || !WorkspaceTemplateRules.TryParse(row.ManifestJson, out var custom, out _, out _) || custom is null)
        {
            return (null, Fail(StatusCodes.Status404NotFound, WorkspaceTemplateRules.NotFound, null));
        }

        return (custom, null);
    }

    private static async Task<AdminScope?> RequireAdminAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return null;
        }

        var membership = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == profile.Id, ct);
        if (membership is null || !WorkspaceRolePolicies.CanManageRoles(membership.Role))
        {
            return null;
        }

        return new AdminScope(profile, workspace);
    }

    private static bool TryReadSelection(
        JsonElement body,
        bool allowDryRun,
        out string? templateId,
        out string? manifestJson,
        out bool dryRun,
        out string error,
        out string? path)
    {
        templateId = null;
        manifestJson = null;
        dryRun = false;
        error = WorkspaceTemplateRules.Invalid;
        path = null;
        if (body.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var allowed = allowDryRun
            ? new[] { "templateId", "manifest", "dryRun" }
            : new[] { "templateId", "manifest" };
        foreach (var property in body.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0)
            {
                error = WorkspaceTemplateRules.UnknownField;
                path = "$." + property.Name;
                return false;
            }
        }

        var hasId = body.TryGetProperty("templateId", out var idElement);
        var hasManifest = body.TryGetProperty("manifest", out var manifestElement);
        if (hasId == hasManifest)
        {
            path = hasId ? "$.manifest" : "$.templateId";
            return false;
        }

        if (hasId)
        {
            if (idElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                path = "$.templateId";
                return false;
            }

            templateId = idElement.GetString()!.Trim();
        }
        else if (manifestElement.ValueKind != JsonValueKind.Object)
        {
            path = "$.manifest";
            return false;
        }
        else
        {
            manifestJson = manifestElement.GetRawText();
        }

        if (allowDryRun && body.TryGetProperty("dryRun", out var dryElement))
        {
            if (dryElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                path = "$.dryRun";
                return false;
            }

            dryRun = dryElement.GetBoolean();
        }

        error = string.Empty;
        return true;
    }

    private static bool TryIdempotencyKey(HttpRequest request, out string key)
    {
        key = string.Empty;
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count != 1)
        {
            return false;
        }

        key = values.ToString().Trim();
        return key.Length is > 0 and <= WorkspaceTemplateRules.MaxIdempotencyKeyLength
            && key.All(c => !char.IsControl(c) && !char.IsWhiteSpace(c));
    }

    private static bool TryReadOnboarding(
        JsonElement body,
        out string? status,
        out OnboardingItemResponse[]? items,
        out string error,
        out string? path)
    {
        status = null;
        items = null;
        error = OnboardingRules.Invalid;
        path = null;
        if (body.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in body.EnumerateObject())
        {
            if (property.Name is not ("status" or "items"))
            {
                error = WorkspaceTemplateRules.UnknownField;
                path = "$." + property.Name;
                return false;
            }
        }

        if (body.TryGetProperty("status", out var statusElement))
        {
            if (statusElement.ValueKind != JsonValueKind.String
                || !OnboardingRules.Statuses.Contains(statusElement.GetString() ?? string.Empty, StringComparer.Ordinal))
            {
                path = "$.status";
                return false;
            }

            status = statusElement.GetString();
        }

        if (body.TryGetProperty("items", out var itemsElement))
        {
            if (itemsElement.ValueKind != JsonValueKind.Array)
            {
                path = "$.items";
                return false;
            }

            var parsed = new List<OnboardingItemResponse>();
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    path = "$.items";
                    return false;
                }

                foreach (var property in item.EnumerateObject())
                {
                    if (property.Name is not ("key" or "state"))
                    {
                        error = WorkspaceTemplateRules.UnknownField;
                        path = "$.items." + property.Name;
                        return false;
                    }
                }

                if (!item.TryGetProperty("key", out var keyElement)
                    || keyElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(keyElement.GetString())
                    || !item.TryGetProperty("state", out var stateElement)
                    || stateElement.ValueKind != JsonValueKind.String
                    || !OnboardingRules.States.Contains(stateElement.GetString() ?? string.Empty, StringComparer.Ordinal))
                {
                    path = "$.items";
                    return false;
                }

                parsed.Add(new OnboardingItemResponse(keyElement.GetString()!.Trim(), stateElement.GetString()!));
            }

            items = parsed.ToArray();
        }

        if (status is null && items is null)
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static TemplateSummaryResponse Summarize(WorkspaceTemplateDocument document, bool builtin) =>
        new(
            document.Id,
            document.Version,
            document.Spaces.Count,
            document.Spaces.Sum(x => x.Channels.Count),
            document.Checklist.ToArray(),
            builtin);

    private static TemplatePlanItemResponse[] ToResponses(
        IReadOnlyList<TemplatePlanItem> items,
        IReadOnlyDictionary<string, Guid> ids) =>
        items.Select(item =>
        {
            var idKey = item.Kind switch
            {
                TemplatePlanKinds.Space => SpaceIdKey(item.Key),
                TemplatePlanKinds.Channel => ChannelIdKey(item.SpaceKey ?? string.Empty, item.Key),
                _ => string.Empty
            };
            Guid? resourceId = ids.TryGetValue(idKey, out var id) ? id : null;
            return new TemplatePlanItemResponse(item.Kind, item.Action, item.Key, item.Name, item.Detail, resourceId);
        }).ToArray();

    private static OnboardingResponse ToOnboarding(WorkspaceOnboarding row) =>
        new(row.Status, row.TemplateId, row.TemplateVersion, ParseItems(row.ItemsJson).ToArray());

    private static OnboardingItemResponse[] DefaultItems() =>
        OnboardingRules.DefaultChecklist.Select(key => new OnboardingItemResponse(key, OnboardingRules.Open)).ToArray();

    private static List<OnboardingItemResponse> ParseItems(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<OnboardingItemResponse>>(json, Json) ?? DefaultItems().ToList();
        }
        catch (JsonException)
        {
            return DefaultItems().ToList();
        }
    }

    private static IResult Fail(int status, string error, string? path)
    {
        object body = string.IsNullOrEmpty(path) ? new { error } : new { error, path };
        return status switch
        {
            StatusCodes.Status404NotFound => Results.NotFound(body),
            StatusCodes.Status409Conflict => Results.Conflict(body),
            _ => Results.BadRequest(body)
        };
    }

    private static string SpaceIdKey(string key) => "space:" + key;

    private static string ChannelIdKey(string spaceKey, string channelKey) => "channel:" + spaceKey + ":" + channelKey;

    private readonly record struct AdminScope(UserProfile Profile, Workspace Workspace);

    private sealed record PlanSnapshot(
        IReadOnlyList<TemplatePlanItem> Items,
        Dictionary<string, Guid> Ids,
        IReadOnlyList<TemplateExistingSpace> Spaces,
        IReadOnlyList<TemplateExistingChannel> Channels,
        TemplateExistingPolicy? Policy);

    private sealed record StructureSnapshot(
        IReadOnlyList<TemplateExistingSpace> Spaces,
        IReadOnlyList<TemplateExistingChannel> Channels,
        TemplateExistingPolicy? Policy);
}
