using Microsoft.EntityFrameworkCore;
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
        if (pages.Count == 0)
        {
            return 0;
        }

        var pageIds = pages.Select(row => row.Id).ToList();
        await VibeChat.Infrastructure.SemanticRetention.RemoveSourcesAsync(db, tenantId, SemanticSourceKind.Page, pageIds, cancellationToken);
        var versions = await db.KnowledgePageVersions.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId && pageIds.Contains(row.PageId))
            .ToListAsync(cancellationToken);
        if (versions.Count > 0)
        {
            db.KnowledgePageVersions.RemoveRange(versions);
        }

        db.KnowledgePages.RemoveRange(pages);
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
}
