using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Directory;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

/// <summary>
/// Contact groups (B-166). Department and personal groups organize the people list.
/// They never grant channel, DM, or workspace access.
/// </summary>
internal static class ContactGroupEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string IdempotencyPrefix = "cg:";

    internal static void MapContactGroups(this RouteGroupBuilder v1)
    {
        v1.MapGet("/workspaces/{workspaceId:guid}/contact-groups", ListGroups);

        v1.MapPost("/workspaces/{workspaceId:guid}/contact-groups", CreateGroup)
            .AllowPermissionGateExempt("department requires workspace.admin; personal is owner-only (B-166)");

        v1.MapPatch("/workspaces/{workspaceId:guid}/contact-groups/{groupId:guid}", UpdateGroup)
            .AllowPermissionGateExempt("department requires workspace.admin; personal is owner-only (B-166)");

        v1.MapDelete("/workspaces/{workspaceId:guid}/contact-groups/{groupId:guid}", DeleteGroup)
            .AllowPermissionGateExempt("department requires workspace.admin; personal is owner-only (B-166)");

        v1.MapPut("/workspaces/{workspaceId:guid}/contact-groups/{groupId:guid}/members", ReplaceMembers)
            .AllowPermissionGateExempt("department requires workspace.admin; personal is owner-only (B-166)");

        v1.MapGet("/workspaces/{workspaceId:guid}/contacts", ListGroupedContacts);
    }

    private static async Task<IResult> ListGroups(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var access = await RequireMemberAsync(workspaceId, http, db, tenant, clock, ct);
        if (access is null)
        {
            return Results.Forbid();
        }

        var groups = await VisibleGroupsAsync(db, access.Value.Workspace.Id, access.Value.Profile.Id, ct);
        var members = await MemberIdsByGroupAsync(db, groups.Select(x => x.Id).ToArray(), ct);
        return Results.Ok(groups.Select(group => ToResponse(group, members)).ToArray());
    }

    private static async Task<IResult> CreateGroup(
        Guid workspaceId,
        CreateContactGroupRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IIdempotencyStore idempotency,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var access = await RequireMemberAsync(workspaceId, http, db, tenant, clock, ct);
        if (access is null)
        {
            return Results.Forbid();
        }

        var (profile, workspace, isAdmin) = access.Value;
        if (!ContactGroupPolicies.TryParseKind(request.Kind, out var kind))
        {
            return Results.BadRequest(new { error = "InvalidKind" });
        }

        if (kind == ContactGroupKind.Department && !isAdmin)
        {
            return Results.Forbid();
        }

        var name = ContactGroupPolicies.NormalizeName(request.Name);
        if (name is null)
        {
            return Results.BadRequest(new { error = string.IsNullOrWhiteSpace(request.Name) ? "NameRequired" : "NameTooLong" });
        }

        if (request.Order is int order && !ContactGroupPolicies.OrderInRange(order))
        {
            return Results.BadRequest(new { error = "InvalidOrder" });
        }

        var owner = ContactGroupPolicies.EffectiveOwner(kind, profile.Id);
        if (!TryIdempotencyKey(http, workspace.Id.Value, out var idempotencyKey, out var idempotencyError))
        {
            return idempotencyError!;
        }

        var hash = Hash("create", kind, name, request.Order, null);
        var replay = await ReplayAsync(idempotency, workspace.TenantId, idempotencyKey, hash, ct);
        if (replay is not null)
        {
            return replay;
        }

        if (await NameTakenAsync(db, workspace.Id, kind, owner, name, null, ct))
        {
            return Results.Conflict(new { error = "NameTaken" });
        }

        var nextOrder = request.Order ?? await NextOrderAsync(db, workspace.Id, kind, owner, ct);
        var now = clock.UtcNow;
        var group = new ContactGroup
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            Kind = kind,
            Name = name,
            Order = nextOrder,
            OwnerUserId = owner,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ContactGroups.Add(group);
        if (kind == ContactGroupKind.Department)
        {
            audit.Add(DepartmentAudit(workspace.TenantId, profile.Id, AuditActions.ContactGroupCreate, group, 0));
        }

        var response = ToResponse(group, []);
        Remember(idempotency, workspace.TenantId, idempotencyKey, hash, StatusCodes.Status201Created, response, clock);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsContactGroupNameConflict(ex))
        {
            return Results.Conflict(new { error = "NameTaken" });
        }

        return Results.Created($"/api/v1/workspaces/{workspaceId}/contact-groups/{group.Id}", response);
    }

    private static async Task<IResult> UpdateGroup(
        Guid workspaceId,
        Guid groupId,
        UpdateContactGroupRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var access = await RequireMemberAsync(workspaceId, http, db, tenant, clock, ct);
        if (access is null)
        {
            return Results.Forbid();
        }

        var (profile, workspace, isAdmin) = access.Value;
        var group = await FindGroupAsync(db, workspace.Id, groupId, ct);
        if (group is null || !ContactGroupPolicies.CanRead(group.Kind, Owner(group), profile.Id.Value))
        {
            return Results.NotFound();
        }

        if (!ContactGroupPolicies.CanWrite(group.Kind, Owner(group), profile.Id.Value, isAdmin))
        {
            return group.Kind == ContactGroupKind.Department ? Results.Forbid() : Results.NotFound();
        }

        var hasName = request.Name is not null;
        var hasOrder = request.Order is not null;
        if (!hasName && !hasOrder)
        {
            return Results.BadRequest(new { error = "InvalidRequest" });
        }

        if (hasName)
        {
            var name = ContactGroupPolicies.NormalizeName(request.Name);
            if (name is null)
            {
                return Results.BadRequest(new { error = string.IsNullOrWhiteSpace(request.Name) ? "NameRequired" : "NameTooLong" });
            }

            if (!ContactGroupPolicies.NamesConflict(group.Name, name)
                && await NameTakenAsync(db, workspace.Id, group.Kind, group.OwnerUserId, name, group.Id, ct))
            {
                return Results.Conflict(new { error = "NameTaken" });
            }

            group.Name = name;
        }

        if (request.Order is int order)
        {
            if (!ContactGroupPolicies.OrderInRange(order))
            {
                return Results.BadRequest(new { error = "InvalidOrder" });
            }

            group.Order = order;
        }

        group.UpdatedAt = clock.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsContactGroupNameConflict(ex))
        {
            return Results.Conflict(new { error = "NameTaken" });
        }

        var members = await MemberIdsByGroupAsync(db, [group.Id], ct);
        return Results.Ok(ToResponse(group, members));
    }

    private static async Task<IResult> DeleteGroup(
        Guid workspaceId,
        Guid groupId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var access = await RequireMemberAsync(workspaceId, http, db, tenant, clock, ct);
        if (access is null)
        {
            return Results.Forbid();
        }

        var (profile, workspace, isAdmin) = access.Value;
        var group = await FindGroupAsync(db, workspace.Id, groupId, ct);
        if (group is null || !ContactGroupPolicies.CanRead(group.Kind, Owner(group), profile.Id.Value))
        {
            return Results.NotFound();
        }

        if (!ContactGroupPolicies.CanWrite(group.Kind, Owner(group), profile.Id.Value, isAdmin))
        {
            return group.Kind == ContactGroupKind.Department ? Results.Forbid() : Results.NotFound();
        }

        if (group.Kind == ContactGroupKind.Department)
        {
            var count = await db.ContactGroupMembers.CountAsync(x => x.GroupId == group.Id, ct);
            audit.Add(DepartmentAudit(workspace.TenantId, profile.Id, AuditActions.ContactGroupDelete, group, count));
        }

        db.ContactGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReplaceMembers(
        Guid workspaceId,
        Guid groupId,
        ReplaceContactGroupMembersRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IIdempotencyStore idempotency,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var access = await RequireMemberAsync(workspaceId, http, db, tenant, clock, ct);
        if (access is null)
        {
            return Results.Forbid();
        }

        var (profile, workspace, isAdmin) = access.Value;
        var group = await FindGroupAsync(db, workspace.Id, groupId, ct);
        if (group is null || !ContactGroupPolicies.CanRead(group.Kind, Owner(group), profile.Id.Value))
        {
            return Results.NotFound();
        }

        if (!ContactGroupPolicies.CanWrite(group.Kind, Owner(group), profile.Id.Value, isAdmin))
        {
            return group.Kind == ContactGroupKind.Department ? Results.Forbid() : Results.NotFound();
        }

        var requested = (request.UserIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (requested.Length > ContactGroupPolicies.MaxMemberCount)
        {
            return Results.BadRequest(new { error = "TooManyMembers" });
        }

        if (!TryIdempotencyKey(http, workspace.Id.Value, out var idempotencyKey, out var idempotencyError))
        {
            return idempotencyError!;
        }

        var hash = Hash("members", group.Kind, group.Id.ToString(), null, requested);
        var replay = await ReplayAsync(idempotency, workspace.TenantId, idempotencyKey, hash, ct);
        if (replay is not null)
        {
            return replay;
        }

        var userIds = requested.Select(id => new UserId(id)).ToArray();
        var valid = await db.WorkspaceMembers.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && userIds.Contains(x.UserId))
            .Select(x => x.UserId)
            .ToListAsync(ct);
        if (valid.Count != userIds.Length)
        {
            return Results.BadRequest(new { error = "InvalidMembers" });
        }

        var existing = await db.ContactGroupMembers.Where(x => x.GroupId == group.Id).ToListAsync(ct);
        db.ContactGroupMembers.RemoveRange(existing);
        foreach (var userId in userIds)
        {
            db.ContactGroupMembers.Add(new ContactGroupMember
            {
                Id = Guid.NewGuid(),
                TenantId = workspace.TenantId,
                WorkspaceId = workspace.Id,
                GroupId = group.Id,
                UserId = userId
            });
        }

        group.UpdatedAt = clock.UtcNow;
        if (group.Kind == ContactGroupKind.Department)
        {
            audit.Add(DepartmentAudit(workspace.TenantId, profile.Id, AuditActions.ContactGroupMembersReplace, group, userIds.Length));
        }

        var response = ToResponse(group, userIds.Select(x => x.Value).OrderBy(x => x).ToArray());
        Remember(idempotency, workspace.TenantId, idempotencyKey, hash, StatusCodes.Status200OK, response, clock);
        await db.SaveChangesAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> ListGroupedContacts(
        Guid workspaceId,
        bool? grouped,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        if (grouped == false)
        {
            return Results.BadRequest(new { error = "InvalidQuery" });
        }

        var access = await RequireMemberAsync(workspaceId, http, db, tenant, clock, ct);
        if (access is null)
        {
            return Results.Forbid();
        }

        var (profile, workspace, _) = access.Value;
        var people = await (
            from m in db.WorkspaceMembers.AsNoTracking()
            where m.WorkspaceId == workspace.Id
            join u in db.UserProfiles.AsNoTracking() on m.UserId equals u.Id
            orderby u.DisplayName
            select new WorkspaceMemberResponse(u.Id.Value, u.DisplayName, u.Email, m.Role.ToString())
        ).ToArrayAsync(ct);

        var groups = await VisibleGroupsAsync(db, workspace.Id, profile.Id, ct);
        var links = await db.ContactGroupMembers.AsNoTracking()
            .Where(x => groups.Select(g => g.Id).Contains(x.GroupId))
            .Select(x => new { x.GroupId, UserId = x.UserId.Value })
            .ToListAsync(ct);
        var byGroup = links.GroupBy(x => x.GroupId).ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToHashSet());
        var peopleById = people.ToDictionary(x => x.UserId);
        var placed = new HashSet<Guid>();
        var sections = new List<ContactSectionResponse>(groups.Count + 1);
        foreach (var group in groups)
        {
            var ids = byGroup.GetValueOrDefault(group.Id) ?? [];
            var members = ids
                .Where(peopleById.ContainsKey)
                .Select(id => peopleById[id])
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var member in members)
            {
                placed.Add(member.UserId);
            }

            sections.Add(new ContactSectionResponse(
                group.Id,
                group.Name,
                ContactGroupPolicies.ToWire(group.Kind),
                members));
        }

        var ungrouped = people.Where(x => !placed.Contains(x.UserId)).ToArray();
        if (ungrouped.Length > 0)
        {
            sections.Add(new ContactSectionResponse(null, null, null, ungrouped));
        }

        return Results.Ok(sections);
    }

    private static async Task<(UserProfile Profile, Workspace Workspace, bool IsAdmin)?> RequireMemberAsync(
        Guid workspaceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        if (workspace is null)
        {
            return null;
        }

        var actor = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == profile.Id, ct);
        var isAdmin = actor is not null && WorkspaceRolePolicies.CanManageRoles(actor.Role);
        return (profile, workspace, isAdmin);
    }

    private static Task<ContactGroup?> FindGroupAsync(VibeChatDbContext db, WorkspaceId workspaceId, Guid groupId, CancellationToken ct) =>
        db.ContactGroups.FirstOrDefaultAsync(x => x.Id == groupId && x.WorkspaceId == workspaceId, ct);

    private static Task<List<ContactGroup>> VisibleGroupsAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        UserId caller,
        CancellationToken ct) =>
        db.ContactGroups.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId
                && (x.Kind == ContactGroupKind.Department || x.OwnerUserId == caller))
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Order)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

    private static async Task<Dictionary<Guid, Guid[]>> MemberIdsByGroupAsync(
        VibeChatDbContext db,
        Guid[] groupIds,
        CancellationToken ct)
    {
        if (groupIds.Length == 0)
        {
            return [];
        }

        var rows = await db.ContactGroupMembers.AsNoTracking()
            .Where(x => groupIds.Contains(x.GroupId))
            .Select(x => new { x.GroupId, UserId = x.UserId.Value })
            .ToListAsync(ct);
        return rows
            .GroupBy(x => x.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).OrderBy(x => x).ToArray());
    }

    private static async Task<bool> NameTakenAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        ContactGroupKind kind,
        UserId? owner,
        string name,
        Guid? exceptId,
        CancellationToken ct)
    {
        var needle = name.ToLowerInvariant();
        var query = db.ContactGroups.Where(x =>
            x.WorkspaceId == workspaceId
            && x.Kind == kind
            && x.OwnerUserId == owner
            && x.Name.ToLower() == needle);
        if (exceptId is Guid id)
        {
            query = query.Where(x => x.Id != id);
        }

        return await query.AnyAsync(ct);
    }

    private static async Task<int> NextOrderAsync(
        VibeChatDbContext db,
        WorkspaceId workspaceId,
        ContactGroupKind kind,
        UserId? owner,
        CancellationToken ct)
    {
        var max = await db.ContactGroups
            .Where(x => x.WorkspaceId == workspaceId && x.Kind == kind && x.OwnerUserId == owner)
            .Select(x => (int?)x.Order)
            .MaxAsync(ct) ?? -1;
        var next = max + 1;
        return next > ContactGroupPolicies.MaxOrder ? ContactGroupPolicies.MaxOrder : next;
    }

    private static Guid? Owner(ContactGroup group) => group.OwnerUserId?.Value;

    private static ContactGroupResponse ToResponse(ContactGroup group, IReadOnlyDictionary<Guid, Guid[]> members) =>
        ToResponse(group, members.GetValueOrDefault(group.Id) ?? []);

    private static ContactGroupResponse ToResponse(ContactGroup group, Guid[] memberUserIds) =>
        new(
            group.Id,
            group.WorkspaceId.Value,
            ContactGroupPolicies.ToWire(group.Kind),
            group.Name,
            group.Order,
            group.OwnerUserId?.Value,
            memberUserIds);

    private static AuditEvent DepartmentAudit(TenantId tenantId, UserId actorId, string action, ContactGroup group, int memberCount) =>
        new()
        {
            TenantId = tenantId,
            ActorUserId = actorId,
            Action = action,
            EntityType = "ContactGroup",
            EntityId = group.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                workspaceId = group.WorkspaceId.Value,
                kind = ContactGroupPolicies.ToWire(group.Kind),
                name = group.Name,
                memberCount
            })
        };

    private static bool TryIdempotencyKey(HttpContext http, Guid workspaceId, out string? key, out IResult? error)
    {
        key = null;
        error = null;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var values))
        {
            return true;
        }

        var raw = values.ToString().Trim();
        if (raw.Length == 0 || raw.Length > 160)
        {
            error = Results.BadRequest(new { error = "InvalidIdempotencyKey" });
            return false;
        }

        key = $"{IdempotencyPrefix}{workspaceId:N}:{raw}";
        return true;
    }

    private static string Hash(string op, ContactGroupKind kind, string nameOrId, int? order, Guid[]? userIds)
    {
        var members = userIds is null ? "" : string.Join(',', userIds.OrderBy(x => x));
        var raw = $"{op}|{ContactGroupPolicies.ToWire(kind)}|{nameOrId}|{order}|{members}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static async Task<IResult?> ReplayAsync(
        IIdempotencyStore idempotency,
        TenantId tenantId,
        string? key,
        string hash,
        CancellationToken ct)
    {
        if (key is null)
        {
            return null;
        }

        var existing = await idempotency.FindAsync(tenantId, key, ct);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            return Results.Conflict(new { error = "IdempotencyConflict" });
        }

        var envelope = JsonSerializer.Deserialize<IdempotencyEnvelope>(existing.ResultJson, Json);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Body))
        {
            return null;
        }

        return Results.Content(envelope.Body, "application/json", statusCode: envelope.Status);
    }

    private static void Remember(
        IIdempotencyStore idempotency,
        TenantId tenantId,
        string? key,
        string hash,
        int status,
        ContactGroupResponse response,
        IClock clock)
    {
        if (key is null)
        {
            return;
        }

        var body = JsonSerializer.Serialize(response, Json);
        var envelope = JsonSerializer.Serialize(new IdempotencyEnvelope(status, body), Json);
        idempotency.StoreAsync(
            new IdempotencyRecord(tenantId, key, hash, envelope, clock.UtcNow),
            CancellationToken.None).GetAwaiter().GetResult();
    }

    private static bool IsContactGroupNameConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && (postgres.ConstraintName?.Contains("contact_groups", StringComparison.OrdinalIgnoreCase) ?? false);

    private sealed record IdempotencyEnvelope(int Status, string Body);
}
