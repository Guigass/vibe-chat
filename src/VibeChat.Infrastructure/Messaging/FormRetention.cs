using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure.Messaging;

public static class FormRetention
{
    public static async Task<int> PurgeExpiredAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var rows = await db.FormSubmissions.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId
                && !row.AnswersPurged
                && row.RetainUntil != null
                && row.RetainUntil < now)
            .OrderBy(row => row.RetainUntil)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return 0;
        }

        foreach (var row in rows)
        {
            row.AnswersJson = "{}";
            row.AnswersPurged = true;
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = null,
            Action = FormPolicies.AnswersPurged,
            EntityType = "FormSubmission",
            EntityId = rows[0].FormId.ToString(),
            MetadataJson = $"{{\"count\":{rows.Count}}}",
            OccurredAt = now,
        });
        return rows.Count;
    }
}
