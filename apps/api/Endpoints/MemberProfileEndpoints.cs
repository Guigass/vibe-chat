using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Api;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class MemberProfileEndpoints
{
    private const string IdempotencyPrefix = "profile:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void MapMemberProfiles(this RouteGroupBuilder v1)
    {
        v1.MapPut("/me/profile", UpdateMineAsync)
            .AllowPermissionGateExempt("caller-only public profile (B-167)");
        v1.MapPost("/me/profile/avatar", UploadAvatarAsync)
            .DisableAntiforgery()
            .AllowPermissionGateExempt("caller-only avatar upload (B-167)");
        v1.MapDelete("/me/profile/avatar", DeleteAvatarAsync)
            .AllowPermissionGateExempt("caller-only avatar delete (B-167)");
        v1.MapGet("/workspaces/{workspaceId:guid}/members/{userId:guid}/profile", ReadAsync);
        v1.MapGet("/workspaces/{workspaceId:guid}/members/{userId:guid}/profile/avatar", ReadAvatarAsync);
    }

    private static async Task<IResult> UpdateMineAsync(
        UpdateMemberProfileRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IAuditWriter audit,
        IIdempotencyStore idempotency,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var tenantId = await RequirePersonalTenantAsync(db, tenant, profile.Id, ct);
        if (tenantId is null)
        {
            return Results.Forbid();
        }

        if (!MemberProfileRules.TryNormalize(
                request.DisplayName,
                request.JobTitle,
                request.About,
                request.HighlightMessage,
                out var displayName,
                out var jobTitle,
                out var about,
                out var highlight,
                out var error))
        {
            return Results.BadRequest(new { error });
        }

        if (!TryIdempotencyKey(http, profile.Id, out var idempotencyKey, out var idempotencyError))
        {
            return idempotencyError!;
        }

        var hash = Hash(displayName, jobTitle, about, highlight);
        var replay = await ReplayAsync(idempotency, tenantId.Value, idempotencyKey, hash, ct);
        if (replay is not null)
        {
            return replay;
        }

        profile.DisplayName = displayName;
        profile.UpdatedAt = clock.UtcNow;
        var row = await UpsertAsync(db, tenantId.Value, profile.Id, clock, ct);
        row.JobTitle = jobTitle;
        row.About = about;
        row.HighlightMessage = highlight;
        row.UpdatedAt = clock.UtcNow;
        audit.Add(new AuditEvent
        {
            TenantId = tenantId.Value,
            ActorUserId = profile.Id,
            Action = AuditActions.ProfileUpdate,
            EntityType = "MemberPublicProfile",
            EntityId = profile.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new
            {
                fields = new[] { "displayName", "jobTitle", "about", "highlightMessage" }
            })
        });
        var workspaceId = await FirstWorkspaceIdAsync(db, tenantId.Value, ct);
        var body = ToResponse(profile, row, workspaceId);
        await RememberAsync(idempotency, tenantId.Value, idempotencyKey, hash, body, clock, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(body);
    }

    private static async Task<IResult> UploadAvatarAsync(
        IFormFile? file,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IObjectStorage storage,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var tenantId = await RequirePersonalTenantAsync(db, tenant, profile.Id, ct);
        if (tenantId is null)
        {
            return Results.Forbid();
        }

        if (file is null)
        {
            return Results.BadRequest(new { error = MemberProfileRules.AvatarEmpty });
        }

        if (file.Length > MemberProfileRules.MaxAvatarBytes)
        {
            return Results.BadRequest(new { error = MemberProfileRules.AvatarTooLarge });
        }

        await using var input = file.OpenReadStream();
        if (!MemberProfileRules.TryReadImage(input, file.ContentType, out var bytes, out var contentType, out var error))
        {
            return Results.BadRequest(new { error });
        }

        var row = await UpsertAsync(db, tenantId.Value, profile.Id, clock, ct);
        var previous = row.AvatarObjectKey;
        var key = MemberProfileRules.BuildStorageKey(tenantId.Value, profile.Id);
        await using (var payload = new MemoryStream(bytes))
        {
            await storage.PutObjectAsync(key, payload, contentType, ct);
        }

        row.AvatarObjectKey = key;
        row.AvatarContentType = contentType;
        row.UpdatedAt = clock.UtcNow;
        audit.Add(AvatarAudit(tenantId.Value, profile.Id, "set"));
        await db.SaveChangesAsync(ct);
        if (MemberProfileRules.OwnsKey(tenantId.Value, profile.Id, previous))
        {
            await storage.DeleteObjectAsync(previous!, ct);
        }

        return Results.Ok(ToResponse(profile, row, await FirstWorkspaceIdAsync(db, tenantId.Value, ct)));
    }

    private static async Task<IResult> DeleteAvatarAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IObjectStorage storage,
        IAuditWriter audit,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var tenantId = await RequirePersonalTenantAsync(db, tenant, profile.Id, ct);
        if (tenantId is null)
        {
            return Results.Forbid();
        }

        var row = await db.MemberPublicProfiles.FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.AvatarObjectKey))
        {
            return Results.Ok(ToResponse(profile, row, await FirstWorkspaceIdAsync(db, tenantId.Value, ct)));
        }

        var previous = row.AvatarObjectKey;
        row.AvatarObjectKey = null;
        row.AvatarContentType = null;
        row.UpdatedAt = clock.UtcNow;
        audit.Add(AvatarAudit(tenantId.Value, profile.Id, "clear"));
        await db.SaveChangesAsync(ct);
        if (MemberProfileRules.OwnsKey(tenantId.Value, profile.Id, previous))
        {
            await storage.DeleteObjectAsync(previous, ct);
        }

        return Results.Ok(ToResponse(profile, row, await FirstWorkspaceIdAsync(db, tenantId.Value, ct)));
    }

    private static async Task<IResult> ReadAsync(
        Guid workspaceId,
        Guid userId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var caller = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), caller.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var targetId = new UserId(userId);
        var member = await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.UserId == targetId, ct);
        if (!member)
        {
            return Results.NotFound();
        }

        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == targetId, ct);
        if (profile is null)
        {
            return Results.NotFound();
        }

        var row = await db.MemberPublicProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == targetId, ct);
        return Results.Ok(ToResponse(profile, row, workspace.Id.Value));
    }

    private static async Task<IResult> ReadAvatarAsync(
        Guid workspaceId,
        Guid userId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IObjectStorage storage,
        IClock clock,
        CancellationToken ct)
    {
        var caller = await EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await ResolveWorkspaceAsync(new WorkspaceId(workspaceId), caller.Id, db, tenant, ct);
        if (workspace is null)
        {
            return Results.Forbid();
        }

        var targetId = new UserId(userId);
        var member = await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspace.Id && x.UserId == targetId, ct);
        if (!member)
        {
            return Results.NotFound();
        }

        var row = await db.MemberPublicProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == targetId, ct);
        if (row is null
            || !MemberProfileRules.OwnsKey(workspace.TenantId, targetId, row.AvatarObjectKey)
            || string.IsNullOrWhiteSpace(row.AvatarContentType))
        {
            return Results.NotFound();
        }

        var stream = await storage.GetObjectAsync(row.AvatarObjectKey!, ct);
        if (stream is null)
        {
            return Results.NotFound();
        }

        return Results.File(stream, row.AvatarContentType);
    }

    private static async Task<TenantId?> RequirePersonalTenantAsync(
        VibeChatDbContext db,
        ITenantContext tenant,
        UserId userId,
        CancellationToken ct)
    {
        await BeginRlsUserAsync(db, tenant, userId, ct);
        var tenantId = await db.WorkspaceMembers.IgnoreQueryFilters()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.JoinedAt)
            .Select(x => x.TenantId)
            .FirstOrDefaultAsync(ct);
        if (tenantId.Value == Guid.Empty)
        {
            return null;
        }

        tenant.SetTenant(tenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        return tenantId;
    }

    private static async Task<Guid?> FirstWorkspaceIdAsync(VibeChatDbContext db, TenantId tenantId, CancellationToken ct)
    {
        var id = await db.Workspaces.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return id.Value == Guid.Empty ? null : id.Value;
    }

    private static async Task<MemberPublicProfile> UpsertAsync(
        VibeChatDbContext db,
        TenantId tenantId,
        UserId userId,
        IClock clock,
        CancellationToken ct)
    {
        var row = await db.MemberPublicProfiles.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is not null)
        {
            return row;
        }

        row = new MemberPublicProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            UpdatedAt = clock.UtcNow
        };
        db.MemberPublicProfiles.Add(row);
        return row;
    }

    private static MemberProfileResponse ToResponse(UserProfile profile, MemberPublicProfile? row, Guid? workspaceId)
    {
        string? avatarUrl = null;
        if (workspaceId is Guid ws && row is not null && !string.IsNullOrWhiteSpace(row.AvatarObjectKey))
        {
            avatarUrl = MemberProfileRules.AvatarPath(ws, profile.Id.Value, row.UpdatedAt);
        }

        return new MemberProfileResponse(
            profile.Id.Value,
            profile.DisplayName,
            profile.Email,
            row?.JobTitle,
            row?.About,
            row?.HighlightMessage,
            avatarUrl);
    }

    private static AuditEvent AvatarAudit(TenantId tenantId, UserId userId, string stage) =>
        new()
        {
            TenantId = tenantId,
            ActorUserId = userId,
            Action = AuditActions.ProfileAvatar,
            EntityType = "MemberPublicProfile",
            EntityId = userId.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { stage })
        };

    private static bool TryIdempotencyKey(HttpContext http, UserId userId, out string? key, out IResult? error)
    {
        key = null;
        error = null;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var values))
        {
            return true;
        }

        var raw = values.ToString().Trim();
        if (raw.Length == 0 || raw.Length > 150)
        {
            error = Results.BadRequest(new { error = "InvalidIdempotencyKey" });
            return false;
        }

        key = $"{IdempotencyPrefix}{userId.Value:N}:{raw}";
        return true;
    }

    private static string Hash(string displayName, string? jobTitle, string? about, string? highlight)
    {
        var raw = $"{displayName}|{jobTitle}|{about}|{highlight}";
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

    private static async Task RememberAsync(
        IIdempotencyStore idempotency,
        TenantId tenantId,
        string? key,
        string hash,
        MemberProfileResponse response,
        IClock clock,
        CancellationToken ct)
    {
        if (key is null)
        {
            return;
        }

        var body = JsonSerializer.Serialize(response, Json);
        var envelope = JsonSerializer.Serialize(new IdempotencyEnvelope(StatusCodes.Status200OK, body), Json);
        await idempotency.StoreAsync(new IdempotencyRecord(tenantId, key, hash, envelope, clock.UtcNow), ct);
    }

    private sealed record IdempotencyEnvelope(int Status, string Body);
}
