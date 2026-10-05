using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using StackExchange.Redis;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using NpgsqlTypes;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Role = VibeChat.SharedKernel.Role;

namespace VibeChat.Infrastructure;

public sealed class PermissionChecker(VibeChatDbContext dbContext) : IPermissionChecker, IWorkspaceMembershipReader, IChannelMembershipReader
{
    public async Task<bool> HasPermissionAsync(TenantId tenantId, UserId userId, string permission, CancellationToken cancellationToken)
    {
        var roles = await GetRolesAsync(tenantId, userId, cancellationToken);
        return roles.Any(role => RolePermissionCatalog.For(role).Contains(permission));
    }

    public async Task<bool> IsMemberAsync(TenantId tenantId, WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken) =>
        await dbContext.WorkspaceMembers.AnyAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.UserId == userId, cancellationToken);

    /// <summary>
    /// Workspace roles from <c>tenancy.workspace_members</c> (B-176). JWT / <c>ICurrentUser.Roles</c> are not used.
    /// </summary>
    public async Task<IReadOnlyCollection<Role>> GetRolesAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken)
    {
        var roles = await dbContext.WorkspaceMembers.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserId == userId)
            .Select(x => x.Role)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (roles.Length > 0)
        {
            return roles;
        }

        var isGuest = await dbContext.ChannelMembers.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.UserId == userId && x.LeftAt == null,
            cancellationToken);
        return isGuest ? [Role.Guest] : [];
    }

    public async Task<bool> CanAccessAsync(TenantId tenantId, ChannelId channelId, UserId userId, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == channelId, cancellationToken);
        if (channel is null)
        {
            return false;
        }

        var isChannelMember = await dbContext.ChannelMembers.AnyAsync(
            x => x.TenantId == tenantId && x.ChannelId == channelId && x.UserId == userId && x.LeftAt == null,
            cancellationToken);
        if (isChannelMember)
        {
            return true;
        }

        if (channel.Type == ChannelType.Public || channel.Type == ChannelType.Announcement)
        {
            return await IsMemberAsync(tenantId, channel.WorkspaceId, userId, cancellationToken);
        }

        return false;
    }

}
