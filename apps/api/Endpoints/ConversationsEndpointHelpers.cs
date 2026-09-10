using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class ConversationsEndpointHelpers
{
    internal static string BuildDirectChannelName(UserId left, UserId right)
    {
        var a = left.Value;
        var b = right.Value;
        return a.CompareTo(b) <= 0 ? $"dm:{a:D}:{b:D}" : $"dm:{b:D}:{a:D}";
    }

    internal static async Task<Channel?> FindDirectChannelAsync(WorkspaceId workspaceId, UserId left, UserId right, VibeChatDbContext db, CancellationToken ct)
    {
        var name = BuildDirectChannelName(left, right);
        return await db.Channels.FirstOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Type == ChannelType.Direct && x.Name == name,
            ct);
    }

    internal static async Task<Dictionary<ChannelId, DirectPeerInfo>> ResolveDirectPeersAsync(
        IReadOnlyCollection<Channel> channels,
        UserId currentUserId,
        VibeChatDbContext db,
        CancellationToken ct)
    {
        var directIds = channels.Where(x => x.Type == ChannelType.Direct).Select(x => x.Id).ToArray();
        if (directIds.Length == 0)
        {
            return new Dictionary<ChannelId, DirectPeerInfo>();
        }

        var memberships = await db.ChannelMembers
            .Where(x => directIds.Contains(x.ChannelId) && x.UserId != currentUserId)
            .Select(x => new { x.ChannelId, x.UserId })
            .ToListAsync(ct);

        var peerIds = memberships.Select(x => x.UserId).Distinct().ToArray();
        var profiles = await db.UserProfiles
            .Where(x => peerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var result = new Dictionary<ChannelId, DirectPeerInfo>();
        foreach (var membership in memberships)
        {
            if (!profiles.TryGetValue(membership.UserId, out var displayName))
            {
                continue;
            }

            result[membership.ChannelId] = new DirectPeerInfo(membership.UserId, displayName);
        }

        return result;
    }

    internal static async Task<HashSet<ChannelId>> GuestChannelIdsAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        IReadOnlyCollection<ChannelId> channelIds,
        CancellationToken ct)
    {
        if (channelIds.Count == 0)
        {
            return [];
        }

        var workspaceMemberIds = await db.WorkspaceMembers.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => x.UserId)
            .ToListAsync(ct);
        var guestIds = await db.ChannelMembers.AsNoTracking()
            .Where(x => channelIds.Contains(x.ChannelId) && !workspaceMemberIds.Contains(x.UserId))
            .Select(x => x.ChannelId)
            .Distinct()
            .ToListAsync(ct);
        return guestIds.ToHashSet();
    }
}
