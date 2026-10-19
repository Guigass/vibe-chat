using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class ScimAdminEndpoints
{
    internal static void MapScimAdmin(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/scim", GetAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/scim/tokens", CreateTokenAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/scim/tokens/{tokenId:guid}/rotate", RotateTokenAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapDelete("/admin/workspaces/{workspaceId:guid}/scim/tokens/{tokenId:guid}", RevokeTokenAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/scim/mappings/dry-run", DryRunAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/scim/mappings", ApplyMappingAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapDelete("/admin/workspaces/{workspaceId:guid}/scim/mappings/{mappingId:guid}", RemoveMappingAsync)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/scim/provisioning/users", ProvisionUserAsync)
            .RequirePermission(Permissions.Workspace.Admin);
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<ScimOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var workspace = await ResolveAsync(workspaceId, http, db, tenant, clock, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var tokens = await db.ScimTokens.AsNoTracking()
            .Where(x => x.RevokedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var groups = await db.ScimGroups.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct);
        var mappings = await db.ScimGroupMappings.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .ToListAsync(ct);
        return Results.Ok(new
        {
            keycloakAdapter = options.Value.KeycloakAdapter,
            tokens = tokens.Select(x => new
            {
                id = x.Id,
                name = x.Name,
                last4 = x.Last4,
                createdAt = x.CreatedAt,
                lastUsedAt = x.LastUsedAt
            }),
            groups = groups.Select(x => new { id = x.Id, displayName = x.DisplayName, externalId = x.ExternalId }),
            mappings = mappings.Select(x => new { id = x.Id, groupId = x.GroupId, role = x.Role.ToString() })
        });
    }

    private static async Task<IResult> CreateTokenAsync(
        Guid workspaceId,
        ScimTokenNameRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<ScimOptions> options,
        IClock clock,
        IAuditWriter audit,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var actor = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), actor.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var name = ScimPolicies.NormalizeName(request.Name);
        if (name is null)
        {
            return Results.BadRequest(new { error = "InvalidTokenName" });
        }

        var active = await db.ScimTokens.CountAsync(x => x.RevokedAt == null, ct);
        if (active >= ScimPolicies.MaxTokensPerTenant)
        {
            return Results.Conflict(new { error = "TokenLimit" });
        }

        var raw = ScimTokenCodec.CreateRaw();
        var now = clock.UtcNow;
        var row = new ScimToken
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            Name = name,
            TokenHash = ScimTokenCodec.Hash(raw),
            Last4 = ScimTokenCodec.Last4(raw),
            Scopes = ScimPolicies.Scopes,
            CreatedByUserId = actor.Id,
            CreatedAt = now
        };
        db.ScimTokens.Add(row);
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            ActorUserId = actor.Id,
            Action = AuditActions.ScimTokenCreate,
            EntityType = "ScimToken",
            EntityId = row.Id.ToString(),
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id = row.Id, name = row.Name, last4 = row.Last4, secret = raw, createdAt = row.CreatedAt });
    }

    private static async Task<IResult> RotateTokenAsync(
        Guid workspaceId,
        Guid tokenId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<ScimOptions> options,
        IClock clock,
        IAuditWriter audit,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var actor = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), actor.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var current = await db.ScimTokens.FirstOrDefaultAsync(x => x.Id == tokenId && x.RevokedAt == null, ct);
        if (current is null)
        {
            return Results.NotFound(new { error = "TokenNotFound" });
        }

        var now = clock.UtcNow;
        current.RevokedAt = now;
        var raw = ScimTokenCodec.CreateRaw();
        var row = new ScimToken
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            Name = current.Name,
            TokenHash = ScimTokenCodec.Hash(raw),
            Last4 = ScimTokenCodec.Last4(raw),
            Scopes = current.Scopes,
            CreatedByUserId = actor.Id,
            CreatedAt = now
        };
        db.ScimTokens.Add(row);
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            ActorUserId = actor.Id,
            Action = AuditActions.ScimTokenRotate,
            EntityType = "ScimToken",
            EntityId = row.Id.ToString(),
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id = row.Id, name = row.Name, last4 = row.Last4, secret = raw, createdAt = row.CreatedAt });
    }

    private static async Task<IResult> RevokeTokenAsync(
        Guid workspaceId,
        Guid tokenId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<ScimOptions> options,
        IClock clock,
        IAuditWriter audit,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var actor = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), actor.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var current = await db.ScimTokens.FirstOrDefaultAsync(x => x.Id == tokenId && x.RevokedAt == null, ct);
        if (current is null)
        {
            return Results.NotFound(new { error = "TokenNotFound" });
        }

        var now = clock.UtcNow;
        current.RevokedAt = now;
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            ActorUserId = actor.Id,
            Action = AuditActions.ScimTokenRevoke,
            EntityType = "ScimToken",
            EntityId = current.Id.ToString(),
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DryRunAsync(
        Guid workspaceId,
        ScimMappingRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IProvisioningPort port,
        IOptions<ScimOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await GateAsync(workspaceId, http, db, tenant, options, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        if (!WorkspaceRolePolicies.TryParseRole(request.Role, out var role))
        {
            return Results.BadRequest(new { error = "ForbiddenRole" });
        }

        var preview = await port.PreviewMappingAsync(gate.Workspace!.TenantId, request.GroupId, gate.Workspace.Id, role, ct);
        return preview.Ok ? Results.Ok(Preview(preview)) : Results.BadRequest(new { error = preview.Error });
    }

    private static async Task<IResult> ApplyMappingAsync(
        Guid workspaceId,
        ScimMappingRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IProvisioningPort port,
        IOptions<ScimOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await GateAsync(workspaceId, http, db, tenant, options, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        if (!WorkspaceRolePolicies.TryParseRole(request.Role, out var role))
        {
            return Results.BadRequest(new { error = "ForbiddenRole" });
        }

        var preview = await port.ApplyMappingAsync(
            gate.Workspace!.TenantId, request.GroupId, gate.Workspace.Id, role, gate.ActorId, ct);
        if (!preview.Ok)
        {
            return preview.Error is "MappingRoleConflict"
                ? Results.Conflict(new { error = preview.Error })
                : Results.BadRequest(new { error = preview.Error });
        }

        return Results.Ok(Preview(preview));
    }

    private static async Task<IResult> RemoveMappingAsync(
        Guid workspaceId,
        Guid mappingId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IProvisioningPort port,
        IOptions<ScimOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await GateAsync(workspaceId, http, db, tenant, options, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var preview = await port.RemoveMappingAsync(gate.Workspace!.TenantId, mappingId, gate.Workspace.Id, gate.ActorId, ct);
        return preview.Ok ? Results.NoContent() : Results.NotFound(new { error = preview.Error });
    }

    private static async Task<IResult> ProvisionUserAsync(
        Guid workspaceId,
        ScimProvisionRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IProvisioningPort port,
        IOptions<ScimOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        var gate = await GateAsync(workspaceId, http, db, tenant, options, clock, ct);
        if (gate.Error is not null)
        {
            return gate.Error;
        }

        var result = await port.ApplyUserAsync(new ProvisionUserCommand(
            gate.Workspace!.TenantId,
            null,
            request.ExternalId,
            request.UserName,
            request.DisplayName,
            request.Active ?? true,
            "internal",
            gate.ActorId), ct);
        if (!result.Ok)
        {
            return result.Error is "UserNameConflict" or "ExternalIdConflict"
                ? Results.Conflict(new { error = result.Error })
                : Results.BadRequest(new { error = result.Error });
        }

        return Results.Ok(new
        {
            id = result.PrincipalId,
            userId = result.UserId.Value,
            userName = result.UserName,
            displayName = result.DisplayName,
            externalId = result.ExternalId,
            active = result.Active,
            created = result.Created
        });
    }

    private static async Task<Workspace?> ResolveAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        return await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
    }

    private static async Task<(Workspace? Workspace, UserId ActorId, IResult? Error)> GateAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<ScimOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return (null, UserId.Empty, Disabled());
        }

        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        return workspace is null
            ? (null, UserId.Empty, Results.Forbid())
            : (workspace, profile.Id, null);
    }

    private static object Preview(MappingPreview preview) => new
    {
        mappingId = preview.MappingId,
        adds = preview.Adds.Select(x => new { userId = x.UserId, role = x.MappedRole }),
        conflicts = preview.Conflicts.Select(x => new { userId = x.UserId, currentRole = x.CurrentRole, mappedRole = x.MappedRole })
    };

    private static IResult Disabled() => Results.NotFound(new { error = "ScimDisabled" });
}

internal sealed record ScimTokenNameRequest(string? Name);
internal sealed record ScimMappingRequest(Guid GroupId, string? Role);
internal sealed record ScimProvisionRequest(string? UserName, string? ExternalId, string? DisplayName, bool? Active);
