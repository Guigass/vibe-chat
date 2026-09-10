using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.Notifications;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class DirectoryEndpoints
{
    internal static void MapWorkspaces(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces", async (HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            await BeginRlsUserAsync(db, tenant, profile.Id, ct);
            var workspaces = await db.WorkspaceMembers.IgnoreQueryFilters()
                .Where(x => x.UserId == profile.Id)
                .Join(db.Workspaces.IgnoreQueryFilters(), m => m.WorkspaceId, w => w.Id, (m, w) => new WorkspaceResponse(w.Id.Value, w.Name, w.Slug, m.Role.ToString()))
                .ToListAsync(ct);
            var memberIds = workspaces.Select(x => x.Id).ToHashSet();
            var guestWorkspaces = await (
                from cm in db.ChannelMembers.IgnoreQueryFilters()
                join ch in db.Channels.IgnoreQueryFilters() on cm.ChannelId equals ch.Id
                join w in db.Workspaces.IgnoreQueryFilters() on ch.WorkspaceId equals w.Id
                where cm.UserId == profile.Id && cm.LeftAt == null && !memberIds.Contains(w.Id.Value)
                select new WorkspaceResponse(w.Id.Value, w.Name, w.Slug, Role.Guest.ToString())
            ).ToListAsync(ct);
            workspaces.AddRange(guestWorkspaces.DistinctBy(x => x.Id));
            return Results.Ok(workspaces);
        });
    }

    internal static void MapSpacesAndMembers(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/spaces", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var spaces = await db.Spaces
                .Where(x => x.WorkspaceId == workspace.Id)
                .OrderBy(x => x.Order)
                .ThenBy(x => x.Name)
                .Select(x => new SpaceResponse(x.Id, x.WorkspaceId.Value, x.Name, x.Order))
                .ToArrayAsync(ct);

            return Results.Ok(spaces);
        });

        v1.MapPost("/workspaces/{workspaceId:guid}/spaces", async (Guid workspaceId, CreateSpaceRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IAuditWriter audit, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { error = "name is required." });
            }

            var maxOrder = await db.Spaces.Where(x => x.WorkspaceId == workspace.Id).Select(x => (int?)x.Order).MaxAsync(ct) ?? -1;
            var space = new Space
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                Name = name,
                Order = request.Order ?? maxOrder + 1,
                CreatedAt = clock.UtcNow
            };
            db.Spaces.Add(space);
            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.SpaceCreate,
                EntityType = "Space",
                EntityId = space.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new { workspaceId, name = space.Name })
            });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/spaces/{space.Id}", new SpaceResponse(space.Id, space.WorkspaceId.Value, space.Name, space.Order));
        }).RequirePermission(Permissions.Channel.Create);

        v1.MapGet("/workspaces/{workspaceId:guid}/members", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var members = await (
                from m in db.WorkspaceMembers
                where m.WorkspaceId == workspace.Id
                join u in db.UserProfiles on m.UserId equals u.Id
                orderby u.DisplayName
                select new WorkspaceMemberResponse(u.Id.Value, u.DisplayName, u.Email, m.Role.ToString())
            ).ToArrayAsync(ct);

            return Results.Ok(members);
        });
    }

    internal static void MapWorkspaceRoles(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/roles", async (Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var actorMembership = await db.WorkspaceMembers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == profile.Id, ct);
            if (actorMembership is null || !WorkspaceRolePolicies.CanManageRoles(actorMembership.Role))
            {
                return Results.Forbid();
            }

            return Results.Ok(new WorkspaceRolesResponse(
                WorkspaceRolePolicies.AssignableRoles.Select(r => r.ToString()).ToArray()));
        }).RequirePermission(Permissions.Workspace.Admin);

        // B-068: admin invite / provision membership (no open self-signup).
        v1.MapPost("/workspaces/{workspaceId:guid}/members", async (
            Guid workspaceId,
            InviteMemberRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IOutboxWriter outbox,
            IAuditWriter audit,
            EmailSettingsResolver emailSettings,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var actorMembership = await db.WorkspaceMembers
                .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == profile.Id, ct);
            if (actorMembership is null || !WorkspaceRolePolicies.CanInviteMembers(actorMembership.Role))
            {
                return Results.Forbid();
            }

            var emailAddress = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(emailAddress) || !emailAddress.Contains('@') || emailAddress.Length > 256)
            {
                return Results.BadRequest(new { error = "Valid email is required." });
            }

            var roleValue = string.IsNullOrWhiteSpace(request.Role) ? Role.Member.ToString() : request.Role;
            if (!WorkspaceRolePolicies.TryParseRole(roleValue, out var inviteRole)
                || !WorkspaceRolePolicies.CanAssignInviteRole(actorMembership.Role, inviteRole))
            {
                return Results.BadRequest(new { error = "InvalidRole" });
            }

            var displayName = (request.DisplayName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = emailAddress.Split('@')[0];
            }

            if (displayName.Length > 160)
            {
                displayName = displayName[..160];
            }

            var pendingSubject = WorkspaceRolePolicies.PendingSubjectForEmail(emailAddress);
            var targetProfile = await db.UserProfiles
                .FirstOrDefaultAsync(x => x.Email.ToLower() == emailAddress || x.Subject == pendingSubject, ct);

            if (targetProfile is null)
            {
                targetProfile = new UserProfile
                {
                    Id = UserId.New(),
                    Subject = pendingSubject,
                    Email = emailAddress,
                    DisplayName = displayName,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                };
                db.UserProfiles.Add(targetProfile);
            }
            else if (string.IsNullOrWhiteSpace(targetProfile.DisplayName)
                     || (WorkspaceRolePolicies.IsPendingSubject(targetProfile.Subject)
                         && !string.IsNullOrWhiteSpace(request.DisplayName)))
            {
                targetProfile.DisplayName = displayName;
                targetProfile.UpdatedAt = clock.UtcNow;
            }

            var existing = await db.WorkspaceMembers
                .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == targetProfile.Id, ct);
            if (existing is not null)
            {
                return Results.Conflict(new { error = "AlreadyMember", userId = targetProfile.Id.Value, role = existing.Role.ToString() });
            }

            var membership = new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                UserId = targetProfile.Id,
                Role = inviteRole,
                JoinedAt = clock.UtcNow
            };
            db.WorkspaceMembers.Add(membership);

            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.MemberInvite,
                EntityType = "WorkspaceMember",
                EntityId = membership.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    workspaceId = workspace.Id.Value,
                    userId = targetProfile.Id.Value,
                    email = emailAddress,
                    role = inviteRole.ToString(),
                    pending = WorkspaceRolePolicies.IsPendingSubject(targetProfile.Subject)
                })
            });

            if (await emailSettings.IsEnabledAsync(workspace.TenantId, ct)
                && !string.IsNullOrWhiteSpace(targetProfile.Email))
            {
                var copy = MemberEmailCopy.Invite(
                    UserLocales.Resolve(targetProfile.Locale),
                    workspace.Name,
                    targetProfile.DisplayName,
                    inviteRole.ToString());
                outbox.Add(new OutboxMessage
                {
                    TenantId = workspace.TenantId,
                    Type = nameof(MemberInvitedEmailEvent),
                    Payload = JsonSerializer.Serialize(new MemberInvitedEmailEvent(
                        workspace.TenantId.Value,
                        workspace.Id.Value,
                        targetProfile.Id.Value,
                        targetProfile.Email,
                        copy.Subject,
                        copy.BodyText))
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Created(
                $"/api/v1/workspaces/{workspaceId}/members/{targetProfile.Id.Value}",
                new WorkspaceMemberResponse(targetProfile.Id.Value, targetProfile.DisplayName, targetProfile.Email, membership.Role.ToString()));
        }).RequirePermission(Permissions.Workspace.Admin);

        v1.MapPut("/workspaces/{workspaceId:guid}/members/{userId:guid}/role", async (
            Guid workspaceId,
            Guid userId,
            UpdateMemberRoleRequest request,
            HttpContext http,
            VibeChatDbContext db,
            ITenantContext tenant,
            IOutboxWriter outbox,
            IAuditWriter audit,
            EmailSettingsResolver emailSettings,
            IClock clock,
            CancellationToken ct) =>
        {
            var profile = await EnsureProfileAsync(http.User, db, clock, ct);
            var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
            if (workspace is null)
            {
                return Results.Forbid();
            }

            var actorMembership = await db.WorkspaceMembers
                .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == profile.Id, ct);
            if (actorMembership is null || !WorkspaceRolePolicies.CanManageRoles(actorMembership.Role))
            {
                return Results.Forbid();
            }

            if (!WorkspaceRolePolicies.TryParseRole(request.Role, out var newRole))
            {
                return Results.BadRequest(new { error = "InvalidRole" });
            }

            var targetUserId = new UserId(userId);
            var targetMembership = await db.WorkspaceMembers
                .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == targetUserId, ct);
            if (targetMembership is null)
            {
                return Results.NotFound();
            }

            var isSelf = targetMembership.UserId == profile.Id;
            if (!WorkspaceRolePolicies.CanChangeMemberRole(actorMembership.Role, targetMembership.Role, newRole, isSelf))
            {
                return Results.Forbid();
            }

            if (targetMembership.Role == newRole)
            {
                var unchanged = await db.UserProfiles.AsNoTracking().FirstAsync(x => x.Id == targetUserId, ct);
                return Results.Ok(new WorkspaceMemberResponse(unchanged.Id.Value, unchanged.DisplayName, unchanged.Email, targetMembership.Role.ToString()));
            }

            var previousRole = targetMembership.Role;
            targetMembership.Role = newRole;

            var targetProfile = await db.UserProfiles.AsNoTracking().FirstAsync(x => x.Id == targetUserId, ct);
            audit.Add(new AuditEvent
            {
                TenantId = workspace.TenantId,
                ActorUserId = profile.Id,
                Action = AuditActions.MemberRoleChange,
                EntityType = "WorkspaceMember",
                EntityId = targetMembership.Id.ToString(),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    workspaceId = workspace.Id.Value,
                    userId = targetUserId.Value,
                    from = previousRole.ToString(),
                    to = newRole.ToString()
                })
            });

            // B-043: optional email via outbox (never on SendMessage hot path). Off by default (D-10).
            if (await emailSettings.IsEnabledAsync(workspace.TenantId, ct)
                && !string.IsNullOrWhiteSpace(targetProfile.Email))
            {
                var copy = MemberEmailCopy.RoleChanged(
                    UserLocales.Resolve(targetProfile.Locale),
                    workspace.Name,
                    targetProfile.DisplayName,
                    previousRole.ToString(),
                    newRole.ToString());
                outbox.Add(new OutboxMessage
                {
                    TenantId = workspace.TenantId,
                    Type = nameof(MemberRoleChangedEmailEvent),
                    Payload = JsonSerializer.Serialize(new MemberRoleChangedEmailEvent(
                        workspace.TenantId.Value,
                        workspace.Id.Value,
                        targetUserId.Value,
                        targetProfile.Email,
                        copy.Subject,
                        copy.BodyText))
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new WorkspaceMemberResponse(targetProfile.Id.Value, targetProfile.DisplayName, targetProfile.Email, targetMembership.Role.ToString()));
        }).RequirePermission(Permissions.Workspace.Admin);
    }
}
