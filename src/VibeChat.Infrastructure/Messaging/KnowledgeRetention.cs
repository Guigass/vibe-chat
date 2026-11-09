using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.Messaging;
using VibeChat.Search;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure.Messaging;

public static class KnowledgeRetention
{
    public static async Task<int> PurgeDeletedAsync(
        VibeChat.Infrastructure.VibeChatDbContext db,
        TenantId tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pages = await db.KnowledgePages.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId && row.DeletedAt != null && row.DeletedAt < cutoff)
            .OrderBy(row => row.DeletedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        var heldWorkspaces = await db.LegalHolds.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId
                && row.Status == LegalHoldStatus.Active
                && (row.ExpiresAt == null || row.ExpiresAt > now))
            .Select(row => row.WorkspaceId)
            .ToListAsync(cancellationToken);
        var held = heldWorkspaces.ToHashSet();
        if (pages.Count == 0)
        {
            return await PurgeOrphanCanvasAsync(db, tenantId, held, cancellationToken);
        }

        var pageIds = pages.Select(row => row.Id).ToList();
        var purgeCanvas = pages.Where(row => !held.Contains(row.WorkspaceId)).Select(row => row.Id).ToList();
        var preserved = pages.Count - purgeCanvas.Count;
        if (preserved > 0)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ActorUserId = null,
                Action = AuditActions.CanvasPreserve,
                EntityType = "CanvasDocument",
                EntityId = null,
                MetadataJson = $"{{\"count\":{preserved},\"reason\":\"legal_hold\"}}",
                OccurredAt = now,
            });
        }

        if (purgeCanvas.Count > 0)
        {
            var documents = await db.CanvasDocuments.IgnoreQueryFilters()
                .Where(row => row.TenantId == tenantId && purgeCanvas.Contains(row.PageId))
                .ToListAsync(cancellationToken);
            var updates = await db.CanvasUpdates.IgnoreQueryFilters()
                .Where(row => row.TenantId == tenantId && purgeCanvas.Contains(row.PageId))
                .ToListAsync(cancellationToken);
            var snapshots = await db.CanvasSnapshots.IgnoreQueryFilters()
                .Where(row => row.TenantId == tenantId && purgeCanvas.Contains(row.PageId))
                .ToListAsync(cancellationToken);
            if (documents.Count > 0)
            {
                db.CanvasDocuments.RemoveRange(documents);
            }

            if (updates.Count > 0)
            {
                db.CanvasUpdates.RemoveRange(updates);
            }

            if (snapshots.Count > 0)
            {
                db.CanvasSnapshots.RemoveRange(snapshots);
            }
        }

        await VibeChat.Infrastructure.SemanticRetention.RemoveSourcesAsync(db, tenantId, SemanticSourceKind.Page, pageIds, cancellationToken);
        var versions = await db.KnowledgePageVersions.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId && pageIds.Contains(row.PageId))
            .ToListAsync(cancellationToken);
        if (versions.Count > 0)
        {
            db.KnowledgePageVersions.RemoveRange(versions);
        }

        db.KnowledgePages.RemoveRange(pages);
        await PurgeOrphanCanvasAsync(db, tenantId, held, cancellationToken);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = null,
            Action = AuditActions.KnowledgePageDelete,
            EntityType = "KnowledgePage",
            EntityId = null,
            MetadataJson = $"{{\"count\":{pages.Count},\"versions\":{versions.Count},\"cutoff\":\"{cutoff:O}\"}}",
            OccurredAt = now,
        });
        return pages.Count;
    }

    private static async Task<int> PurgeOrphanCanvasAsync(
        VibeChat.Infrastructure.VibeChatDbContext db,
        TenantId tenantId,
        HashSet<WorkspaceId> held,
        CancellationToken cancellationToken)
    {
        var livePageIds = await db.KnowledgePages.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        var live = livePageIds.ToHashSet();
        var documents = await db.CanvasDocuments.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var orphanIds = documents
            .Where(row => !live.Contains(row.PageId) && !held.Contains(row.WorkspaceId))
            .Select(row => row.PageId)
            .ToList();
        if (orphanIds.Count == 0)
        {
            return 0;
        }

        var updates = await db.CanvasUpdates.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId && orphanIds.Contains(row.PageId))
            .ToListAsync(cancellationToken);
        var snapshots = await db.CanvasSnapshots.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId && orphanIds.Contains(row.PageId))
            .ToListAsync(cancellationToken);
        db.CanvasDocuments.RemoveRange(documents.Where(row => orphanIds.Contains(row.PageId)));
        if (updates.Count > 0)
        {
            db.CanvasUpdates.RemoveRange(updates);
        }

        if (snapshots.Count > 0)
        {
            db.CanvasSnapshots.RemoveRange(snapshots);
        }

        return orphanIds.Count;
    }
}
