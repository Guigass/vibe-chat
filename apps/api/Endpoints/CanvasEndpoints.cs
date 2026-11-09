using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Infrastructure.Messaging;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Microsoft.Extensions.Options;

namespace VibeChat.Api.Endpoints;

internal static class CanvasEndpoints
{
    internal static void MapCanvas(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/canvas", GetAsync)
            .RequirePermission(Permissions.Knowledge.View);
        v1.MapGet("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/canvas/updates", ListUpdatesAsync)
            .RequirePermission(Permissions.Knowledge.View);
        v1.MapPost("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/canvas/updates", PostUpdateAsync)
            .RequirePermission(Permissions.Knowledge.Edit);
        v1.MapPost("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/canvas/presence", PresenceAsync)
            .RequirePermission(Permissions.Knowledge.View);
        v1.MapPost("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/canvas/snapshots", SnapshotAsync)
            .RequirePermission(Permissions.Knowledge.Edit);
        v1.MapPost("/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/canvas/restore", RestoreAsync)
            .RequirePermission(Permissions.Knowledge.Manage);
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        Guid pageId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<KnowledgePageOptions> pages,
        IOptions<CanvasOptions> canvas,
        IClock clock,
        CanvasPresence presence,
        CancellationToken ct)
    {
        var blocked = Flags(pages.Value, canvas.Value);
        if (blocked is not null)
        {
            return blocked;
        }

        var loaded = await LoadPageAsync(workspaceId, pageId, http, db, tenant, clock, ct);
        if (loaded.Error is not null || loaded.Page is null || loaded.Profile is null)
        {
            return loaded.Error ?? Results.NotFound();
        }

        var document = await EnsureAsync(db, loaded.Page, loaded.Profile.Id, clock.UtcNow, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(db, ct);
            await RlsSession.EnsureAppliedAsync(db, tenant, ct);
            document = await db.CanvasDocuments.FirstAsync(row => row.PageId == loaded.Page.Id, ct);
        }

        return Results.Ok(await StateAsync(db, document, loaded.Page.Body, presence, clock.UtcNow, ct));
    }

    private static async Task<IResult> ListUpdatesAsync(
        Guid workspaceId,
        Guid pageId,
        long afterSeq,
        int epoch,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<KnowledgePageOptions> pages,
        IOptions<CanvasOptions> canvas,
        IClock clock,
        CancellationToken ct)
    {
        var blocked = Flags(pages.Value, canvas.Value);
        if (blocked is not null)
        {
            return blocked;
        }

        var loaded = await LoadPageAsync(workspaceId, pageId, http, db, tenant, clock, ct);
        if (loaded.Error is not null || loaded.Page is null)
        {
            return loaded.Error ?? Results.NotFound();
        }

        var document = await db.CanvasDocuments.AsNoTracking().FirstOrDefaultAsync(row => row.PageId == loaded.Page.Id, ct);
        if (document is null || epoch != document.Epoch || afterSeq < document.CompactedThroughSeq)
        {
            return Conflict("CanvasResync");
        }

        var rows = await db.CanvasUpdates.AsNoTracking()
            .Where(row => row.PageId == loaded.Page.Id && row.Epoch == document.Epoch && row.Seq > afterSeq)
            .OrderBy(row => row.Seq)
            .Take(50)
            .ToListAsync(ct);
        var items = rows.Select(row => new CanvasUpdateItemResponse(
            row.Seq, row.Epoch, row.AuthorUserId.Value, Convert.ToBase64String(row.Payload))).ToList();
        return Results.Ok(new CanvasUpdateListResponse(document.Epoch, document.CompactedThroughSeq, items));
    }

    private static async Task<IResult> PostUpdateAsync(
        Guid workspaceId,
        Guid pageId,
        PostCanvasUpdateRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> pages,
        IOptions<CanvasOptions> canvas,
        IClock clock,
        CancellationToken ct)
    {
        var blocked = Flags(pages.Value, canvas.Value);
        if (blocked is not null)
        {
            return blocked;
        }

        if (request.WireVersion != CanvasPolicies.WireVersion)
        {
            return Bad("CanvasWireUnsupported");
        }

        var key = KnowledgePolicies.Normalize(request.IdempotencyKey);
        if (!KnowledgePolicies.IsValidIdempotencyKey(key))
        {
            return Bad("InvalidIdempotencyKey");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(request.UpdateBase64 ?? string.Empty);
        }
        catch (FormatException)
        {
            return Bad("CanvasUpdateRejected");
        }

        if (payload.Length == 0 || payload.Length > CanvasPolicies.MaxUpdateBytes)
        {
            return Bad(payload.Length == 0 ? "CanvasUpdateRejected" : "CanvasQuotaExceeded");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            db.ChangeTracker.Clear();
            await RlsSession.EnsureAppliedAsync(db, tenant, ct);
            var loaded = await LoadPageAsync(workspaceId, pageId, http, db, tenant, clock, ct);
            if (loaded.Error is not null || loaded.Page is null || loaded.Profile is null || loaded.Collection is null)
            {
                return loaded.Error ?? Results.NotFound();
            }

            var page = loaded.Page;
            var now = clock.UtcNow;
            var document = await EnsureAsync(db, page, loaded.Profile.Id, now, ct);
            var replay = await db.CanvasUpdates.AsNoTracking().FirstOrDefaultAsync(
                row => row.PageId == page.Id && row.Epoch == document.Epoch && row.AuthorUserId == loaded.Profile.Id && row.IdempotencyKey == key, ct);
            if (replay is not null)
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(Write(document, page.Body, replayed: true));
            }

            if (request.Epoch != document.Epoch)
            {
                return Conflict("CanvasEpochMismatch");
            }

            var live = await db.CanvasUpdates.CountAsync(row => row.PageId == page.Id && row.Epoch == document.Epoch, ct);
            if (live >= CanvasPolicies.MaxLiveUpdates)
            {
                return Bad("CanvasQuotaExceeded");
            }

            if (!CanvasCrdt.TryApply(document.State, payload, out var next, out var text))
            {
                return Bad("CanvasUpdateRejected");
            }

            if (!KnowledgePolicies.IsSafeBody(text))
            {
                return Bad("UnsafeKnowledgeBody");
            }

            if (next.Length > CanvasPolicies.MaxStateBytes)
            {
                return Bad("CanvasStateTooLarge");
            }

            document.Seq += 1;
            document.State = next;
            document.UpdatedBy = loaded.Profile.Id;
            document.UpdatedAt = now;
            page.Body = text;
            page.UpdatedBy = loaded.Profile.Id;
            page.UpdatedAt = now;
            db.CanvasUpdates.Add(new CanvasUpdate
            {
                Id = Guid.NewGuid(),
                TenantId = page.TenantId,
                PageId = page.Id,
                Epoch = document.Epoch,
                Seq = document.Seq,
                AuthorUserId = loaded.Profile.Id,
                IdempotencyKey = key,
                Payload = payload,
                CreatedAt = now,
            });
            AddAudit(audit, page, loaded.Profile.Id, AuditActions.CanvasUpdate, now, document.Seq, document.Epoch, payload.Length);
            AddOutbox(outbox, page, loaded.Collection, document, "updated", now);
            if (document.Seq % CanvasPolicies.CompactEvery == 0)
            {
                await AddSnapshotAsync(db, audit, document, page, loaded.Profile.Id, text, now, ct);
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(Write(document, text, replayed: false));
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                await RollbackAsync(db, ct);
            }
        }

        return Conflict("CanvasEpochMismatch");
    }

    private static async Task<IResult> PresenceAsync(
        Guid workspaceId,
        Guid pageId,
        PostCanvasPresenceRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<KnowledgePageOptions> pages,
        IOptions<CanvasOptions> canvas,
        IClock clock,
        CanvasPresence presence,
        CancellationToken ct)
    {
        var blocked = Flags(pages.Value, canvas.Value);
        if (blocked is not null)
        {
            return blocked;
        }

        var loaded = await LoadPageAsync(workspaceId, pageId, http, db, tenant, clock, ct);
        if (loaded.Error is not null || loaded.Page is null || loaded.Profile is null)
        {
            return loaded.Error ?? Results.NotFound();
        }

        var cursor = Math.Clamp(request.Cursor, 0, KnowledgePolicies.MaxBodyLength);
        presence.Touch(
            loaded.Page.Id,
            loaded.Profile.Id.Value,
            CanvasPolicies.Label(loaded.Profile.DisplayName, loaded.Profile.Id.Value),
            cursor,
            clock.UtcNow);
        return Results.NoContent();
    }

    private static async Task<IResult> SnapshotAsync(
        Guid workspaceId,
        Guid pageId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> pages,
        IOptions<CanvasOptions> canvas,
        IClock clock,
        CancellationToken ct)
    {
        var blocked = Flags(pages.Value, canvas.Value);
        if (blocked is not null)
        {
            return blocked;
        }

        var loaded = await LoadPageAsync(workspaceId, pageId, http, db, tenant, clock, ct);
        if (loaded.Error is not null || loaded.Page is null || loaded.Profile is null)
        {
            return loaded.Error ?? Results.NotFound();
        }

        var now = clock.UtcNow;
        var document = await EnsureAsync(db, loaded.Page, loaded.Profile.Id, now, ct);
        var text = CanvasCrdt.Read(document.State);
        var snapshot = await AddSnapshotAsync(db, audit, document, loaded.Page, loaded.Profile.Id, text, now, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CanvasSnapshotResponse(snapshot.Id, snapshot.Seq, snapshot.Epoch, snapshot.CreatedAt));
    }

    private static async Task<IResult> RestoreAsync(
        Guid workspaceId,
        Guid pageId,
        PostCanvasRestoreRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IOptions<KnowledgePageOptions> pages,
        IOptions<CanvasOptions> canvas,
        IClock clock,
        CancellationToken ct)
    {
        var blocked = Flags(pages.Value, canvas.Value);
        if (blocked is not null)
        {
            return blocked;
        }

        var loaded = await LoadPageAsync(workspaceId, pageId, http, db, tenant, clock, ct);
        if (loaded.Error is not null || loaded.Page is null || loaded.Profile is null || loaded.Collection is null)
        {
            return loaded.Error ?? Results.NotFound();
        }

        var document = await db.CanvasDocuments.FirstOrDefaultAsync(row => row.PageId == loaded.Page.Id, ct);
        var snapshot = document is null
            ? null
            : await db.CanvasSnapshots.FirstOrDefaultAsync(row => row.Id == request.SnapshotId && row.PageId == loaded.Page.Id, ct);
        if (document is null || snapshot is null)
        {
            return Results.NotFound();
        }

        var now = clock.UtcNow;
        var held = await HeldAsync(db, loaded.Page.TenantId, loaded.Page.WorkspaceId, now, ct);
        if (!held)
        {
            var stale = await db.CanvasUpdates.Where(row => row.PageId == loaded.Page.Id).ToListAsync(ct);
            if (stale.Count > 0)
            {
                db.CanvasUpdates.RemoveRange(stale);
            }
        }

        document.Epoch += 1;
        document.State = snapshot.State;
        document.Seq = snapshot.Seq;
        document.CompactedThroughSeq = snapshot.Seq;
        document.UpdatedBy = loaded.Profile.Id;
        document.UpdatedAt = now;
        loaded.Page.Body = snapshot.Body;
        loaded.Page.Version += 1;
        loaded.Page.UpdatedBy = loaded.Profile.Id;
        loaded.Page.UpdatedAt = now;
        db.KnowledgePageVersions.Add(new KnowledgePageVersion
        {
            Id = Guid.NewGuid(),
            TenantId = loaded.Page.TenantId,
            PageId = loaded.Page.Id,
            Version = loaded.Page.Version,
            Title = loaded.Page.Title,
            Body = snapshot.Body,
            ActorUserId = loaded.Profile.Id,
            Action = CanvasPolicies.ActionRestored,
            RestoredFromVersion = snapshot.PageVersion,
            At = now,
        });
        AddAudit(audit, loaded.Page, loaded.Profile.Id, AuditActions.CanvasRestore, now, document.Seq, document.Epoch, snapshot.State.Length);
        AddOutbox(outbox, loaded.Page, loaded.Collection, document, "restored", now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(Write(document, snapshot.Body, replayed: false));
    }

    internal static async Task<CanvasExportSummary?> ExportSummaryAsync(VibeChatDbContext db, Guid pageId, CancellationToken ct)
    {
        var document = await db.CanvasDocuments.AsNoTracking().FirstOrDefaultAsync(row => row.PageId == pageId, ct);
        if (document is null)
        {
            return null;
        }

        var updates = await db.CanvasUpdates.AsNoTracking().CountAsync(row => row.PageId == pageId, ct);
        var snapshots = await db.CanvasSnapshots.AsNoTracking()
            .Where(row => row.PageId == pageId)
            .OrderBy(row => row.CreatedAt)
            .Select(row => row.Id)
            .ToListAsync(ct);
        return new CanvasExportSummary(document.Epoch, document.Seq, updates, snapshots, Sha(document.State));
    }

    private static async Task<CanvasDocument> EnsureAsync(
        VibeChatDbContext db,
        KnowledgePage page,
        UserId actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var document = await db.CanvasDocuments.FirstOrDefaultAsync(row => row.PageId == page.Id, ct);
        if (document is not null)
        {
            return document;
        }

        document = new CanvasDocument
        {
            PageId = page.Id,
            TenantId = page.TenantId,
            WorkspaceId = page.WorkspaceId,
            Epoch = 1,
            Seq = 0,
            CompactedThroughSeq = 0,
            State = CanvasCrdt.Create(page.Body),
            UpdatedBy = actor,
            UpdatedAt = now,
        };
        db.CanvasDocuments.Add(document);
        return document;
    }

    private static async Task<CanvasSnapshot> AddSnapshotAsync(
        VibeChatDbContext db,
        IAuditWriter audit,
        CanvasDocument document,
        KnowledgePage page,
        UserId actor,
        string text,
        DateTimeOffset now,
        CancellationToken ct)
    {
        page.Version += 1;
        page.Body = text;
        page.UpdatedBy = actor;
        page.UpdatedAt = now;
        db.KnowledgePageVersions.Add(new KnowledgePageVersion
        {
            Id = Guid.NewGuid(),
            TenantId = page.TenantId,
            PageId = page.Id,
            Version = page.Version,
            Title = page.Title,
            Body = text,
            ActorUserId = actor,
            Action = CanvasPolicies.ActionSnapshot,
            At = now,
        });
        var snapshot = new CanvasSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = page.TenantId,
            PageId = page.Id,
            Epoch = document.Epoch,
            Seq = document.Seq,
            PageVersion = page.Version,
            State = document.State,
            Body = text,
            ActorUserId = actor,
            CreatedAt = now,
        };
        db.CanvasSnapshots.Add(snapshot);
        var held = await HeldAsync(db, page.TenantId, page.WorkspaceId, now, ct);
        if (!held)
        {
            var covered = await db.CanvasUpdates
                .Where(row => row.PageId == page.Id && row.Epoch == document.Epoch && row.Seq < document.Seq)
                .ToListAsync(ct);
            if (covered.Count > 0)
            {
                db.CanvasUpdates.RemoveRange(covered);
            }

            document.CompactedThroughSeq = document.Seq == 0 ? 0 : document.Seq - 1;
            var old = await db.CanvasSnapshots
                .Where(row => row.PageId == page.Id)
                .OrderByDescending(row => row.CreatedAt)
                .Skip(CanvasPolicies.MaxSnapshots - 1)
                .ToListAsync(ct);
            if (old.Count > 0)
            {
                db.CanvasSnapshots.RemoveRange(old);
            }
        }

        AddAudit(audit, page, actor, AuditActions.CanvasSnapshot, now, document.Seq, document.Epoch, document.State.Length);
        return snapshot;
    }

    private static async Task<CanvasStateResponse> StateAsync(
        VibeChatDbContext db,
        CanvasDocument document,
        string body,
        CanvasPresence presence,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var snapshots = await db.CanvasSnapshots.AsNoTracking()
            .Where(row => row.PageId == document.PageId)
            .OrderByDescending(row => row.CreatedAt)
            .Take(CanvasPolicies.MaxSnapshots)
            .Select(row => new CanvasSnapshotResponse(row.Id, row.Seq, row.Epoch, row.CreatedAt))
            .ToListAsync(ct);
        var peers = presence.List(document.PageId, now)
            .Select(peer => new CanvasPeerResponse(peer.UserId, peer.Label, peer.Cursor))
            .ToList();
        return new CanvasStateResponse(
            CanvasPolicies.WireVersion,
            document.Epoch,
            document.Seq,
            document.CompactedThroughSeq,
            Convert.ToBase64String(document.State),
            body,
            peers,
            snapshots);
    }

    private static async Task<bool> HeldAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        WorkspaceId workspaceId,
        DateTimeOffset now,
        CancellationToken ct) =>
        await db.LegalHolds.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == tenantId
                && row.WorkspaceId == workspaceId
                && row.Status == LegalHoldStatus.Active
                && (row.ExpiresAt == null || row.ExpiresAt > now),
            ct);

    private static async Task<PageLoad> LoadPageAsync(
        Guid workspaceId,
        Guid pageId,
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
            return new PageLoad(null, null, null, Results.Forbid());
        }

        var page = await db.KnowledgePages.FirstOrDefaultAsync(row => row.Id == pageId && row.WorkspaceId == workspace.Id, ct);
        if (page is null || page.DeletedAt is not null)
        {
            return new PageLoad(null, null, null, Results.NotFound());
        }

        var collection = await db.KnowledgeCollections.FirstOrDefaultAsync(row => row.Id == page.CollectionId && row.DeletedAt == null, ct);
        if (collection is null || !await CanViewAsync(db, workspace, profile.Id, collection, ct))
        {
            return new PageLoad(null, null, null, Results.NotFound());
        }

        return new PageLoad(page, collection, profile, null);
    }

    private static async Task<bool> CanViewAsync(
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
        if (channel is null)
        {
            return false;
        }

        var inWorkspace = await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == channel.TenantId && row.WorkspaceId == channel.WorkspaceId && row.UserId == userId, ct);
        var inChannel = await db.ChannelMembers.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == channel.TenantId && row.ChannelId == channel.Id && row.UserId == userId && row.LeftAt == null, ct);
        if (!inWorkspace && !inChannel)
        {
            return false;
        }

        return !channel.Type.RequiresChannelMembership() || inChannel;
    }

    private static void AddOutbox(
        IOutboxWriter outbox,
        KnowledgePage page,
        KnowledgeCollection collection,
        CanvasDocument document,
        string action,
        DateTimeOffset at)
    {
        outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = page.TenantId,
            Type = CanvasPolicies.ChangedEvent,
            OccurredAt = at,
            Payload = JsonSerializer.Serialize(new
            {
                tenantId = page.TenantId.Value,
                workspaceId = page.WorkspaceId.Value,
                pageId = page.Id,
                collectionId = page.CollectionId,
                epoch = document.Epoch,
                seq = document.Seq,
                action,
                channelId = collection.ScopeKind == KnowledgeScopeKind.Channel ? collection.ScopeId : (Guid?)null,
            }),
        });
    }

    private static void AddAudit(
        IAuditWriter audit,
        KnowledgePage page,
        UserId actor,
        string action,
        DateTimeOffset at,
        long seq,
        int epoch,
        int bytes) =>
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = page.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "CanvasDocument",
            EntityId = page.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { seq, epoch, bytes }),
            OccurredAt = at,
        });

    private static CanvasWriteResponse Write(CanvasDocument document, string body, bool replayed) =>
        new(document.Epoch, document.Seq, body, Convert.ToBase64String(document.State), replayed);

    private static string Sha(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static IResult? Flags(KnowledgePageOptions pages, CanvasOptions canvas)
    {
        if (!pages.Enabled)
        {
            return Results.NotFound(new { error = "KnowledgeDisabled" });
        }

        if (!canvas.Enabled)
        {
            return Results.NotFound(new { error = "CanvasRealtimeDisabled" });
        }

        return null;
    }

    private static IResult Bad(string code) => Results.BadRequest(new { error = code });

    private static IResult Conflict(string code) =>
        Results.Json(new { error = code }, statusCode: StatusCodes.Status409Conflict);

    private static async Task RollbackAsync(VibeChatDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        if (db.Database.CurrentTransaction is not null)
        {
            await db.Database.RollbackTransactionAsync(ct);
        }
    }

    private readonly record struct PageLoad(KnowledgePage? Page, KnowledgeCollection? Collection, UserProfile? Profile, IResult? Error);
}
