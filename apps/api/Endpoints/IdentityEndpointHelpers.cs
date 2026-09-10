using System.Security.Claims;
using VibeChat.Api;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class IdentityEndpointHelpers
{
    internal static Task<UserProfile> EnsureProfileAsync(ClaimsPrincipal principal, VibeChatDbContext db, IClock clock, CancellationToken ct) =>
        RequestAuth.EnsureProfileAsync(principal, db, clock, ct);

    internal static Task BeginRlsUserAsync(VibeChatDbContext db, ITenantContext tenant, UserId userId, CancellationToken ct) =>
        RequestAuth.BeginRlsUserAsync(db, tenant, userId, ct);

    internal static Task<Workspace?> ResolveWorkspaceAsync(WorkspaceId workspaceId, UserId userId, VibeChatDbContext db, ITenantContext tenant, CancellationToken ct) =>
        RequestAuth.ResolveWorkspaceAsync(workspaceId, userId, db, tenant, ct);

    internal static Task<Channel?> ResolveChannelAsync(ChannelId channelId, UserId userId, VibeChatDbContext db, ITenantContext tenant, CancellationToken ct) =>
        RequestAuth.ResolveChannelAsync(channelId, userId, db, tenant, ct);

    internal static Task<(Workspace? Workspace, bool IsGuest)> ResolveWorkspaceOrGuestAsync(
        WorkspaceId workspaceId,
        UserId userId,
        VibeChatDbContext db,
        ITenantContext tenant,
        CancellationToken ct) =>
        RequestAuth.ResolveWorkspaceOrGuestAsync(workspaceId, userId, db, tenant, ct);
}
