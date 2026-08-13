using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api;

/// <summary>
/// Shared authZ helpers for Minimal API handlers and <see cref="RequirePermissionFilter"/> (B-174).
/// Tenant is always derived from membership — never from request body alone.
/// </summary>
internal static class RequestAuth
{
    public static async Task<UserProfile> EnsureProfileAsync(
        ClaimsPrincipal principal,
        VibeChatDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Missing subject.");
        var profile = await db.UserProfiles.FirstOrDefaultAsync(x => x.Subject == subject, ct);
        if (profile is not null)
        {
            profile.Email = principal.FindFirstValue(ClaimTypes.Email)
                ?? principal.FindFirstValue("email")
                ?? profile.Email;
            profile.DisplayName = principal.FindFirstValue("name") ?? profile.DisplayName;
            profile.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return profile;
        }

        var email = (principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email")
            ?? string.Empty).Trim();
        var displayName = principal.FindFirstValue("name") ?? subject;

        // B-068: claim pending invite stub created by admin (subject pending:{email}).
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.ToLowerInvariant();
            var pendingSubject = WorkspaceRolePolicies.PendingSubjectForEmail(normalizedEmail);
            var pending = await db.UserProfiles.FirstOrDefaultAsync(
                x => x.Subject == pendingSubject
                    || (x.Email.ToLower() == normalizedEmail && x.Subject.StartsWith("pending:")),
                ct);
            if (pending is not null)
            {
                pending.Subject = subject;
                pending.Email = email;
                pending.DisplayName = string.IsNullOrWhiteSpace(displayName) ? pending.DisplayName : displayName;
                pending.UpdatedAt = clock.UtcNow;
                await db.SaveChangesAsync(ct);
                return pending;
            }
        }

        var userId = Guid.TryParse(principal.FindFirstValue("vibechat_user_id"), out var claimId)
            ? new UserId(claimId)
            : UserId.New();
        profile = new UserProfile
        {
            Id = userId,
            Subject = subject,
            Email = string.IsNullOrWhiteSpace(email) ? $"{subject}@unknown.local" : email,
            DisplayName = displayName,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.UserProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public static async Task BeginRlsUserAsync(
        VibeChatDbContext db,
        ITenantContext tenant,
        UserId userId,
        CancellationToken ct)
    {
        tenant.SetUser(userId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
    }

    public static async Task<Workspace?> ResolveWorkspaceAsync(
        WorkspaceId workspaceId,
        UserId userId,
        VibeChatDbContext db,
        ITenantContext tenant,
        CancellationToken ct)
    {
        await BeginRlsUserAsync(db, tenant, userId, ct);

        var workspace = await db.Workspaces.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == workspaceId, ct);
        if (workspace is null)
        {
            return null;
        }

        var isMember = await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(
            x => x.TenantId == workspace.TenantId
                && x.WorkspaceId == workspace.Id
                && x.UserId == userId,
            ct);
        if (!isMember)
        {
            return null;
        }

        tenant.SetTenant(workspace.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        return workspace;
    }

    public static async Task<Channel?> ResolveChannelAsync(
        ChannelId channelId,
        UserId userId,
        VibeChatDbContext db,
        ITenantContext tenant,
        CancellationToken ct)
    {
        await BeginRlsUserAsync(db, tenant, userId, ct);

        var channel = await db.Channels.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == channelId, ct);
        if (channel is null)
        {
            return null;
        }

        var isWorkspaceMember = await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(
            x => x.TenantId == channel.TenantId
                && x.WorkspaceId == channel.WorkspaceId
                && x.UserId == userId,
            ct);
        if (!isWorkspaceMember)
        {
            return null;
        }

        if (channel.Type is ChannelType.Private or ChannelType.Direct or ChannelType.Group)
        {
            var isChannelMember = await db.ChannelMembers.IgnoreQueryFilters().AnyAsync(
                x => x.TenantId == channel.TenantId
                    && x.ChannelId == channel.Id
                    && x.UserId == userId,
                ct);
            if (!isChannelMember)
            {
                return null;
            }
        }

        tenant.SetTenant(channel.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        return channel;
    }

    /// <summary>
    /// Resolves tenant for admin surfaces without a route workspace/channel id:
    /// any membership where the user holds every required permission.
    /// Prefer <paramref name="preferredWorkspaceId"/> when present (route/query).
    /// </summary>
    public static async Task<TenantId?> ResolveAdminTenantAsync(
        UserId userId,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IReadOnlyList<string> requiredPermissions,
        Guid? preferredWorkspaceId,
        CancellationToken ct)
    {
        await BeginRlsUserAsync(db, tenant, userId, ct);

        if (preferredWorkspaceId is { } wsId && wsId != Guid.Empty)
        {
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(wsId), userId, db, tenant, ct);
            if (workspace is null)
            {
                return null;
            }

            foreach (var permission in requiredPermissions)
            {
                if (!await permissions.HasPermissionAsync(workspace.TenantId, userId, permission, ct))
                {
                    return null;
                }
            }

            return workspace.TenantId;
        }

        var memberships = await db.WorkspaceMembers.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.JoinedAt)
            .ToListAsync(ct);

        foreach (var membership in memberships)
        {
            var ok = true;
            foreach (var permission in requiredPermissions)
            {
                if (!await permissions.HasPermissionAsync(membership.TenantId, userId, permission, ct))
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
            {
                continue;
            }

            tenant.SetTenant(membership.TenantId);
            await RlsSession.EnsureAppliedAsync(db, tenant, ct);
            return membership.TenantId;
        }

        return null;
    }
}
