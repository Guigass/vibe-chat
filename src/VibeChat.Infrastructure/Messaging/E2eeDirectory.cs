using Microsoft.EntityFrameworkCore;
using VibeChat.Conversations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public static class E2eeDirectory
{
    public static async Task<List<E2eeDevice>> ActiveDevicesAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        ChannelId channelId,
        CancellationToken cancellationToken)
    {
        var members = db.ChannelMembers
            .Where(x => x.ChannelId == channelId)
            .Select(x => x.UserId);
        return await db.E2eeDevices
            .Where(x => x.TenantId == tenantId && x.RevokedAt == null && members.Contains(x.UserId))
            .ToListAsync(cancellationToken);
    }

    public static async Task BumpChannelsAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        IReadOnlyCollection<ChannelId> channelIds,
        CancellationToken cancellationToken)
    {
        if (channelIds.Count == 0)
        {
            return;
        }

        var rows = await db.Channels
            .Where(x => x.TenantId == tenantId
                && channelIds.Contains(x.Id)
                && x.Type == ChannelType.ConfidentialE2ee)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.E2eeGeneration++;
        }
    }

    public static async Task BumpForUserAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var ids = await db.ChannelMembers
            .Where(x => x.UserId == userId)
            .Select(x => x.ChannelId)
            .ToListAsync(cancellationToken);
        await BumpChannelsAsync(db, tenantId, ids, cancellationToken);
    }
}
