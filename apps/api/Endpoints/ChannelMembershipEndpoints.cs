using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;
using static VibeChat.Api.Endpoints.MessagingEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class ChannelMembershipEndpoints
{
    internal static void MapChannelMembers(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members", ListMembers);

        v1.MapPost("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members", AddMember)
            .AllowPermissionGateExempt("channel creator or channel.manage (B-186)");

        v1.MapDelete("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members/me", Leave)
            .AllowPermissionGateExempt("membership-only channel leave (B-186)");

        v1.MapDelete("/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members/{userId:guid}", RemoveMember)
            .AllowPermissionGateExempt("channel creator or channel.manage (B-186)");
    }

    private static async Task<IResult> ListMembers(
        Guid workspaceId,
        Guid channelId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IPresenceService presence,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.WorkspaceId != new WorkspaceId(workspaceId))
        {
            return Results.Forbid();
        }

        if (http.Request.Query.ContainsKey("query"))
        {
            return Results.Ok(await AutocompleteAsync(channel, http.Request.Query["query"].ToString(), db, ct));
        }

        if (!TryReadCursor(http.Request.Query["cursor"].ToString(), out var skip))
        {
            return Error(StatusCodes.Status400BadRequest, "InvalidCursor");
        }

        var limit = 50;
        var limitRaw = http.Request.Query["limit"].ToString();
        if (!string.IsNullOrEmpty(limitRaw))
        {
            if (!int.TryParse(limitRaw, out limit) || limit < 1)
            {
                return Error(StatusCodes.Status400BadRequest, "InvalidLimit");
            }

            limit = Math.Min(limit, 200);
        }

        var page = channel.Type is ChannelType.Public or ChannelType.Announcement
            ? await PageWorkspaceAsync(channel, skip, limit, db, ct)
            : await PageChannelAsync(channel, skip, limit, db, ct);

        var statuses = page.Items.Length == 0
            ? new Dictionary<UserId, PresenceStatus>()
            : await presence.GetStatusesAsync(
                channel.TenantId,
                page.Items.Select(item => new UserId(item.UserId)).ToArray(),
                ct);

        var items = page.Items.Select(item => item with
        {
            Presence = statuses.TryGetValue(new UserId(item.UserId), out var status)
                ? status.ToString().ToLowerInvariant()
                : PresenceStatus.Offline.ToString().ToLowerInvariant()
        }).ToArray();

        var canManage = channel.Type == ChannelType.Private
            && await IsStewardAsync(channel, profile.Id, permissions, ct);
        var next = skip + items.Length < page.Total ? WriteCursor(skip + items.Length) : null;
        return Results.Ok(new ChannelMemberPageResponse(items, next, page.Total, canManage));
    }

    private static async Task<IResult> AddMember(
        Guid workspaceId,
        Guid channelId,
        AddChannelMemberRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IAuditWriter audit,
        IOutboxWriter outbox,
        IConversationSequenceStore sequences,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.WorkspaceId != new WorkspaceId(workspaceId))
        {
            return Results.Forbid();
        }

        if (channel.Type != ChannelType.Private)
        {
            return Error(StatusCodes.Status400BadRequest, "ChannelMembershipNotPrivate");
        }

        if (!await IsStewardAsync(channel, profile.Id, permissions, ct))
        {
            return Results.Forbid();
        }

        if (request.UserId == Guid.Empty)
        {
            return Error(StatusCodes.Status400BadRequest, "InvalidUser");
        }

        var targetId = new UserId(request.UserId);
        var inWorkspace = await db.WorkspaceMembers.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == channel.WorkspaceId && x.UserId == targetId,
            ct);
        if (!inWorkspace)
        {
            return Results.Forbid();
        }

        var active = await db.ChannelMembers.AsNoTracking().AnyAsync(
            x => x.ChannelId == channel.Id && x.UserId == targetId,
            ct);
        if (active)
        {
            return Error(StatusCodes.Status409Conflict, "AlreadyChannelMember");
        }

        var now = clock.UtcNow;
        var existing = await db.ChannelMembers.IgnoreQueryFilters().FirstOrDefaultAsync(
            x => x.TenantId == channel.TenantId && x.ChannelId == channel.Id && x.UserId == targetId,
            ct);
        if (existing is null)
        {
            db.ChannelMembers.Add(new ChannelMember
            {
                Id = Guid.NewGuid(),
                TenantId = channel.TenantId,
                ChannelId = channel.Id,
                UserId = targetId,
                JoinedAt = now,
                JoinedSeq = 0
            });
        }
        else
        {
            existing.LeftAt = null;
            existing.LeftSeq = null;
            existing.JoinedAt = now;
            existing.JoinedSeq = 0;
        }

        audit.Add(new AuditEvent
        {
            TenantId = channel.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.ChannelMemberAdd,
            EntityType = "ChannelMember",
            EntityId = channel.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { channelId, userId = request.UserId })
        });
        await AppendMembershipSystemEventAsync(
            db,
            sequences,
            outbox,
            clock,
            channel.TenantId,
            channel.Id,
            profile.Id,
            profile.DisplayName,
            SystemEventTokens.MemberAddBody(request.UserId),
            ct);
        await db.SaveChangesAsync(ct);

        var target = await db.UserProfiles.AsNoTracking().FirstAsync(x => x.Id == targetId, ct);
        return Results.Ok(new ChannelMemberResponse(target.Id.Value, target.DisplayName, target.Email, now, IsGuest: false));
    }

    private static async Task<IResult> RemoveMember(
        Guid workspaceId,
        Guid channelId,
        Guid userId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IAuditWriter audit,
        IOutboxWriter outbox,
        IConversationSequenceStore sequences,
        IPresenceService presence,
        IHubContext<ChatHub> hub,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.WorkspaceId != new WorkspaceId(workspaceId))
        {
            return Results.Forbid();
        }

        if (channel.Type != ChannelType.Private)
        {
            return Error(StatusCodes.Status400BadRequest, "ChannelMembershipNotPrivate");
        }

        if (userId == profile.Id.Value)
        {
            return Error(StatusCodes.Status400BadRequest, "UseChannelLeave");
        }

        if (!await IsStewardAsync(channel, profile.Id, permissions, ct))
        {
            return Results.Forbid();
        }

        var targetId = new UserId(userId);
        var membership = await db.ChannelMembers.FirstOrDefaultAsync(
            x => x.ChannelId == channel.Id && x.UserId == targetId,
            ct);
        if (membership is null)
        {
            return Results.NotFound();
        }

        if (await IsLastStewardAsync(channel, targetId, db, permissions, ct))
        {
            return Error(StatusCodes.Status409Conflict, "LastChannelManager");
        }

        await MarkLeftAsync(db, membership, clock, ct);
        audit.Add(new AuditEvent
        {
            TenantId = channel.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.ChannelMemberRemove,
            EntityType = "ChannelMember",
            EntityId = channel.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { channelId, userId })
        });
        await AppendMembershipSystemEventAsync(
            db,
            sequences,
            outbox,
            clock,
            channel.TenantId,
            channel.Id,
            profile.Id,
            profile.DisplayName,
            SystemEventTokens.MemberRemoveBody(userId),
            ct);
        await db.SaveChangesAsync(ct);
        await EvictAsync(presence, hub, channel.TenantId, channel.Id, targetId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Leave(
        Guid workspaceId,
        Guid channelId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPermissionChecker permissions,
        IAuditWriter audit,
        IOutboxWriter outbox,
        IConversationSequenceStore sequences,
        IPresenceService presence,
        IHubContext<ChatHub> hub,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.WorkspaceId != new WorkspaceId(workspaceId))
        {
            return Results.Forbid();
        }

        if (channel.Type != ChannelType.Private)
        {
            return Error(StatusCodes.Status400BadRequest, "ChannelMembershipNotPrivate");
        }

        var membership = await db.ChannelMembers.FirstOrDefaultAsync(
            x => x.ChannelId == channel.Id && x.UserId == profile.Id,
            ct);
        if (membership is null)
        {
            return Results.Forbid();
        }

        if (await IsLastStewardAsync(channel, profile.Id, db, permissions, ct))
        {
            return Error(StatusCodes.Status409Conflict, "LastChannelManager");
        }

        await MarkLeftAsync(db, membership, clock, ct);
        audit.Add(new AuditEvent
        {
            TenantId = channel.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.ChannelMemberLeave,
            EntityType = "ChannelMember",
            EntityId = channel.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { channelId, userId = profile.Id.Value })
        });
        await AppendMembershipSystemEventAsync(
            db,
            sequences,
            outbox,
            clock,
            channel.TenantId,
            channel.Id,
            profile.Id,
            profile.DisplayName,
            SystemEventTokens.MemberLeaveBody(profile.Id.Value),
            ct);
        await db.SaveChangesAsync(ct);
        await EvictAsync(presence, hub, channel.TenantId, channel.Id, profile.Id, ct);
        return Results.NoContent();
    }

    private static async Task<ChannelMemberResponse[]> AutocompleteAsync(
        Channel channel,
        string query,
        VibeChatDbContext db,
        CancellationToken ct)
    {
        var queryLower = query.Trim().ToLowerInvariant();
        var roster = channel.Type is ChannelType.Public or ChannelType.Announcement
            ? await (
                from m in db.WorkspaceMembers.AsNoTracking()
                where m.WorkspaceId == channel.WorkspaceId
                join u in db.UserProfiles.AsNoTracking() on m.UserId equals u.Id
                select new ChannelMemberResponse(u.Id.Value, u.DisplayName, u.Email)
            ).ToListAsync(ct)
            : [];
        var channelRoster = await (
            from cm in db.ChannelMembers.AsNoTracking()
            where cm.ChannelId == channel.Id
            join u in db.UserProfiles.AsNoTracking() on cm.UserId equals u.Id
            select new ChannelMemberResponse(u.Id.Value, u.DisplayName, u.Email)
        ).ToListAsync(ct);
        var seen = roster.Select(x => x.UserId).ToHashSet();
        foreach (var member in channelRoster)
        {
            if (seen.Add(member.UserId))
            {
                roster.Add(member);
            }
        }

        var members = roster
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(queryLower))
        {
            members = members
                .Where(x =>
                    x.DisplayName.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
                    || x.Email.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToArray();
        }
        else
        {
            members = members.Take(8).ToArray();
        }

        return members;
    }

    private static async Task<(ChannelMemberResponse[] Items, int Total)> PageWorkspaceAsync(
        Channel channel,
        int skip,
        int limit,
        VibeChatDbContext db,
        CancellationToken ct)
    {
        var query =
            from m in db.WorkspaceMembers.AsNoTracking()
            where m.WorkspaceId == channel.WorkspaceId
            join u in db.UserProfiles.AsNoTracking() on m.UserId equals u.Id
            orderby u.DisplayName, u.Id
            select new { UserId = u.Id.Value, u.DisplayName, u.Email, m.JoinedAt };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip(skip).Take(limit).ToListAsync(ct);
        var items = rows
            .Select(row => new ChannelMemberResponse(row.UserId, row.DisplayName, row.Email, row.JoinedAt))
            .ToArray();
        return (items, total);
    }

    private static async Task<(ChannelMemberResponse[] Items, int Total)> PageChannelAsync(
        Channel channel,
        int skip,
        int limit,
        VibeChatDbContext db,
        CancellationToken ct)
    {
        var query =
            from cm in db.ChannelMembers.AsNoTracking()
            where cm.ChannelId == channel.Id
            join u in db.UserProfiles.AsNoTracking() on cm.UserId equals u.Id
            orderby u.DisplayName, u.Id
            select new { UserId = u.Id, DisplayName = u.DisplayName, u.Email, cm.JoinedAt };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip(skip).Take(limit).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return ([], total);
        }

        var ids = rows.Select(row => row.UserId).ToArray();
        var inWorkspace = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == channel.WorkspaceId && ids.Contains(m.UserId))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        var members = inWorkspace.ToHashSet();
        var items = rows
            .Select(row => new ChannelMemberResponse(
                row.UserId.Value,
                row.DisplayName,
                row.Email,
                row.JoinedAt,
                IsGuest: !members.Contains(row.UserId)))
            .ToArray();
        return (items, total);
    }

    private static async Task<bool> IsStewardAsync(
        Channel channel,
        UserId userId,
        IPermissionChecker permissions,
        CancellationToken ct)
    {
        if (channel.CreatedBy == userId)
        {
            return true;
        }

        return await permissions.HasPermissionAsync(channel.TenantId, userId, Permissions.Channel.Manage, ct);
    }

    private static async Task<bool> IsLastStewardAsync(
        Channel channel,
        UserId targetId,
        VibeChatDbContext db,
        IPermissionChecker permissions,
        CancellationToken ct)
    {
        if (!await IsStewardAsync(channel, targetId, permissions, ct))
        {
            return false;
        }

        var memberIds = await db.ChannelMembers.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id)
            .Select(x => x.UserId)
            .ToListAsync(ct);
        var stewards = 0;
        foreach (var memberId in memberIds)
        {
            if (await IsStewardAsync(channel, memberId, permissions, ct))
            {
                stewards++;
                if (stewards > 1)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static async Task MarkLeftAsync(
        VibeChatDbContext db,
        ChannelMember membership,
        IClock clock,
        CancellationToken ct)
    {
        var leaveSeq = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == membership.ChannelId)
            .Select(m => (long?)m.Sequence)
            .MaxAsync(ct) ?? 0;
        membership.LeftAt = clock.UtcNow;
        membership.LeftSeq = leaveSeq;
    }

    private static async Task EvictAsync(
        IPresenceService presence,
        IHubContext<ChatHub> hub,
        TenantId tenantId,
        ChannelId channelId,
        UserId userId,
        CancellationToken ct)
    {
        foreach (var connectionId in await presence.GetConnectionIdsAsync(tenantId, userId, ct))
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, ChatHub.ChannelGroup(tenantId, channelId), ct);
        }
    }

    private static bool TryReadCursor(string raw, out int skip)
    {
        skip = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            if (!text.StartsWith("o:", StringComparison.Ordinal) || !int.TryParse(text[2..], out skip) || skip < 0)
            {
                return false;
            }

            return skip <= 100_000;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string WriteCursor(int skip) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"o:{skip}"));

    private static IResult Error(int status, string code) =>
        Results.Json(new { error = code }, statusCode: status);
}
