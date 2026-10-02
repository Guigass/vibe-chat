using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Notifications;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class UserStatusEndpoints
{
    internal static void MapUserStatus(this RouteGroupBuilder v1)
    {
        v1.MapGet("/me/status", GetMineAsync).RequirePermission(Permissions.Message.Read);

        v1.MapPut("/me/status", SetMineAsync).RequirePermission(Permissions.Message.Read);

        v1.MapDelete("/me/status", ClearMineAsync).RequirePermission(Permissions.Message.Read);

        v1.MapGet("/workspaces/{workspaceId:guid}/availability", ListAvailabilityAsync)
            .RequirePermission(Permissions.Message.Read);

        v1.MapDelete("/workspaces/{workspaceId:guid}/members/{userId:guid}/status", ClearMemberAsync)
            .RequirePermission(Permissions.Workspace.Admin);

        v1.MapPost("/workspaces/{workspaceId:guid}/members/{userId:guid}/status/report", ReportAsync)
            .RequirePermission(Permissions.Message.Read);

        v1.MapGet("/me/availability/calendar", GetCalendarAsync).RequirePermission(Permissions.Message.Read);
    }

    private static async Task<IResult> GetMineAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPresenceService presence,
        IClock clock,
        IHubContext<ChatHub> hub,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        await BeginRlsUserAsync(db, tenant, profile.Id, ct);
        if (!tenant.HasTenant)
        {
            return Results.Forbid();
        }

        await PurgeExpiredAsync(db, tenant.TenantId, presence, clock, hub, logs, ct);
        var row = await db.UserStatuses.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
        var response = await BuildOneAsync(db, tenant.TenantId, profile.Id, row, presence, clock, ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> SetMineAsync(
        SetUserStatusRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPresenceService presence,
        IClock clock,
        IHubContext<ChatHub> hub,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        await BeginRlsUserAsync(db, tenant, profile.Id, ct);
        if (!tenant.HasTenant)
        {
            return Results.Forbid();
        }

        if (!UserStatusRules.TryNormalize(
                request.State,
                request.Emoji,
                request.Text,
                out var state,
                out var emoji,
                out var text,
                out var error))
        {
            return Results.BadRequest(new { error });
        }

        var pref = await db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
        var now = clock.UtcNow;
        var expiresAt = UserStatusRules.ResolveExpiresAt(
            request.ExpiresAt,
            request.ClearAtEndOfDay,
            pref?.TimeZone,
            now,
            out error);
        if (error.Length > 0)
        {
            return Results.BadRequest(new { error });
        }

        var row = await db.UserStatuses.FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
        if (row is null)
        {
            row = new UserStatus
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                UserId = profile.Id
            };
            db.UserStatuses.Add(row);
        }

        row.State = state;
        row.Emoji = emoji;
        row.Text = text;
        row.ClearAtEndOfDay = request.ClearAtEndOfDay;
        row.ExpiresAt = expiresAt;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        logs.CreateLogger("VibeChat.UserStatus").LogInformation(
            "User status updated for {UserId} in tenant {TenantId}",
            profile.Id.Value,
            tenant.TenantId.Value);

        var response = await BuildOneAsync(db, tenant.TenantId, profile.Id, row, presence, clock, ct);
        await PublishAsync(hub, logs, tenant.TenantId, profile.Id.Value, response, ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> ClearMineAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPresenceService presence,
        IClock clock,
        IHubContext<ChatHub> hub,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        await BeginRlsUserAsync(db, tenant, profile.Id, ct);
        if (!tenant.HasTenant)
        {
            return Results.Forbid();
        }

        var row = await db.UserStatuses.FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
        if (row is not null)
        {
            db.UserStatuses.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        var response = await BuildOneAsync(db, tenant.TenantId, profile.Id, status: null, presence, clock, ct);
        await PublishAsync(hub, logs, tenant.TenantId, profile.Id.Value, response, ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> ListAvailabilityAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IPresenceService presence,
        IClock clock,
        IHubContext<ChatHub> hub,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        await PurgeExpiredAsync(db, workspace.TenantId, presence, clock, hub, logs, ct);
        var memberIds = await db.WorkspaceMembers.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => x.UserId)
            .ToListAsync(ct);
        var statuses = await db.UserStatuses.AsNoTracking()
            .Where(x => memberIds.Contains(x.UserId))
            .ToListAsync(ct);
        var byUser = statuses.ToDictionary(x => x.UserId);
        var presenceMap = await presence.GetStatusesAsync(workspace.TenantId, memberIds, ct);
        var prefs = await db.NotificationPreferences.AsNoTracking()
            .Where(x => memberIds.Contains(x.UserId))
            .ToListAsync(ct);
        var prefByUser = prefs.ToDictionary(x => x.UserId);
        var now = clock.UtcNow;
        var response = memberIds.Select(id =>
        {
            byUser.TryGetValue(id, out var status);
            prefByUser.TryGetValue(id, out var pref);
            presenceMap.TryGetValue(id, out var presenceStatus);
            return ToResponse(id, presenceStatus, pref, status, now);
        }).ToArray();
        return Results.Ok(response);
    }

    private static async Task<IResult> ClearMemberAsync(
        Guid workspaceId,
        Guid userId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IPresenceService presence,
        IClock clock,
        IHubContext<ChatHub> hub,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var target = new UserId(userId);
        var isMember = await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.UserId == target, ct);
        if (!isMember)
        {
            return Results.NotFound();
        }

        var row = await db.UserStatuses.FirstOrDefaultAsync(x => x.UserId == target, ct);
        if (row is not null)
        {
            db.UserStatuses.Remove(row);
        }

        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = profile.Id,
            Action = AuditActions.UserStatusClear,
            EntityType = "UserStatus",
            EntityId = target.Value.ToString(),
            MetadataJson = "{}"
        });
        await db.SaveChangesAsync(ct);
        logs.CreateLogger("VibeChat.UserStatus").LogInformation(
            "Admin cleared user status {TargetUserId} in tenant {TenantId}",
            target.Value,
            workspace.TenantId.Value);

        var response = await BuildOneAsync(db, workspace.TenantId, target, status: null, presence, clock, ct);
        await PublishAsync(hub, logs, workspace.TenantId, target.Value, response, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReportAsync(
        Guid workspaceId,
        Guid userId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var target = new UserId(userId);
        if (target == profile.Id)
        {
            return Results.BadRequest(new { error = "CannotReportSelf" });
        }

        var isMember = await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.UserId == target, ct);
        if (!isMember)
        {
            return Results.NotFound();
        }

        var since = clock.UtcNow.AddHours(-1);
        var recent = await db.AuditEvents.AsNoTracking().AnyAsync(
            x => x.ActorUserId == profile.Id
                && x.Action == AuditActions.UserStatusReport
                && x.EntityId == target.Value.ToString()
                && x.OccurredAt >= since,
            ct);
        if (!recent)
        {
            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.UserStatusReport,
                EntityType = "UserStatus",
                EntityId = target.Value.ToString(),
                MetadataJson = "{}"
            });
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> GetCalendarAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        IOptions<AvailabilityCalendarOptions> options,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        await BeginRlsUserAsync(db, tenant, profile.Id, ct);
        if (!tenant.HasTenant)
        {
            return Results.Forbid();
        }

        if (!options.Value.Enabled)
        {
            return Results.NotFound(new { error = "CalendarIntegrationDisabled" });
        }

        return Results.Ok(new CalendarAvailabilityResponse(Enabled: true, Connected: false));
    }

    private static async Task PurgeExpiredAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        IPresenceService presence,
        IClock clock,
        IHubContext<ChatHub> hub,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var expired = await db.UserStatuses
            .Where(x => x.ExpiresAt != null && x.ExpiresAt <= now)
            .ToListAsync(ct);
        if (expired.Count == 0)
        {
            return;
        }

        var ids = expired.Select(x => x.UserId).ToArray();
        db.UserStatuses.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
        foreach (var userId in ids)
        {
            var response = await BuildOneAsync(db, tenantId, userId, status: null, presence, clock, ct);
            await PublishAsync(hub, logs, tenantId, userId.Value, response, ct);
        }
    }

    private static async Task<UserStatusResponse> BuildOneAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        UserId userId,
        UserStatus? status,
        IPresenceService presence,
        IClock clock,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (!UserStatusRules.IsActive(status, now))
        {
            status = null;
        }

        var pref = await db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var presenceMap = await presence.GetStatusesAsync(tenantId, [userId], ct);
        presenceMap.TryGetValue(userId, out var presenceStatus);
        var member = ToResponse(userId, presenceStatus, pref, status, now);
        return new UserStatusResponse(member.Presence, member.Availability, member.Status);
    }

    private static MemberAvailabilityResponse ToResponse(
        UserId userId,
        PresenceStatus presenceStatus,
        NotificationPreference? pref,
        UserStatus? status,
        DateTimeOffset now)
    {
        var active = UserStatusRules.IsActive(status, now);
        var presenceName = UserStatusRules.ToApiPresence(presenceStatus.ToString());
        var dnd = pref is not null && PushDispatchPolicies.IsWithinDnd(
            pref.DndEnabled,
            pref.DndStart,
            pref.DndEnd,
            pref.DndDays,
            pref.TimeZone,
            now);
        var availability = UserStatusRules.Derive(
            presenceName,
            dnd,
            active ? status!.State : null,
            active);
        UserStatusBody? body = active
            ? new UserStatusBody(
                UserStatusRules.ToApiState(status!.State),
                status.Emoji,
                status.Text,
                status.ClearAtEndOfDay,
                status.ExpiresAt)
            : null;
        return new MemberAvailabilityResponse(
            userId.Value,
            presenceName,
            UserStatusRules.ToApiAvailability(availability),
            body);
    }

    private static async Task PublishAsync(
        IHubContext<ChatHub> hub,
        ILoggerFactory logs,
        TenantId tenantId,
        Guid userId,
        UserStatusResponse response,
        CancellationToken ct)
    {
        try
        {
            await hub.Clients.Group(ChatHub.TenantGroup(tenantId)).SendAsync(
                "UserStatusChanged",
                new
                {
                    tenantId = tenantId.Value,
                    userId,
                    presence = response.Presence,
                    availability = response.Availability,
                    status = response.Status
                },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logs.CreateLogger("VibeChat.UserStatus").LogWarning(ex, "User status realtime publish failed");
        }
    }
}
