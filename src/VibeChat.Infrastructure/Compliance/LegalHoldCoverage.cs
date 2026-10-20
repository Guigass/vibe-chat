using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure.Compliance;

public readonly record struct HoldPurgeSplit(IReadOnlyList<Message> Purgeable, int Preserved);

/// <summary>
/// Active holds block purge only inside their scope. Flag-off does not erase
/// holds that already exist: purge still consults them.
/// </summary>
public static class LegalHoldCoverage
{
    public static async Task<HoldPurgeSplit> SplitAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        IReadOnlyList<Message> candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return new HoldPurgeSplit(candidates, 0);
        }

        var holds = await db.LegalHolds.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.Status == LegalHoldStatus.Active)
            .ToListAsync(cancellationToken);
        holds = holds.Where(x => x.ExpiresAt is null || x.ExpiresAt > now).ToList();
        if (holds.Count == 0)
        {
            return new HoldPurgeSplit(candidates, 0);
        }

        var conversationIds = candidates.Select(x => x.ConversationId.Value).ToHashSet();
        var channels = await db.Channels.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new { x.Id, x.WorkspaceId })
            .ToListAsync(cancellationToken);
        var channelById = channels
            .Where(x => conversationIds.Contains(x.Id.Value))
            .ToDictionary(x => x.Id.Value, x => x.WorkspaceId.Value);
        var missing = conversationIds.Where(id => !channelById.ContainsKey(id)).ToArray();
        var threads = missing.Length == 0
            ? []
            : await db.MessageThreads.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && missing.Contains(x.Id))
                .Select(x => new { x.Id, x.ChannelId })
                .ToListAsync(cancellationToken);
        var workspaceByChannel = channels.ToDictionary(x => x.Id.Value, x => x.WorkspaceId.Value);
        var threadById = threads.ToDictionary(x => x.Id, x => x.ChannelId.Value);

        var purgeable = new List<Message>(candidates.Count);
        var preserved = 0;
        foreach (var message in candidates)
        {
            if (!TrySubject(message, channelById, threadById, workspaceByChannel, out var subject))
            {
                preserved++;
                continue;
            }

            var covered = holds.Any(hold => LegalHoldRules.IsCovering(
                hold.Status,
                hold.ExpiresAt,
                hold.WorkspaceId.Value,
                hold.ScopeKind,
                hold.ScopeId,
                hold.IntervalStart,
                hold.IntervalEnd,
                subject,
                now));
            if (covered)
            {
                preserved++;
            }
            else
            {
                purgeable.Add(message);
            }
        }

        return new HoldPurgeSplit(purgeable, preserved);
    }

    private static bool TrySubject(
        Message message,
        IReadOnlyDictionary<Guid, Guid> channelWorkspace,
        IReadOnlyDictionary<Guid, Guid> threadChannel,
        IReadOnlyDictionary<Guid, Guid> workspaceByChannel,
        out HoldSubject subject)
    {
        var conversationId = message.ConversationId.Value;
        if (channelWorkspace.TryGetValue(conversationId, out var workspaceId))
        {
            subject = new HoldSubject(workspaceId, conversationId, conversationId, message.AuthorId.Value, message.CreatedAt);
            return true;
        }

        if (threadChannel.TryGetValue(conversationId, out var channelId)
            && workspaceByChannel.TryGetValue(channelId, out workspaceId))
        {
            subject = new HoldSubject(workspaceId, channelId, conversationId, message.AuthorId.Value, message.CreatedAt);
            return true;
        }

        subject = default;
        return false;
    }
}
