using Microsoft.EntityFrameworkCore;
using VibeChat.Administration;
using VibeChat.Api;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.SharedKernel;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;
using static VibeChat.Api.Endpoints.NotificationsEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class NotificationsEndpoints
{
    internal static void MapNotifications(this RouteGroupBuilder v1)
    {
        v1.MapGet("/notifications/push/public-key", async (ProcessSettingsResolver processSettings, CancellationToken ct) =>
        {
            var process = await processSettings.ResolveAsync(ct);
            var publicKey = process.VapidConfigured ? process.VapidPublicKey?.Trim() : null;
            var enabled = process.PushEnabled && !string.IsNullOrWhiteSpace(publicKey);
            return Results.Ok(new PushPublicKeyResponse(enabled, enabled ? publicKey : null));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapGet("/notifications/push/subscriptions", async (
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var rows = await db.PushSubscriptions.AsNoTracking()
                .Where(x => x.UserId == profile.Id)
                .OrderByDescending(x => x.LastSeenAt)
                .Select(x => new PushSubscriptionResponse(x.Id, x.Endpoint, x.UserAgent, x.CreatedAt, x.LastSeenAt))
                .ToArrayAsync(ct);
            return Results.Ok(rows);
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPost("/notifications/push/subscriptions", async (
            PushSubscriptionRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            if (!tenant.HasTenant)
            {
                return Results.Forbid();
            }

            var endpoint = request.Endpoint?.Trim() ?? string.Empty;
            var p256dh = request.P256dh?.Trim() ?? string.Empty;
            var auth = request.Auth?.Trim() ?? string.Empty;
            if (endpoint.Length is < 8 or > 2048
                || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            {
                return Results.BadRequest(new { error = "InvalidEndpoint" });
            }

            if (p256dh.Length is < 8 or > 256 || auth.Length is < 8 or > 256)
            {
                return Results.BadRequest(new { error = "InvalidKeys" });
            }

            var userAgent = string.IsNullOrWhiteSpace(request.UserAgent)
                ? http.Request.Headers.UserAgent.ToString()
                : request.UserAgent.Trim();
            if (userAgent.Length > 512)
            {
                userAgent = userAgent[..512];
            }

            var now = clock.UtcNow;
            var existing = await db.PushSubscriptions.FirstOrDefaultAsync(
                x => x.UserId == profile.Id && x.Endpoint == endpoint,
                ct);
            if (existing is not null)
            {
                existing.P256dh = p256dh;
                existing.Auth = auth;
                existing.UserAgent = userAgent;
                existing.LastSeenAt = now;
                existing.FailedAt = null;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new PushSubscriptionResponse(existing.Id, existing.Endpoint, existing.UserAgent, existing.CreatedAt, existing.LastSeenAt));
            }

            var row = new PushSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                UserId = profile.Id,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                UserAgent = userAgent,
                CreatedAt = now,
                LastSeenAt = now
            };
            db.PushSubscriptions.Add(row);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new PushSubscriptionResponse(row.Id, row.Endpoint, row.UserAgent, row.CreatedAt, row.LastSeenAt));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapDelete("/notifications/push/subscriptions/{id:guid}", async (
            Guid id,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var row = await db.PushSubscriptions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == profile.Id, ct);
            if (row is null)
            {
                return Results.NotFound();
            }

            db.PushSubscriptions.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.Message.Read);

        // B-097: notification preferences + DND. Strictly per (tenant, user) — nobody, including admin,
        // reads or writes another user's row (no audit trail for personal preference by design).
        v1.MapGet("/notifications/preferences", async (
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var pref = await db.NotificationPreferences.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
            // B-102: row presence no longer implies a mute override alone (a row may exist only to carry
            // FollowAllThreads) — Level null means "no override", filtered out of this mute-specific array.
            var overrides = await db.ChannelNotificationPreferences.AsNoTracking()
                .Where(x => x.UserId == profile.Id && x.Level != null)
                .Select(x => new ChannelNotificationOverrideResponse(x.ChannelId.Value, x.Level!.Value.ToString(), x.MutedUntil))
                .ToArrayAsync(ct);
            var followAllThreadsChannelIds = await db.ChannelNotificationPreferences.AsNoTracking()
                .Where(x => x.UserId == profile.Id && x.FollowAllThreads)
                .Select(x => x.ChannelId.Value)
                .ToArrayAsync(ct);
            return Results.Ok(new NotificationPreferencesResponse(
                (pref?.Level ?? NotificationLevel.MentionsAndDms).ToString(),
                pref?.HidePreview ?? false,
                pref?.DndEnabled ?? false,
                pref?.DndStart,
                pref?.DndEnd,
                pref?.DndDays ?? (short)0,
                pref?.TimeZone,
                pref?.DigestEnabled ?? false,
                pref?.PriorityContactUserIds ?? [],
                overrides,
                followAllThreadsChannelIds));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPut("/notifications/preferences", async (
            UpdateNotificationPreferencesRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            if (!tenant.HasTenant)
            {
                return Results.Forbid();
            }

            if (!Enum.TryParse<NotificationLevel>(request.Level, ignoreCase: true, out var level)
                || !Enum.IsDefined(level))
            {
                return Results.BadRequest(new { error = "InvalidLevel" });
            }

            if (request.DndDays is < 0 or > 127)
            {
                return Results.BadRequest(new { error = "InvalidDndDays" });
            }

            if (request.DndEnabled)
            {
                if (request.DndStart is null || request.DndEnd is null || string.IsNullOrWhiteSpace(request.TimeZone)
                    || !IsValidTimeZone(request.TimeZone))
                {
                    return Results.BadRequest(new { error = "DndRequiresWindowAndValidTimeZone" });
                }
            }

            var requestedContactIds = (request.PriorityContactUserIds ?? [])
                .Distinct()
                .Take(50)
                .Select(id => new UserId(id))
                .ToArray();
            var priorityContacts = requestedContactIds.Length == 0
                ? []
                : await db.WorkspaceMembers.AsNoTracking()
                    .Where(x => x.TenantId == tenant.TenantId && requestedContactIds.Contains(x.UserId))
                    .Select(x => x.UserId.Value)
                    .Distinct()
                    .ToArrayAsync(ct);

            var pref = await db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
            if (pref is null)
            {
                pref = new NotificationPreference { Id = Guid.NewGuid(), TenantId = tenant.TenantId, UserId = profile.Id };
                db.NotificationPreferences.Add(pref);
            }

            pref.Level = level;
            pref.HidePreview = request.HidePreview;
            pref.DndEnabled = request.DndEnabled;
            pref.DndStart = request.DndStart;
            pref.DndEnd = request.DndEnd;
            pref.DndDays = request.DndDays;
            pref.TimeZone = request.TimeZone;
            pref.DigestEnabled = request.DigestEnabled;
            pref.PriorityContactUserIds = priorityContacts;
            await db.SaveChangesAsync(ct);

            var overrides = await db.ChannelNotificationPreferences.AsNoTracking()
                .Where(x => x.UserId == profile.Id && x.Level != null)
                .Select(x => new ChannelNotificationOverrideResponse(x.ChannelId.Value, x.Level!.Value.ToString(), x.MutedUntil))
                .ToArrayAsync(ct);
            var followAllThreadsChannelIds = await db.ChannelNotificationPreferences.AsNoTracking()
                .Where(x => x.UserId == profile.Id && x.FollowAllThreads)
                .Select(x => x.ChannelId.Value)
                .ToArrayAsync(ct);
            return Results.Ok(new NotificationPreferencesResponse(
                pref.Level.ToString(), pref.HidePreview, pref.DndEnabled, pref.DndStart, pref.DndEnd,
                pref.DndDays, pref.TimeZone, pref.DigestEnabled, pref.PriorityContactUserIds, overrides,
                followAllThreadsChannelIds));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPut("/notifications/preferences/channels/{channelId:guid}", async (
            Guid channelId,
            UpdateChannelNotificationPreferenceRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            if (!Enum.TryParse<NotificationLevel>(request.Level, ignoreCase: true, out var level)
                || !Enum.IsDefined(level))
            {
                return Results.BadRequest(new { error = "InvalidLevel" });
            }

            DateTimeOffset? mutedUntil = null;
            if (request.Duration is not null)
            {
                var now = clock.UtcNow;
                switch (request.Duration)
                {
                    case "OneHour":
                        mutedUntil = now.AddHours(1);
                        break;
                    case "EightHours":
                        mutedUntil = now.AddHours(8);
                        break;
                    case "UntilTomorrow":
                        var globalPref = await db.NotificationPreferences.AsNoTracking()
                            .FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
                        mutedUntil = NextLocalMidnightUtc(now, globalPref?.TimeZone);
                        break;
                    case "Indefinite":
                        mutedUntil = null;
                        break;
                    default:
                        return Results.BadRequest(new { error = "InvalidDuration" });
                }
            }

            var row = await db.ChannelNotificationPreferences
                .FirstOrDefaultAsync(x => x.ChannelId == channel.Id && x.UserId == profile.Id, ct);
            if (row is null)
            {
                row = new ChannelNotificationPreference
                {
                    Id = Guid.NewGuid(),
                    TenantId = channel.TenantId,
                    UserId = profile.Id,
                    ChannelId = channel.Id
                };
                db.ChannelNotificationPreferences.Add(row);
            }

            row.Level = level;
            row.MutedUntil = mutedUntil;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ChannelNotificationOverrideResponse(channel.Id.Value, level.ToString(), row.MutedUntil));
        }).RequirePermission(Permissions.Message.Read);

        v1.MapDelete("/notifications/preferences/channels/{channelId:guid}", async (
            Guid channelId,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var row = await db.ChannelNotificationPreferences
                .FirstOrDefaultAsync(x => x.ChannelId == channel.Id && x.UserId == profile.Id, ct);
            if (row is not null)
            {
                if (row.FollowAllThreads)
                {
                    // B-102: clearing a mute override must not silently drop "seguir todas as threads" —
                    // keep the row, just clear the level/mute fields it existed for.
                    row.Level = null;
                    row.MutedUntil = null;
                }
                else
                {
                    db.ChannelNotificationPreferences.Remove(row);
                }

                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        }).RequirePermission(Permissions.Message.Read);

        v1.MapPut("/notifications/preferences/channels/{channelId:guid}/follow-all-threads", async (
            Guid channelId,
            UpdateChannelFollowAllThreadsRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
            if (channel is null)
            {
                return Results.Forbid();
            }

            var row = await db.ChannelNotificationPreferences
                .FirstOrDefaultAsync(x => x.ChannelId == channel.Id && x.UserId == profile.Id, ct);
            if (row is null)
            {
                if (!request.Enabled)
                {
                    return Results.NoContent();
                }

                row = new ChannelNotificationPreference
                {
                    Id = Guid.NewGuid(),
                    TenantId = channel.TenantId,
                    UserId = profile.Id,
                    ChannelId = channel.Id,
                    Level = null
                };
                db.ChannelNotificationPreferences.Add(row);
            }

            row.FollowAllThreads = request.Enabled;
            if (!request.Enabled && row.Level is null)
            {
                // Nothing left to persist on this row (no mute override, no follow-all-threads).
                db.ChannelNotificationPreferences.Remove(row);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.Message.Read);
    }
}
