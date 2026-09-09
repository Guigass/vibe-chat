using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Realtime;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class RealtimeEndpoints
{
    internal static void MapPresence(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/presence", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IPresenceService presence, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var memberIds = await db.WorkspaceMembers
                .Where(x => x.WorkspaceId == workspace.Id)
                .Select(x => x.UserId)
                .ToListAsync(ct);
            var statuses = await presence.GetStatusesAsync(workspace.TenantId, memberIds, ct);
            var response = memberIds.Select(id =>
            {
                statuses.TryGetValue(id, out var status);
                return new PresenceResponse(id.Value, status.ToString().ToLowerInvariant());
            }).ToArray();
            return Results.Ok(response);
        });
    }
}
