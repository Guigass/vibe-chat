using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class IdentityEndpoints
{
    internal static void MapIdentity(this RouteGroupBuilder v1)
    {
        v1.MapGet("/me", async (HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, IAuditWriter audit, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var roles = await db.WorkspaceMembers.IgnoreQueryFilters().Where(x => x.UserId == profile.Id).Select(x => x.Role).Distinct().ToArrayAsync(ct);
            if (roles.Length == 0
                && await db.ChannelMembers.IgnoreQueryFilters().AnyAsync(x => x.UserId == profile.Id && x.LeftAt == null, ct))
            {
                roles = [Role.Guest];
            }
            if (roles.Any(x => x is Role.Admin or Role.PlatformOwner or Role.WorkspaceOwner))
            {
                var membershipTenant = await db.WorkspaceMembers.IgnoreQueryFilters()
                    .Where(x => x.UserId == profile.Id)
                    .Select(x => x.TenantId)
                    .FirstOrDefaultAsync(ct);
                if (membershipTenant.Value != Guid.Empty)
                {
                    tenant.SetTenant(membershipTenant);
                    await RlsSession.EnsureAppliedAsync(db, tenant, ct);
                    audit.Add(new AuditEvent
                    {
                        TenantId = membershipTenant,
                        ActorUserId = profile.Id,
                        Action = AuditActions.AdminLogin,
                        EntityType = "UserProfile",
                        EntityId = profile.Id.ToString()
                    });
                    await db.SaveChangesAsync(ct);
                }
            }

            var (wallpaper, accent) = await ReadAppearanceAsync(db, tenant, profile.Id, ct);
            return Results.Ok(ToMe(profile, roles, wallpaper, accent));
        });

        v1.MapPut("/me", async (UpdateMeRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var locale = (request.Locale ?? string.Empty).Trim();
            if (!UserLocales.IsSupported(locale))
            {
                return Results.BadRequest(new { error = "InvalidLocale" });
            }

            profile.Locale = locale;
            profile.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);

            var roles = await db.WorkspaceMembers.IgnoreQueryFilters().Where(x => x.UserId == profile.Id).Select(x => x.Role).Distinct().ToArrayAsync(ct);
            if (roles.Length == 0
                && await db.ChannelMembers.IgnoreQueryFilters().AnyAsync(x => x.UserId == profile.Id && x.LeftAt == null, ct))
            {
                roles = [Role.Guest];
            }
            var (wallpaper, accent) = await ReadAppearanceAsync(db, tenant, profile.Id, ct);
            return Results.Ok(ToMe(profile, roles, wallpaper, accent));
        }).AllowPermissionGateExempt("caller-only profile locale update");

        // B-185: wallpaper and accent are personal and tenant-scoped. The route user
        // must be the caller — admins cannot edit someone else's appearance.
        v1.MapPut("/users/{userId:guid}/appearance", async (
            Guid userId,
            UpdateAppearanceRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            if (profile.Id.Value != userId)
            {
                return Results.Forbid();
            }

            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var tenantId = await ResolvePersonalTenantAsync(db, profile.Id, ct);
            if (tenantId is null)
            {
                return Results.Forbid();
            }

            if (!VisualPreferenceCatalog.TryNormalizeWallpaper(request.ChatWallpaperId, out var wallpaper))
            {
                return Results.BadRequest(new { error = VisualPreferenceCatalog.InvalidWallpaper });
            }

            if (!VisualPreferenceCatalog.TryNormalizeAccent(request.AccentColorId, out var accent))
            {
                return Results.BadRequest(new { error = VisualPreferenceCatalog.InvalidAccent });
            }

            tenant.SetTenant(tenantId.Value);
            await RlsSession.EnsureAppliedAsync(db, tenant, ct);
            var row = await db.UserVisualPreferences.FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
            if (row is null)
            {
                if (wallpaper is null && accent is null)
                {
                    return Results.Ok(new AppearanceResponse(null, null));
                }

                row = new UserVisualPreference
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId.Value,
                    UserId = profile.Id
                };
                db.UserVisualPreferences.Add(row);
            }

            row.ChatWallpaperId = wallpaper;
            row.AccentColorId = accent;
            row.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new AppearanceResponse(wallpaper, accent));
        }).AllowPermissionGateExempt("caller-only visual preference (B-185)");
    }

    private static MeResponse ToMe(UserProfile profile, Role[] roles, string? wallpaper, string? accent) =>
        new(
            profile.Id.Value,
            profile.Subject,
            profile.Email,
            profile.DisplayName,
            roles.Select(x => x.ToString()).ToArray(),
            profile.Locale,
            wallpaper,
            accent);

    private static async Task<(string? Wallpaper, string? Accent)> ReadAppearanceAsync(
        VibeChatDbContext db,
        ITenantContext tenant,
        UserId userId,
        CancellationToken ct)
    {
        var tenantId = await ResolvePersonalTenantAsync(db, userId, ct);
        if (tenantId is null)
        {
            return (null, null);
        }

        tenant.SetTenant(tenantId.Value);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        var row = await db.UserVisualPreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        return (
            VisualPreferenceCatalog.ResolveWallpaper(row?.ChatWallpaperId),
            VisualPreferenceCatalog.ResolveAccent(row?.AccentColorId));
    }

    private static async Task<TenantId?> ResolvePersonalTenantAsync(
        VibeChatDbContext db,
        UserId userId,
        CancellationToken ct)
    {
        var fromWorkspace = await db.WorkspaceMembers.IgnoreQueryFilters()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.JoinedAt)
            .Select(x => x.TenantId)
            .FirstOrDefaultAsync(ct);
        if (fromWorkspace.Value != Guid.Empty)
        {
            return fromWorkspace;
        }

        var fromChannel = await db.ChannelMembers.IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.LeftAt == null)
            .Select(x => x.TenantId)
            .FirstOrDefaultAsync(ct);
        return fromChannel.Value == Guid.Empty ? null : fromChannel;
    }
}
