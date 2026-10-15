using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Microsoft.Extensions.Options;

namespace VibeChat.Api.Endpoints;

internal static class KnowledgeEndpoints
{
    internal static void MapKnowledge(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/knowledge/collections", ListCollectionsAsync)
            .RequirePermission(Permissions.Knowledge.View);
        v1.MapPost("/workspaces/{workspaceId:guid}/knowledge/collections", CreateCollectionAsync)
            .RequirePermission(Permissions.Knowledge.Edit);
        v1.MapDelete("/workspaces/{workspaceId:guid}/knowledge/collections/{collectionId:guid}", DeleteCollectionAsync)
            .RequirePermission(Permissions.Knowledge.Manage);
        v1.MapGet("/workspaces/{workspaceId:guid}/knowledge/pages", ListPagesAsync)
            .RequirePermission(Permissions.Knowledge.View);
        v1.MapPost("/workspaces/{workspaceId:guid}/knowledge/pages", CreatePageAsync)
            .RequirePermission(Permissions.Knowledge.Edit);
        v1.MapGet("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}", GetPageAsync)
            .RequirePermission(Permissions.Knowledge.View);
        v1.MapPatch("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}", UpdatePageAsync)
            .RequirePermission(Permissions.Knowledge.Edit);
        v1.MapDelete("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}", DeletePageAsync)
            .RequirePermission(Permissions.Knowledge.Manage);
        v1.MapPost("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/restore", RestorePageAsync)
            .RequirePermission(Permissions.Knowledge.Manage);
        v1.MapGet("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/history", HistoryAsync)
            .RequirePermission(Permissions.Knowledge.View);
        v1.MapGet("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/export", ExportPageAsync)
            .RequirePermission(Permissions.Knowledge.View);
        v1.MapGet("/workspaces/{workspaceId:guid}/knowledge/search", SearchAsync)
            .RequirePermission(Permissions.Knowledge.View);
        v1.MapGet("/workspaces/{workspaceId:guid}/knowledge/export", ExportWorkspaceAsync)
            .RequirePermission(Permissions.Knowledge.Manage);
    }

    private static async Task<IResult> ListCollectionsAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null || gate.Workspace is null || gate.Profile is null)
        {
            return gate.Error ?? Results.Forbid();
        }

        var workspace = gate.Workspace;
        var profile = gate.Profile;
        var rows = await db.KnowledgeCollections.AsNoTracking()
            .Where(row => row.WorkspaceId == workspace.Id && row.DeletedAt == null)
            .OrderBy(row => row.Title)
            .Take(KnowledgePolicies.MaxCollectionsPerWorkspace)
            .ToListAsync(ct);
        var visible = new List<KnowledgeCollectionResponse>();
        foreach (var row in rows)
        {
            if (await CanViewScopeAsync(db, workspace, profile.Id, row, ct))
            {
                visible.Add(ToCollection(row));
            }
        }

        var canEdit = await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Knowledge.Edit, ct);
        var canManage = await permissions.HasPermissionAsync(workspace.TenantId, profile.Id, Permissions.Knowledge.Manage, ct);
        return Results.Ok(new KnowledgeCollectionListResponse(visible, canEdit, canManage));
    }

    private static async Task<IResult> CreateCollectionAsync(
        Guid workspaceId,
        CreateKnowledgeCollectionRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var key = KnowledgePolicies.Normalize(request.IdempotencyKey);
        if (!KnowledgePolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        var title = KnowledgePolicies.Normalize(request.Title);
        if (!KnowledgePolicies.IsValidTitle(title))
        {
            return Bad("InvalidTitle");
        }

        if (!KnowledgePolicies.TryParseScope(request.Scope, out var scope))
        {
            return Bad("InvalidKnowledgeScope");
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null || gate.Workspace is null || gate.Profile is null)
        {
            return gate.Error ?? Results.Forbid();
        }

        var workspace = gate.Workspace;
        var profile = gate.Profile;
        var existing = await db.KnowledgeCollections.FirstOrDefaultAsync(
            row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            return await CollectionResultAsync(db, workspace, profile.Id, existing, ct);
        }

        var scopeId = scope switch
        {
            KnowledgeScopeKind.Workspace => workspace.Id.Value,
            _ => request.ScopeId ?? Guid.Empty,
        };
        if (!await ScopeExistsAsync(db, workspace, profile.Id, scope, scopeId, ct))
        {
            return Results.Forbid();
        }

        if (request.ParentId is Guid parentId)
        {
            var parent = await db.KnowledgeCollections.FirstOrDefaultAsync(
                row => row.Id == parentId && row.WorkspaceId == workspace.Id && row.DeletedAt == null, ct);
            if (parent is null || parent.ParentId is not null || parent.ScopeKind != scope || parent.ScopeId != scopeId
                || !await CanViewScopeAsync(db, workspace, profile.Id, parent, ct))
            {
                return Bad("InvalidKnowledgeParent");
            }
        }

        var count = await db.KnowledgeCollections.CountAsync(
            row => row.WorkspaceId == workspace.Id && row.DeletedAt == null, ct);
        if (count >= KnowledgePolicies.MaxCollectionsPerWorkspace)
        {
            return Bad("KnowledgeQuotaExceeded");
        }

        var now = clock.UtcNow;
        var collection = new KnowledgeCollection
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            ScopeKind = scope,
            ScopeId = scopeId,
            Title = title,
            Slug = await UniqueCollectionSlugAsync(db, workspace.Id, scope, scopeId, title, ct),
            ParentId = request.ParentId,
            CreatedBy = profile.Id,
            IdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.KnowledgeCollections.Add(collection);
        audit.Add(AuditFor(collection.TenantId, profile.Id, AuditActions.KnowledgeCollectionCreate, "KnowledgeCollection", collection.Id, now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.KnowledgeCollections.FirstOrDefaultAsync(
                row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
            if (raced is null)
            {
                throw;
            }

            return await CollectionResultAsync(db, workspace, profile.Id, raced, ct);
        }

        return Results.Ok(ToCollection(collection));
    }

    private static async Task<IResult> DeleteCollectionAsync(
        Guid workspaceId,
        Guid collectionId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var collection = await db.KnowledgeCollections.FirstOrDefaultAsync(
            row => row.Id == collectionId && row.WorkspaceId == gate.Workspace!.Id, ct);
        if (collection is null || collection.DeletedAt is not null
            || !await CanViewScopeAsync(db, gate.Workspace!, gate.Profile!.Id, collection, ct))
        {
            return Results.NotFound();
        }

        var now = clock.UtcNow;
        collection.DeletedAt = now;
        collection.UpdatedAt = now;
        var pages = await db.KnowledgePages
            .Where(row => row.CollectionId == collection.Id && row.DeletedAt == null)
            .ToListAsync(ct);
        foreach (var page in pages)
        {
            page.DeletedAt = now;
            page.UpdatedAt = now;
            page.UpdatedBy = gate.Profile.Id;
            page.Version += 1;
            AddVersion(db, page, gate.Profile.Id, KnowledgePolicies.ActionDeleted, null, now);
        }

        audit.Add(AuditFor(collection.TenantId, gate.Profile.Id, AuditActions.KnowledgeCollectionDelete, "KnowledgeCollection", collection.Id, now));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListPagesAsync(
        Guid workspaceId,
        Guid collectionId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var opened = await OpenCollectionAsync(workspaceId, collectionId, http, db, tenant, clock, ct);
        if (opened.Error is not null)
        {
            return opened.Error;
        }

        var items = await db.KnowledgePages.AsNoTracking()
            .Where(row => row.CollectionId == collectionId && row.DeletedAt == null)
            .OrderBy(row => row.Title)
            .Take(KnowledgePolicies.MaxPagesPerCollection)
            .Select(row => new KnowledgePageSummaryResponse(row.Id, row.CollectionId, row.Title, row.Slug, row.Version, row.UpdatedAt))
            .ToListAsync(ct);
        return Results.Ok(new KnowledgePageListResponse(items));
    }

    private static async Task<IResult> CreatePageAsync(
        Guid workspaceId,
        CreateKnowledgePageRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var key = KnowledgePolicies.Normalize(request.IdempotencyKey);
        var title = KnowledgePolicies.Normalize(request.Title);
        var body = request.Body ?? string.Empty;
        if (!KnowledgePolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        if (!KnowledgePolicies.IsValidTitle(title))
        {
            return Bad("InvalidTitle");
        }

        if (!KnowledgePolicies.IsSafeBody(body))
        {
            return Bad("UnsafeKnowledgeBody");
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null || gate.Workspace is null || gate.Profile is null)
        {
            return gate.Error ?? Results.Forbid();
        }

        var workspace = gate.Workspace;
        var profile = gate.Profile;
        var existing = await db.KnowledgePages.FirstOrDefaultAsync(
            row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            return await PageResultAsync(db, workspace, profile.Id, existing, includeDeleted: false, ct);
        }

        var collection = await db.KnowledgeCollections.FirstOrDefaultAsync(
            row => row.Id == request.CollectionId && row.WorkspaceId == workspace.Id && row.DeletedAt == null, ct);
        if (collection is null || !await CanViewScopeAsync(db, workspace, profile.Id, collection, ct))
        {
            return Results.Forbid();
        }

        var count = await db.KnowledgePages.CountAsync(
            row => row.CollectionId == collection.Id && row.DeletedAt == null, ct);
        if (count >= KnowledgePolicies.MaxPagesPerCollection)
        {
            return Bad("KnowledgeQuotaExceeded");
        }

        var now = clock.UtcNow;
        var page = new KnowledgePage
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            CollectionId = collection.Id,
            Title = title,
            Slug = await UniquePageSlugAsync(db, collection.Id, title, ct),
            Body = body,
            Version = 1,
            CreatedBy = profile.Id,
            UpdatedBy = profile.Id,
            IdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.KnowledgePages.Add(page);
        AddVersion(db, page, profile.Id, KnowledgePolicies.ActionCreated, null, now);
        AddAudit(audit, page, profile.Id, AuditActions.KnowledgePageCreate, now);
        AddOutbox(outbox, page, collection, KnowledgePolicies.ActionCreated, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.KnowledgePages.FirstOrDefaultAsync(
                row => row.CreatedBy == profile.Id && row.IdempotencyKey == key, ct);
            if (raced is null)
            {
                throw;
            }

            return await PageResultAsync(db, workspace, profile.Id, raced, includeDeleted: false, ct);
        }

        return Results.Ok(await ToPageAsync(db, page, profile.Id, ct));
    }

    private static async Task<IResult> GetPageAsync(
        Guid workspaceId,
        Guid pageId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadVisiblePageAsync(workspaceId, pageId, http, db, tenant, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        return Results.Ok(await ToPageAsync(db, loaded.Page!, loaded.Profile!.Id, ct));
    }

    private static async Task<IResult> UpdatePageAsync(
        Guid workspaceId,
        Guid pageId,
        UpdateKnowledgePageRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var title = KnowledgePolicies.Normalize(request.Title);
        var body = request.Body ?? string.Empty;
        if (!KnowledgePolicies.IsValidTitle(title) || !KnowledgePolicies.IsSafeBody(body))
        {
            return Bad(!KnowledgePolicies.IsValidTitle(title) ? "InvalidTitle" : "UnsafeKnowledgeBody");
        }

        var loaded = await LoadVisiblePageAsync(workspaceId, pageId, http, db, tenant, permissions: null, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        var page = loaded.Page!;
        if (page.DeletedAt is not null)
        {
            return Results.NotFound();
        }

        if (page.Version != request.Version)
        {
            return Conflict(page.Version);
        }

        var now = clock.UtcNow;
        page.Title = title;
        page.Body = body;
        page.Version += 1;
        page.UpdatedBy = loaded.Profile!.Id;
        page.UpdatedAt = now;
        AddVersion(db, page, loaded.Profile.Id, KnowledgePolicies.ActionUpdated, null, now);
        AddAudit(audit, page, loaded.Profile.Id, AuditActions.KnowledgePageUpdate, now);
        AddOutbox(outbox, page, loaded.Collection!, KnowledgePolicies.ActionUpdated, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.KnowledgePages.AsNoTracking().Where(row => row.Id == page.Id).Select(row => row.Version).FirstAsync(ct);
            return Conflict(current);
        }

        return Results.Ok(await ToPageAsync(db, page, loaded.Profile.Id, ct));
    }

    private static async Task<IResult> DeletePageAsync(
        Guid workspaceId,
        Guid pageId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadVisiblePageAsync(workspaceId, pageId, http, db, tenant, permissions: null, clock, ct);
        if (loaded.Error is not null || loaded.Page!.DeletedAt is not null)
        {
            return loaded.Error ?? Results.NotFound();
        }

        var now = clock.UtcNow;
        var page = loaded.Page;
        page.DeletedAt = now;
        page.UpdatedAt = now;
        page.UpdatedBy = loaded.Profile!.Id;
        page.Version += 1;
        AddVersion(db, page, loaded.Profile.Id, KnowledgePolicies.ActionDeleted, null, now);
        AddAudit(audit, page, loaded.Profile.Id, AuditActions.KnowledgePageDelete, now);
        AddOutbox(outbox, page, loaded.Collection!, KnowledgePolicies.ActionDeleted, now);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestorePageAsync(
        Guid workspaceId,
        Guid pageId,
        RestoreKnowledgePageRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadVisiblePageAsync(workspaceId, pageId, http, db, tenant, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        var page = loaded.Page!;
        if (page.Version != request.ExpectedVersion)
        {
            return Conflict(page.Version);
        }

        var source = await db.KnowledgePageVersions.AsNoTracking().FirstOrDefaultAsync(
            row => row.PageId == page.Id && row.Version == request.SourceVersion, ct);
        if (source is null)
        {
            return Results.NotFound();
        }

        var now = clock.UtcNow;
        page.Title = source.Title;
        page.Body = source.Body;
        page.DeletedAt = null;
        page.Version += 1;
        page.UpdatedBy = loaded.Profile!.Id;
        page.UpdatedAt = now;
        AddVersion(db, page, loaded.Profile.Id, KnowledgePolicies.ActionRestored, source.Version, now);
        AddAudit(audit, page, loaded.Profile.Id, AuditActions.KnowledgePageRestore, now);
        AddOutbox(outbox, page, loaded.Collection!, KnowledgePolicies.ActionRestored, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.KnowledgePages.AsNoTracking().Where(row => row.Id == page.Id).Select(row => row.Version).FirstAsync(ct);
            return Conflict(current);
        }

        return Results.Ok(await ToPageAsync(db, page, loaded.Profile.Id, ct));
    }

    private static async Task<IResult> HistoryAsync(
        Guid workspaceId,
        Guid pageId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadVisiblePageAsync(workspaceId, pageId, http, db, tenant, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        var rows = await db.KnowledgePageVersions.AsNoTracking()
            .Where(row => row.PageId == pageId)
            .OrderByDescending(row => row.Version)
            .ToListAsync(ct);
        var items = rows.Select(row => new KnowledgeVersionResponse(
            row.Version, row.Title, row.Body, row.Action, row.ActorUserId.Value, row.RestoredFromVersion, row.At)).ToList();
        return Results.Ok(new KnowledgeHistoryResponse(items));
    }

    private static async Task<IResult> ExportPageAsync(
        Guid workspaceId,
        Guid pageId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var loaded = await LoadVisiblePageAsync(workspaceId, pageId, http, db, tenant, permissions, clock, ct);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }

        var versionRows = await db.KnowledgePageVersions.AsNoTracking()
            .Where(row => row.PageId == pageId)
            .OrderBy(row => row.Version)
            .ToListAsync(ct);
        var versions = versionRows.Select(row => new KnowledgeVersionResponse(
            row.Version, row.Title, row.Body, row.Action, row.ActorUserId.Value, row.RestoredFromVersion, row.At)).ToList();
        audit.Add(AuditFor(loaded.Page!.TenantId, loaded.Profile!.Id, AuditActions.KnowledgeExport, "KnowledgePage", pageId, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new KnowledgeExportResponse(await ToPageAsync(db, loaded.Page, loaded.Profile.Id, ct), versions));
    }

    private static async Task<IResult> SearchAsync(
        Guid workspaceId,
        string? q,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var term = new string((q ?? string.Empty).Where(ch => ch is not '%' and not '_' and not '\\').ToArray()).Trim();
        if (term.Length < KnowledgePolicies.MinQueryLength || term.Length > KnowledgePolicies.MaxQueryLength)
        {
            return Bad("InvalidKnowledgeQuery");
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var collections = await db.KnowledgeCollections.AsNoTracking()
            .Where(row => row.WorkspaceId == gate.Workspace!.Id && row.DeletedAt == null)
            .ToListAsync(ct);
        var visibleIds = new List<Guid>();
        foreach (var collection in collections)
        {
            if (await CanViewScopeAsync(db, gate.Workspace!, gate.Profile!.Id, collection, ct))
            {
                visibleIds.Add(collection.Id);
            }
        }

        if (visibleIds.Count == 0)
        {
            return Results.Ok(new KnowledgeSearchResponse([]));
        }

        var needle = term.ToLowerInvariant();
        var items = await db.KnowledgePages.AsNoTracking()
            .Where(row => row.WorkspaceId == gate.Workspace!.Id
                && row.DeletedAt == null
                && visibleIds.Contains(row.CollectionId)
                && (row.Title.ToLower().Contains(needle) || row.Body.ToLower().Contains(needle)))
            .OrderByDescending(row => row.UpdatedAt)
            .Take(KnowledgePolicies.MaxPageSize)
            .Select(row => new KnowledgePageSummaryResponse(row.Id, row.CollectionId, row.Title, row.Slug, row.Version, row.UpdatedAt))
            .ToListAsync(ct);
        return Results.Ok(new KnowledgeSearchResponse(items));
    }

    private static async Task<IResult> ExportWorkspaceAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var collections = await db.KnowledgeCollections.AsNoTracking()
            .Where(row => row.WorkspaceId == gate.Workspace!.Id && row.DeletedAt == null)
            .ToListAsync(ct);
        var visible = new List<Guid>();
        foreach (var collection in collections)
        {
            if (await CanViewScopeAsync(db, gate.Workspace!, gate.Profile!.Id, collection, ct))
            {
                visible.Add(collection.Id);
            }
        }

        if (visible.Count == 0)
        {
            audit.Add(AuditFor(gate.Workspace!.TenantId, gate.Profile!.Id, AuditActions.KnowledgeExport, "Workspace", gate.Workspace.Id.Value, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { pageCount = 0, versions = Array.Empty<KnowledgeVersionResponse>() });
        }

        var pages = await db.KnowledgePages.AsNoTracking()
            .Where(row => visible.Contains(row.CollectionId))
            .Select(row => row.Id)
            .ToListAsync(ct);
        var versionRows = await db.KnowledgePageVersions.AsNoTracking()
            .Where(row => pages.Contains(row.PageId))
            .OrderBy(row => row.PageId)
            .ThenBy(row => row.Version)
            .ToListAsync(ct);
        var versions = versionRows.Select(row => new KnowledgeVersionResponse(
            row.Version, row.Title, row.Body, row.Action, row.ActorUserId.Value, row.RestoredFromVersion, row.At)).ToList();
        audit.Add(AuditFor(gate.Workspace!.TenantId, gate.Profile!.Id, AuditActions.KnowledgeExport, "Workspace", gate.Workspace.Id.Value, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { pageCount = pages.Count, versions });
    }

    private static async Task<PageLoad> LoadVisiblePageAsync(
        Guid workspaceId,
        Guid pageId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker? permissions,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return new PageLoad(null, null, null, gate.Error);
        }

        var page = await db.KnowledgePages.FirstOrDefaultAsync(
            row => row.Id == pageId && row.WorkspaceId == gate.Workspace!.Id, ct);
        if (page is null)
        {
            return new PageLoad(null, null, null, Results.NotFound());
        }

        var collection = await db.KnowledgeCollections.FirstOrDefaultAsync(row => row.Id == page.CollectionId, ct);
        if (collection is null || !await CanViewScopeAsync(db, gate.Workspace!, gate.Profile!.Id, collection, ct))
        {
            return new PageLoad(null, null, null, Results.NotFound());
        }

        if (page.DeletedAt is not null)
        {
            var canManage = permissions is not null
                && await permissions.HasPermissionAsync(gate.Workspace!.TenantId, gate.Profile.Id, Permissions.Knowledge.Manage, ct);
            if (!canManage)
            {
                return new PageLoad(null, null, null, Results.NotFound());
            }
        }

        return new PageLoad(page, collection, gate.Profile, null);
    }

    private static async Task<CollectionLoad> OpenCollectionAsync(
        Guid workspaceId,
        Guid collectionId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await OpenWorkspaceAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate.Error is not null)
        {
            return new CollectionLoad(null, gate.Error);
        }

        var collection = await db.KnowledgeCollections.AsNoTracking().FirstOrDefaultAsync(
            row => row.Id == collectionId && row.WorkspaceId == gate.Workspace!.Id && row.DeletedAt == null, ct);
        if (collection is null || !await CanViewScopeAsync(db, gate.Workspace!, gate.Profile!.Id, collection, ct))
        {
            return new CollectionLoad(null, Results.NotFound());
        }

        return new CollectionLoad(collection, null);
    }

    private static async Task<WorkspaceGate> OpenWorkspaceAsync(
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
            return new WorkspaceGate(null, null, Results.Forbid());
        }

        return new WorkspaceGate(workspace, profile, null);
    }

    private static async Task<bool> ScopeExistsAsync(
        VibeChatDbContext db,
        Workspace workspace,
        UserId userId,
        KnowledgeScopeKind scope,
        Guid scopeId,
        CancellationToken ct)
    {
        switch (scope)
        {
            case KnowledgeScopeKind.Workspace:
                return scopeId == workspace.Id.Value;
            case KnowledgeScopeKind.Space:
                return await db.Spaces.AsNoTracking().AnyAsync(
                    row => row.Id == scopeId && row.WorkspaceId == workspace.Id, ct);
            case KnowledgeScopeKind.Channel:
                var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(
                    row => row.Id == new ChannelId(scopeId) && row.WorkspaceId == workspace.Id, ct);
                return channel is not null && await CanReadChannelAsync(db, channel, userId, ct);
            default:
                return false;
        }
    }

    private static async Task<bool> CanViewScopeAsync(
        VibeChatDbContext db,
        Workspace workspace,
        UserId userId,
        KnowledgeCollection collection,
        CancellationToken ct)
    {
        if (collection.WorkspaceId != workspace.Id || collection.TenantId != workspace.TenantId)
        {
            return false;
        }

        if (collection.ScopeKind != KnowledgeScopeKind.Channel)
        {
            return true;
        }

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(
            row => row.Id == new ChannelId(collection.ScopeId) && row.WorkspaceId == workspace.Id, ct);
        return channel is not null && await CanReadChannelAsync(db, channel, userId, ct);
    }

    private static async Task<bool> CanReadChannelAsync(
        VibeChatDbContext db,
        Channel channel,
        UserId userId,
        CancellationToken ct)
    {
        var isWorkspaceMember = await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == channel.TenantId
                && row.WorkspaceId == channel.WorkspaceId
                && row.UserId == userId, ct);
        var isChannelMember = await db.ChannelMembers.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == channel.TenantId
                && row.ChannelId == channel.Id
                && row.UserId == userId
                && row.LeftAt == null, ct);
        if (!isWorkspaceMember && !isChannelMember)
        {
            return false;
        }

        return !channel.Type.RequiresChannelMembership() || isChannelMember;
    }

    private static async Task<string> UniqueCollectionSlugAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        KnowledgeScopeKind scope,
        Guid scopeId,
        string title,
        CancellationToken ct)
    {
        var root = KnowledgePolicies.Slugify(title);
        var slug = root;
        for (var n = 2; n < 50; n++)
        {
            var taken = await db.KnowledgeCollections.AnyAsync(
                row => row.WorkspaceId == workspaceId
                    && row.ScopeKind == scope
                    && row.ScopeId == scopeId
                    && row.Slug == slug
                    && row.DeletedAt == null, ct);
            if (!taken)
            {
                return slug;
            }

            var suffix = "-" + n;
            slug = root.Length + suffix.Length > KnowledgePolicies.MaxSlugLength
                ? root[..(KnowledgePolicies.MaxSlugLength - suffix.Length)] + suffix
                : root + suffix;
        }

        return root + "-" + Guid.NewGuid().ToString("N")[..6];
    }

    private static async Task<string> UniquePageSlugAsync(
        VibeChatDbContext db,
        Guid collectionId,
        string title,
        CancellationToken ct)
    {
        var root = KnowledgePolicies.Slugify(title);
        var slug = root;
        for (var n = 2; n < 50; n++)
        {
            var taken = await db.KnowledgePages.AnyAsync(
                row => row.CollectionId == collectionId && row.Slug == slug && row.DeletedAt == null, ct);
            if (!taken)
            {
                return slug;
            }

            var suffix = "-" + n;
            slug = root.Length + suffix.Length > KnowledgePolicies.MaxSlugLength
                ? root[..(KnowledgePolicies.MaxSlugLength - suffix.Length)] + suffix
                : root + suffix;
        }

        return root + "-" + Guid.NewGuid().ToString("N")[..6];
    }

    private static async Task<KnowledgePageResponse> ToPageAsync(
        VibeChatDbContext db,
        KnowledgePage page,
        UserId viewer,
        CancellationToken ct)
    {
        var references = new List<KnowledgeReferenceResponse>();
        foreach (var token in KnowledgePolicies.ParseReferences(page.Body))
        {
            references.Add(await ResolveReferenceAsync(db, page.TenantId, viewer, token, ct));
        }

        return new KnowledgePageResponse(
            page.Id,
            page.CollectionId,
            page.Title,
            page.Slug,
            page.Body,
            page.Version,
            page.CreatedBy.Value,
            page.UpdatedBy.Value,
            page.CreatedAt,
            page.UpdatedAt,
            page.DeletedAt is not null,
            references);
    }

    private static async Task<KnowledgeReferenceResponse> ResolveReferenceAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        UserId viewer,
        KnowledgeReferenceToken token,
        CancellationToken ct)
    {
        switch (token.Kind)
        {
            case KnowledgePolicies.RefMessage:
                var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(
                    row => row.Id == new MessageId(token.TargetId) && row.TenantId == tenantId, ct);
                if (message is null || message.DeletedAt is not null)
                {
                    return Redacted(token);
                }

                var messageChannelId = message.ConversationId;
                if (message.ThreadId is Guid threadId)
                {
                    var thread = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(row => row.Id == threadId, ct);
                    if (thread is null)
                    {
                        return Redacted(token);
                    }

                    messageChannelId = thread.ChannelId;
                }

                var messageChannel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(row => row.Id == messageChannelId, ct);
                if (messageChannel is null || !await CanReadChannelAsync(db, messageChannel, viewer, ct))
                {
                    return Redacted(token);
                }

                return new KnowledgeReferenceResponse(token.Kind, token.TargetId, KnowledgePolicies.RefVisible, null, messageChannel.Id.Value);
            case KnowledgePolicies.RefTask:
            {
                var taskItem = await db.MessageTasks.AsNoTracking().FirstOrDefaultAsync(
                    row => row.Id == token.TargetId && row.TenantId == tenantId, ct);
                if (taskItem is null || taskItem.SourceRedacted)
                {
                    return Redacted(token);
                }

                var taskChannel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(row => row.Id == taskItem.ChannelId, ct);
                var taskCanRead = taskChannel is not null && await CanReadChannelAsync(db, taskChannel, viewer, ct);
                var taskAssignee = await db.MessageTaskAssignees.AsNoTracking()
                    .AnyAsync(row => row.TaskId == taskItem.Id && row.UserId == viewer, ct);
                if (!TaskPolicies.CanSee(taskItem, viewer, taskCanRead, taskAssignee))
                {
                    return Redacted(token);
                }

                return new KnowledgeReferenceResponse(
                    token.Kind, token.TargetId, KnowledgePolicies.RefVisible, taskItem.Title, taskItem.ChannelId.Value);
            }
            case KnowledgePolicies.RefDecision:
                var wanted = CommitmentKind.Decision;
                var commitment = await db.Commitments.AsNoTracking().FirstOrDefaultAsync(
                    row => row.Id == token.TargetId && row.TenantId == tenantId && row.Kind == wanted, ct);
                if (commitment is null)
                {
                    return Redacted(token);
                }

                var commitmentChannel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(row => row.Id == commitment.ChannelId, ct);
                var canRead = commitmentChannel is not null && await CanReadChannelAsync(db, commitmentChannel, viewer, ct);
                if (!CommitmentPolicies.CanSee(commitment, viewer, canRead) || commitment.SourceRedacted)
                {
                    return Redacted(token);
                }

                return new KnowledgeReferenceResponse(
                    token.Kind, token.TargetId, KnowledgePolicies.RefVisible, commitment.Title, commitment.ChannelId.Value);
            case KnowledgePolicies.RefAttachment:
                var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(
                    row => row.Id == token.TargetId && row.TenantId == tenantId, ct);
                if (attachment is null || attachment.MessageId is null)
                {
                    return Redacted(token);
                }

                var attachmentChannel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(row => row.Id == attachment.ChannelId, ct);
                var source = await db.Messages.AsNoTracking().FirstOrDefaultAsync(row => row.Id == attachment.MessageId, ct);
                if (attachmentChannel is null || source is null || source.DeletedAt is not null
                    || !await CanReadChannelAsync(db, attachmentChannel, viewer, ct))
                {
                    return Redacted(token);
                }

                return new KnowledgeReferenceResponse(
                    token.Kind, token.TargetId, KnowledgePolicies.RefVisible, attachment.FileName, attachment.ChannelId.Value);
            default:
                return Redacted(token);
        }
    }

    private static KnowledgeReferenceResponse Redacted(KnowledgeReferenceToken token) =>
        new(token.Kind, token.TargetId, KnowledgePolicies.RefRedacted, null, null);

    private static async Task<IResult> CollectionResultAsync(
        VibeChatDbContext db,
        Workspace workspace,
        UserId userId,
        KnowledgeCollection collection,
        CancellationToken ct)
    {
        if (!await CanViewScopeAsync(db, workspace, userId, collection, ct) || collection.DeletedAt is not null)
        {
            return Results.Forbid();
        }

        return Results.Ok(ToCollection(collection));
    }

    private static async Task<IResult> PageResultAsync(
        VibeChatDbContext db,
        Workspace workspace,
        UserId userId,
        KnowledgePage page,
        bool includeDeleted,
        CancellationToken ct)
    {
        var collection = await db.KnowledgeCollections.AsNoTracking().FirstOrDefaultAsync(row => row.Id == page.CollectionId, ct);
        if (collection is null || !await CanViewScopeAsync(db, workspace, userId, collection, ct))
        {
            return Results.Forbid();
        }

        if (page.DeletedAt is not null && !includeDeleted)
        {
            return Results.NotFound();
        }

        return Results.Ok(await ToPageAsync(db, page, userId, ct));
    }

    private static KnowledgeCollectionResponse ToCollection(KnowledgeCollection row) =>
        new(row.Id, row.Title, row.Slug, KnowledgePolicies.ScopeToken(row.ScopeKind), row.ScopeId, row.ParentId, row.UpdatedAt);

    private static void AddVersion(
        VibeChatDbContext db,
        KnowledgePage page,
        UserId actor,
        string action,
        int? restoredFrom,
        DateTimeOffset at)
    {
        db.KnowledgePageVersions.Add(new KnowledgePageVersion
        {
            Id = Guid.NewGuid(),
            TenantId = page.TenantId,
            PageId = page.Id,
            Version = page.Version,
            Title = page.Title,
            Body = page.Body,
            ActorUserId = actor,
            Action = action,
            RestoredFromVersion = restoredFrom,
            At = at,
        });
    }

    private static void AddAudit(IAuditWriter audit, KnowledgePage page, UserId actor, string action, DateTimeOffset at) =>
        audit.Add(AuditFor(page.TenantId, actor, action, "KnowledgePage", page.Id, at));

    private static AuditEvent AuditFor(
        TenantId tenantId,
        UserId actor,
        string action,
        string entityType,
        Guid entityId,
        DateTimeOffset at) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            MetadataJson = "{}",
            OccurredAt = at,
        };

    private static void AddOutbox(
        IOutboxWriter outbox,
        KnowledgePage page,
        KnowledgeCollection collection,
        string action,
        DateTimeOffset at)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = page.TenantId,
            Type = KnowledgePolicies.ChangedEvent,
            OccurredAt = at,
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = page.TenantId.Value,
                workspaceId = page.WorkspaceId.Value,
                pageId = page.Id,
                collectionId = page.CollectionId,
                version = page.Version,
                action,
                channelId = collection.ScopeKind == KnowledgeScopeKind.Channel ? collection.ScopeId : (Guid?)null,
            }),
        });
    }

    private static IResult Disabled() => Results.NotFound(new { error = "KnowledgeDisabled" });

    private static IResult Bad(string code) => Results.BadRequest(new { error = code });

    private static IResult Conflict(int version) =>
        Results.Json(new { error = "KnowledgeVersionConflict", currentVersion = version }, statusCode: StatusCodes.Status409Conflict);

    private readonly record struct WorkspaceGate(Workspace? Workspace, UserProfile? Profile, IResult? Error);

    private readonly record struct CollectionLoad(KnowledgeCollection? Collection, IResult? Error);

    private readonly record struct PageLoad(KnowledgePage? Page, KnowledgeCollection? Collection, UserProfile? Profile, IResult? Error);
}
