using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

internal sealed class NoopExternalSessionRevoker : IExternalSessionRevoker
{
    public Task RevokeAsync(UserId userId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class ScimProvisioningService(
    VibeChatDbContext db,
    IClock clock,
    IAuditWriter audit,
    IOptionsMonitor<ScimOptions> options,
    IExternalSessionRevoker revoker,
    ILogger<ScimProvisioningService> logger) : IProvisioningPort
{
    public async Task<ProvisionUserResult> ApplyUserAsync(ProvisionUserCommand command, CancellationToken cancellationToken)
    {
        if (ScimPolicies.ExternalIdRejected(command.ExternalId))
        {
            return UserFail("InvalidExternalId");
        }

        var externalId = ScimPolicies.NormalizeExternalId(command.ExternalId);
        var now = clock.UtcNow;
        ScimPrincipal? principal = null;
        if (command.PrincipalId is { } id)
        {
            principal = await db.ScimPrincipals.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (principal is null)
            {
                return UserFail("PrincipalNotFound");
            }
        }

        var userName = ScimPolicies.NormalizeUserName(command.UserName) ?? principal?.UserName;
        if (userName is null)
        {
            return UserFail("InvalidUserName");
        }

        var displayName = ScimPolicies.NormalizeDisplayName(command.DisplayName ?? principal?.DisplayName, userName);
        if (command.PrincipalId is null)
        {
            ScimPrincipal? byExternal = null;
            if (externalId is not null)
            {
                byExternal = await db.ScimPrincipals.FirstOrDefaultAsync(x => x.ExternalId == externalId, cancellationToken);
            }

            var byName = await db.ScimPrincipals.FirstOrDefaultAsync(x => x.UserName == userName, cancellationToken);
            if (byExternal is not null && byName is not null && byExternal.Id != byName.Id)
            {
                return UserFail("UserNameConflict");
            }

            principal = byExternal ?? byName;
        }
        else if (await db.ScimPrincipals.AnyAsync(
            x => x.UserName == userName && x.Id != principal!.Id, cancellationToken))
        {
            return UserFail("UserNameConflict");
        }

        var created = false;
        if (principal is null)
        {
            var profile = await FindOrCreateProfileAsync(userName, displayName, now, cancellationToken);
            principal = new ScimPrincipal
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                UserId = profile.Id,
                ExternalId = externalId,
                UserName = userName,
                DisplayName = displayName,
                Active = command.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.ScimPrincipals.Add(principal);
            created = true;
        }
        else
        {
            if (externalId is not null
                && principal.ExternalId is not null
                && !string.Equals(principal.ExternalId, externalId, StringComparison.Ordinal))
            {
                var taken = await db.ScimPrincipals.AnyAsync(
                    x => x.ExternalId == externalId && x.Id != principal.Id, cancellationToken);
                if (taken)
                {
                    return UserFail("ExternalIdConflict");
                }
            }

            principal.UserName = userName;
            principal.DisplayName = displayName;
            if (externalId is not null)
            {
                principal.ExternalId = externalId;
            }

            principal.UpdatedAt = now;
            var profile = await db.UserProfiles.FirstAsync(x => x.Id == principal.UserId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                profile.DisplayName = displayName;
                profile.UpdatedAt = now;
            }
        }

        var deactivated = false;
        if (!command.Active)
        {
            if (principal.Active || created)
            {
                deactivated = true;
            }

            principal.Active = false;
            await DeprovisionAsync(command.TenantId, principal.UserId, now, cancellationToken);
        }
        else if (!principal.Active)
        {
            principal.Active = true;
            await ReactivateAsync(command.TenantId, principal.UserId, cancellationToken);
        }
        else
        {
            principal.Active = true;
        }

        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            ActorUserId = command.ActorUserId,
            Action = deactivated ? AuditActions.ScimUserDeprovision : AuditActions.ScimUserProvision,
            EntityType = "ScimPrincipal",
            EntityId = principal.Id.ToString(),
            MetadataJson = $"{{\"source\":\"{command.Source}\",\"active\":{(command.Active ? "true" : "false")}}}",
            OccurredAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        VibeChatMetrics.ScimProvisioned.Add(1, new KeyValuePair<string, object?>("result", deactivated ? "deactivated" : created ? "created" : "updated"));

        if (deactivated && options.CurrentValue.KeycloakAdapter)
        {
            try
            {
                await revoker.RevokeAsync(principal.UserId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SCIM external session revoke failed");
            }
        }

        return new ProvisionUserResult(
            true, null, principal.Id, principal.UserId, created, deactivated,
            principal.UserName, principal.DisplayName, principal.ExternalId, principal.Active);
    }

    public async Task<ProvisionGroupResult> ApplyGroupAsync(ProvisionGroupCommand command, CancellationToken cancellationToken)
    {
        if (ScimPolicies.ExternalIdRejected(command.ExternalId))
        {
            return GroupFail("InvalidExternalId");
        }

        var externalId = ScimPolicies.NormalizeExternalId(command.ExternalId);
        var now = clock.UtcNow;
        ScimGroup? group = null;
        if (command.GroupId is { } id)
        {
            group = await db.ScimGroups.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (group is null)
            {
                return GroupFail("GroupNotFound");
            }
        }
        else if (externalId is not null)
        {
            group = await db.ScimGroups.FirstOrDefaultAsync(x => x.ExternalId == externalId, cancellationToken);
        }

        if (command.Remove)
        {
            if (group is null)
            {
                return GroupFail("GroupNotFound");
            }

            await RemoveGroupAsync(group, cancellationToken);
            audit.Add(Audit(command.TenantId, command.ActorUserId, AuditActions.ScimGroupProvision, group.Id, command.Source, now));
            await db.SaveChangesAsync(cancellationToken);
            return new ProvisionGroupResult(true, null, group.Id, false, true, group.DisplayName, group.ExternalId, []);
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName) && group is null)
        {
            return GroupFail("InvalidDisplayName");
        }

        var displayName = ScimPolicies.NormalizeDisplayName(command.DisplayName ?? group?.DisplayName, group?.DisplayName ?? "group");
        if (HasControl(displayName))
        {
            return GroupFail("InvalidDisplayName");
        }

        if (command.MemberPrincipalIds is { } requested)
        {
            var distinct = requested.Distinct().ToArray();
            var known = await db.ScimPrincipals.Where(x => distinct.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken);
            if (known.Count != distinct.Length)
            {
                return GroupFail("PrincipalNotFound");
            }
        }

        var created = false;
        if (group is null)
        {
            group = new ScimGroup
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                ExternalId = externalId,
                DisplayName = displayName,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.ScimGroups.Add(group);
            created = true;
        }
        else
        {
            group.DisplayName = displayName;
            if (externalId is not null)
            {
                group.ExternalId = externalId;
            }

            group.UpdatedAt = now;
        }

        if (command.MemberPrincipalIds is { } members)
        {
            var distinct = members.Distinct().ToArray();
            var current = await db.ScimGroupMembers.Where(x => x.GroupId == group.Id).ToListAsync(cancellationToken);
            db.ScimGroupMembers.RemoveRange(current);
            foreach (var principalId in distinct)
            {
                db.ScimGroupMembers.Add(new ScimGroupMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = command.TenantId,
                    GroupId = group.Id,
                    PrincipalId = principalId
                });
            }
        }

        await ReconcileGroupAsync(command.TenantId, group.Id, dryRun: false, cancellationToken);
        audit.Add(Audit(command.TenantId, command.ActorUserId, AuditActions.ScimGroupProvision, group.Id, command.Source, now));
        await db.SaveChangesAsync(cancellationToken);
        var memberIds = await db.ScimGroupMembers.Where(x => x.GroupId == group.Id).Select(x => x.PrincipalId).ToListAsync(cancellationToken);
        return new ProvisionGroupResult(true, null, group.Id, created, false, group.DisplayName, group.ExternalId, memberIds);
    }

    public Task<MappingPreview> PreviewMappingAsync(
        TenantId tenantId,
        Guid groupId,
        WorkspaceId workspaceId,
        Role role,
        CancellationToken cancellationToken) =>
        EvaluateMappingAsync(tenantId, groupId, workspaceId, role, persist: false, actor: null, cancellationToken);

    public Task<MappingPreview> ApplyMappingAsync(
        TenantId tenantId,
        Guid groupId,
        WorkspaceId workspaceId,
        Role role,
        UserId actorUserId,
        CancellationToken cancellationToken) =>
        EvaluateMappingAsync(tenantId, groupId, workspaceId, role, persist: true, actorUserId, cancellationToken);

    public async Task<MappingPreview> RemoveMappingAsync(
        TenantId tenantId,
        Guid mappingId,
        WorkspaceId workspaceId,
        UserId actorUserId,
        CancellationToken cancellationToken)
    {
        var mapping = await db.ScimGroupMappings.FirstOrDefaultAsync(
            x => x.Id == mappingId && x.WorkspaceId == workspaceId, cancellationToken);
        if (mapping is null)
        {
            return EmptyPreview("MappingNotFound");
        }

        var grants = await db.ScimMembershipGrants
            .Where(x => x.GroupId == mapping.GroupId && x.WorkspaceId == workspaceId)
            .ToListAsync(cancellationToken);
        await ReleaseGrantsAsync(grants, cancellationToken);
        db.ScimGroupMappings.Remove(mapping);
        audit.Add(Audit(tenantId, actorUserId, AuditActions.ScimMappingApply, mapping.Id, "remove", clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        return new MappingPreview(true, null, null, [], []);
    }

    private async Task<MappingPreview> EvaluateMappingAsync(
        TenantId tenantId,
        Guid groupId,
        WorkspaceId workspaceId,
        Role role,
        bool persist,
        UserId? actor,
        CancellationToken cancellationToken)
    {
        if (!ScimPolicies.IsMappedRole(role))
        {
            return EmptyPreview("ForbiddenRole");
        }

        var group = await db.ScimGroups.FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);
        if (group is null)
        {
            return EmptyPreview("GroupNotFound");
        }

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.Id == workspaceId, cancellationToken);
        if (workspace is null || workspace.TenantId != tenantId)
        {
            return EmptyPreview("WorkspaceNotFound");
        }

        var existing = await db.ScimGroupMappings.FirstOrDefaultAsync(
            x => x.GroupId == groupId && x.WorkspaceId == workspaceId, cancellationToken);
        if (existing is not null && existing.Role != role)
        {
            return EmptyPreview("MappingRoleConflict");
        }

        var preview = await ProjectAsync(tenantId, groupId, workspaceId, role, persist, cancellationToken);
        if (!persist)
        {
            return preview;
        }

        var mapping = existing ?? new ScimGroupMapping
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GroupId = groupId,
            WorkspaceId = workspaceId,
            Role = role,
            CreatedAt = clock.UtcNow
        };
        if (existing is null)
        {
            db.ScimGroupMappings.Add(mapping);
        }

        audit.Add(Audit(tenantId, actor, AuditActions.ScimMappingApply, mapping.Id, "apply", clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        return preview with { MappingId = mapping.Id };
    }

    private async Task<MappingPreview> ProjectAsync(
        TenantId tenantId,
        Guid groupId,
        WorkspaceId workspaceId,
        Role role,
        bool persist,
        CancellationToken cancellationToken)
    {
        var principalIds = await db.ScimGroupMembers.Where(x => x.GroupId == groupId)
            .Select(x => x.PrincipalId)
            .ToListAsync(cancellationToken);
        var principals = await db.ScimPrincipals
            .Where(x => principalIds.Contains(x.Id) && x.Active)
            .ToListAsync(cancellationToken);
        var adds = new List<MappingEffect>();
        var conflicts = new List<MappingEffect>();
        var now = clock.UtcNow;
        var keepUsers = new HashSet<Guid>();

        foreach (var principal in principals)
        {
            keepUsers.Add(principal.UserId.Value);
            var membership = await db.WorkspaceMembers.FirstOrDefaultAsync(
                x => x.WorkspaceId == workspaceId && x.UserId == principal.UserId, cancellationToken);
            if (membership is null)
            {
                adds.Add(new MappingEffect(principal.UserId.Value, "", role.ToString()));
                if (!persist)
                {
                    continue;
                }

                db.WorkspaceMembers.Add(new WorkspaceMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    WorkspaceId = workspaceId,
                    UserId = principal.UserId,
                    Role = role,
                    JoinedAt = now
                });
                db.ScimMembershipGrants.Add(new ScimMembershipGrant
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    GroupId = groupId,
                    WorkspaceId = workspaceId,
                    UserId = principal.UserId,
                    Role = role
                });
                continue;
            }

            if (membership.Role != role)
            {
                conflicts.Add(new MappingEffect(principal.UserId.Value, membership.Role.ToString(), role.ToString()));
            }
        }

        if (persist)
        {
            var grants = await db.ScimMembershipGrants
                .Where(x => x.GroupId == groupId && x.WorkspaceId == workspaceId)
                .ToListAsync(cancellationToken);
            var stale = grants.Where(x => !keepUsers.Contains(x.UserId.Value)).ToList();
            await ReleaseGrantsAsync(stale, cancellationToken);
        }

        return new MappingPreview(true, null, null, adds, conflicts);
    }

    private async Task ReconcileGroupAsync(TenantId tenantId, Guid groupId, bool dryRun, CancellationToken cancellationToken)
    {
        var mappings = await db.ScimGroupMappings.Where(x => x.GroupId == groupId).ToListAsync(cancellationToken);
        foreach (var mapping in mappings)
        {
            await ProjectAsync(tenantId, groupId, mapping.WorkspaceId, mapping.Role, persist: !dryRun, cancellationToken);
        }
    }

    private async Task RemoveGroupAsync(ScimGroup group, CancellationToken cancellationToken)
    {
        var mappings = await db.ScimGroupMappings.Where(x => x.GroupId == group.Id).ToListAsync(cancellationToken);
        foreach (var mapping in mappings)
        {
            var grants = await db.ScimMembershipGrants
                .Where(x => x.GroupId == group.Id && x.WorkspaceId == mapping.WorkspaceId)
                .ToListAsync(cancellationToken);
            await ReleaseGrantsAsync(grants, cancellationToken);
        }

        db.ScimGroupMappings.RemoveRange(mappings);
        var members = await db.ScimGroupMembers.Where(x => x.GroupId == group.Id).ToListAsync(cancellationToken);
        db.ScimGroupMembers.RemoveRange(members);
        db.ScimGroups.Remove(group);
    }

    private async Task ReleaseGrantsAsync(IReadOnlyList<ScimMembershipGrant> grants, CancellationToken cancellationToken)
    {
        foreach (var grant in grants)
        {
            var others = await db.ScimMembershipGrants.AnyAsync(
                x => x.Id != grant.Id
                    && x.UserId == grant.UserId
                    && x.WorkspaceId == grant.WorkspaceId,
                cancellationToken);
            if (!others)
            {
                var membership = await db.WorkspaceMembers.FirstOrDefaultAsync(
                    x => x.WorkspaceId == grant.WorkspaceId && x.UserId == grant.UserId && x.Role == grant.Role,
                    cancellationToken);
                if (membership is not null)
                {
                    db.WorkspaceMembers.Remove(membership);
                }
            }

            db.ScimMembershipGrants.Remove(grant);
        }
    }

    private async Task DeprovisionAsync(TenantId tenantId, UserId userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var memberships = await db.WorkspaceMembers.Where(x => x.TenantId == tenantId && x.UserId == userId).ToListAsync(cancellationToken);
        db.WorkspaceMembers.RemoveRange(memberships);
        var channels = await db.ChannelMembers.Where(x => x.TenantId == tenantId && x.UserId == userId && x.LeftAt == null).ToListAsync(cancellationToken);
        foreach (var channel in channels)
        {
            channel.LeftAt = now;
        }

        var grants = await db.ScimMembershipGrants.Where(x => x.TenantId == tenantId && x.UserId == userId).ToListAsync(cancellationToken);
        db.ScimMembershipGrants.RemoveRange(grants);
        var existing = await db.AccessRevocations.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);
        if (existing is null)
        {
            db.AccessRevocations.Add(new AccessRevocation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                RevokedAt = now,
                Reason = "scim"
            });
        }
        else
        {
            existing.RevokedAt = now;
        }
    }

    private async Task ReactivateAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken)
    {
        var revocation = await db.AccessRevocations.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);
        if (revocation is not null)
        {
            db.AccessRevocations.Remove(revocation);
        }
    }

    private async Task<UserProfile> FindOrCreateProfileAsync(
        string userName,
        string displayName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (userName.Contains('@'))
        {
            var pendingSubject = WorkspaceRolePolicies.PendingSubjectForEmail(userName);
            var pending = await db.UserProfiles.FirstOrDefaultAsync(
                x => x.Subject == pendingSubject, cancellationToken);
            if (pending is not null)
            {
                return pending;
            }
        }

        var profile = new UserProfile
        {
            Id = UserId.New(),
            Subject = $"scim:{Guid.NewGuid():N}",
            Email = userName.Contains('@') ? userName : $"{Guid.NewGuid():N}@scim.local",
            DisplayName = displayName,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (profile.Subject.Length > 256)
        {
            profile.Subject = "scim:" + profile.Id.Value.ToString("N");
        }

        db.UserProfiles.Add(profile);
        return profile;
    }

    private static AuditEvent Audit(TenantId tenantId, UserId? actor, string action, Guid entityId, string source, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "Scim",
            EntityId = entityId.ToString(),
            MetadataJson = $"{{\"source\":\"{source}\"}}",
            OccurredAt = now
        };

    private static ProvisionUserResult UserFail(string error) =>
        new(false, error, Guid.Empty, UserId.Empty, false, false, "", "", null, false);

    private static ProvisionGroupResult GroupFail(string error) =>
        new(false, error, Guid.Empty, false, false, "", null, []);

    private static MappingPreview EmptyPreview(string error) =>
        new(false, error, null, [], []);

    private static bool HasControl(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                return true;
            }
        }

        return false;
    }
}
