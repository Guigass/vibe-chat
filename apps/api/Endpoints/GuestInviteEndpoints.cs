using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class GuestInviteEndpoints
{
    internal static void MapGuestInvites(this RouteGroupBuilder v1)
    {
        v1.MapPost("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/invites", CreateInviteAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapGet("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/invites", ListInvitesAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapDelete("/invites/{inviteId:guid}", RevokeInviteAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/invites/{token}/accept", AcceptInviteAsync)
            .AllowPermissionGateExempt("authenticated accept of hashed channel invite (B-040)");
    }

    private static async Task<IResult> CreateInviteAsync(
        Guid workspaceId,
        Guid channelId,
        CreateChannelInviteRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IRateLimiter rateLimiter,
        IOptions<InviteOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound();
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        if (!await rateLimiter.TryAcquireAsync(
                $"invite:create:{profile.Id.Value:N}",
                InvitePolicies.CreatePerMinute,
                TimeSpan.FromMinutes(1),
                ct))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == new ChannelId(channelId) && x.WorkspaceId == workspace.Id, ct);
        if (channel is null || !InvitePolicies.AllowsInvite(channel.Type.ToString()))
        {
            return Results.NotFound();
        }

        string? email = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            email = InvitePolicies.NormalizeEmail(request.Email);
            if (email is null)
            {
                return Results.BadRequest(new { error = "ValidEmailRequired" });
            }

            var existingMember = await (
                from m in db.WorkspaceMembers.AsNoTracking()
                join u in db.UserProfiles.AsNoTracking() on m.UserId equals u.Id
                where m.WorkspaceId == workspace.Id && u.Email.ToLower() == email
                select m.Id).AnyAsync(ct);
            if (existingMember)
            {
                return Results.Conflict(new { error = "AlreadyMember" });
            }
        }

        var raw = InviteToken.CreateRaw();
        var invite = new ChannelInvite
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            ChannelId = channel.Id,
            TokenHash = InviteToken.Hash(raw),
            CreatedByUserId = profile.Id,
            Email = email,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(InvitePolicies.NormalizeExpiryDays(request.ExpiresInDays, options.Value.MaxExpiryDays))
        };
        db.ChannelInvites.Add(invite);
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.GuestInvite,
            EntityType = "ChannelInvite",
            EntityId = invite.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId,
                channelId,
                email,
                expiresAt = invite.ExpiresAt
            })
        });
        await db.SaveChangesAsync(ct);
        return Results.Created(
            $"/api/v1/invites/{invite.Id}",
            new ChannelInviteCreatedResponse(invite.Id, $"/invite/{raw}", invite.ExpiresAt));
    }

    private static async Task<IResult> ListInvitesAsync(
        Guid workspaceId,
        Guid channelId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<InviteOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound();
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == new ChannelId(channelId) && x.WorkspaceId == workspace.Id, ct);
        if (channel is null)
        {
            return Results.NotFound();
        }

        var now = clock.UtcNow;
        var invites = await db.ChannelInvites.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        var workspaceMemberIds = await db.WorkspaceMembers.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => x.UserId)
            .ToListAsync(ct);
        var guests = await (
            from cm in db.ChannelMembers.AsNoTracking()
            join u in db.UserProfiles.AsNoTracking() on cm.UserId equals u.Id
            where cm.ChannelId == channel.Id && !workspaceMemberIds.Contains(cm.UserId)
            orderby cm.JoinedAt
            select new ChannelGuestResponse(u.Id.Value, u.DisplayName, u.Email, cm.JoinedAt)
        ).ToArrayAsync(ct);

        return Results.Ok(new ChannelInvitesPageResponse(
            invites.Select(x => new ChannelInviteResponse(
                x.Id,
                x.Email,
                x.CreatedAt,
                x.ExpiresAt,
                x.AcceptedAt,
                x.AcceptedByUserId?.Value,
                x.RevokedAt,
                InvitePolicies.Status(x, now))).ToArray(),
            guests));
    }

    private static async Task<IResult> RevokeInviteAsync(
        Guid inviteId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IPresenceService presence,
        IHubContext<ChatHub> hub,
        IOptions<InviteOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound();
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        await BeginRlsUserAsync(db, tenant, profile.Id, ct);

        var invite = await db.ChannelInvites.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == inviteId, ct);
        if (invite is null)
        {
            return Results.NotFound();
        }

        var workspace = await ResolveWorkspaceAsync(invite.WorkspaceId, profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        if (invite.RevokedAt is null)
        {
            invite.RevokedAt = clock.UtcNow;
            invite.RevokedByUserId = profile.Id;
        }

        ChannelMember? membership = null;
        if (invite.AcceptedByUserId is { } guestId)
        {
            membership = await db.ChannelMembers
                .FirstOrDefaultAsync(x => x.ChannelId == invite.ChannelId && x.UserId == guestId, ct);
            if (membership is not null && membership.LeftAt is null)
            {
                membership.LeftAt = clock.UtcNow;
            }
        }

        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.GuestRevoke,
            EntityType = "ChannelInvite",
            EntityId = invite.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId = workspace.Id.Value,
                channelId = invite.ChannelId.Value,
                acceptedByUserId = invite.AcceptedByUserId?.Value
            })
        });
        await db.SaveChangesAsync(ct);

        if (invite.AcceptedByUserId is { } revokedUser)
        {
            foreach (var connectionId in await presence.GetConnectionIdsAsync(workspace.TenantId, revokedUser, ct))
            {
                await hub.Groups.RemoveFromGroupAsync(
                    connectionId,
                    ChatHub.ChannelGroup(workspace.TenantId, invite.ChannelId),
                    ct);
                await hub.Clients.Client(connectionId).SendAsync("AccessRevoked", new
                {
                    channelId = invite.ChannelId.Value,
                    workspaceId = workspace.Id.Value
                }, ct);
            }
        }

        return Results.NoContent();
    }

    private static async Task<IResult> AcceptInviteAsync(
        string token,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IRateLimiter rateLimiter,
        IOptions<InviteOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound();
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        await BeginRlsUserAsync(db, tenant, profile.Id, ct);

        if (!await rateLimiter.TryAcquireAsync(
                $"invite:accept:{profile.Id.Value:N}",
                InvitePolicies.AcceptPerMinute,
                TimeSpan.FromMinutes(1),
                ct))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var hash = InviteToken.Hash(token ?? string.Empty);
        await RlsSession.SetInviteTokenHashAsync(db, hash, ct);
        var invite = await db.ChannelInvites.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        var now = clock.UtcNow;
        if (invite is null
            || InvitePolicies.IsUnavailable(invite, now)
            || !InvitePolicies.EmailMatches(invite.Email, profile.Email))
        {
            if (invite is not null && invite.AcceptedAt is null && invite.RevokedAt is null && invite.ExpiresAt <= now)
            {
                audit.Add(new AuditEvent
                {
                    TenantId = invite.TenantId,
                    ActorUserId = profile.Id,
                    Action = AuditActions.GuestExpire,
                    EntityType = "ChannelInvite",
                    EntityId = invite.Id.ToString(),
                    MetadataJson = JsonSerializer.Serialize(new { channelId = invite.ChannelId.Value })
                });
                tenant.SetTenant(invite.TenantId);
                await RlsSession.EnsureAppliedAsync(db, tenant, ct);
                await db.SaveChangesAsync(ct);
            }

            return Results.Json(new { error = "InviteUnavailable" }, statusCode: StatusCodes.Status410Gone);
        }

        tenant.SetTenant(invite.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);

        var alreadyWorkspaceMember = await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == invite.WorkspaceId && x.UserId == profile.Id, ct);
        if (alreadyWorkspaceMember)
        {
            return Results.Conflict(new { error = "AlreadyMember" });
        }

        var existingChannelMember = await db.ChannelMembers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ChannelId == invite.ChannelId && x.UserId == profile.Id, ct);
        if (existingChannelMember is null)
        {
            db.ChannelMembers.Add(new ChannelMember
            {
                Id = Guid.NewGuid(),
                TenantId = invite.TenantId,
                ChannelId = invite.ChannelId,
                UserId = profile.Id,
                JoinedAt = now,
                JoinedSeq = 0
            });
        }
        else if (existingChannelMember.LeftAt is not null)
        {
            existingChannelMember.LeftAt = null;
            existingChannelMember.LeftSeq = null;
            existingChannelMember.JoinedAt = now;
            existingChannelMember.JoinedSeq = 0;
        }

        invite.AcceptedAt = now;
        invite.AcceptedByUserId = profile.Id;

        var channel = await db.Channels.AsNoTracking()
            .FirstAsync(x => x.Id == invite.ChannelId, ct);
        audit.Add(new AuditEvent
        {
            TenantId = invite.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.GuestAccept,
            EntityType = "ChannelInvite",
            EntityId = invite.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId = invite.WorkspaceId.Value,
                channelId = invite.ChannelId.Value
            })
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new AcceptInviteResponse(channel.Id.Value, channel.WorkspaceId.Value, channel.Name));
    }
}
