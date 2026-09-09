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

            return Results.Ok(new MeResponse(profile.Id.Value, profile.Subject, profile.Email, profile.DisplayName, roles.Select(x => x.ToString()).ToArray(), profile.Locale));
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
            return Results.Ok(new MeResponse(profile.Id.Value, profile.Subject, profile.Email, profile.DisplayName, roles.Select(x => x.ToString()).ToArray(), profile.Locale));
        });
    }
}
