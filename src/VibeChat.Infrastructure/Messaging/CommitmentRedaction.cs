using Microsoft.EntityFrameworkCore;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure.Messaging;

public static class CommitmentRedaction
{
    public static async Task MarkSourceAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        MessageId messageId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Commitments.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId && row.SourceMessageId == messageId && !row.SourceRedacted)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.SourceRedacted = true;
        }
    }

    public static async Task MarkSourcesAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        IReadOnlyCollection<MessageId> messageIds,
        CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0)
        {
            return;
        }

        var rows = await db.Commitments.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId && messageIds.Contains(row.SourceMessageId) && !row.SourceRedacted)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.SourceRedacted = true;
        }
    }
}
