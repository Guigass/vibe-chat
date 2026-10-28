using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Infrastructure;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using static VibeChat.Api.Endpoints.IdentityEndpointHelpers;
using static VibeChat.Api.Endpoints.MessagingEndpointHelpers;

namespace VibeChat.Api.Endpoints;

internal static class E2eeEndpoints
{
    internal static void MapE2ee(this RouteGroupBuilder v1)
    {
        v1.MapGet("/e2ee/capabilities", Capabilities).RequirePermission(Permissions.Message.Read);
        v1.MapGet("/e2ee/devices", ListDevices).RequirePermission(Permissions.Message.Read);
        v1.MapPost("/e2ee/devices", RegisterDevice).RequirePermission(Permissions.Message.Read);
        v1.MapDelete("/e2ee/devices/{deviceId:guid}", RevokeDevice).RequirePermission(Permissions.Message.Read);
        v1.MapGet("/e2ee/backup", ReadBackup).RequirePermission(Permissions.Message.Read);
        v1.MapPut("/e2ee/backup", WriteBackup).RequirePermission(Permissions.Message.Read);
        v1.MapPost("/e2ee/devices/{deviceId:guid}/verify", VerifyDevice).RequirePermission(Permissions.Message.Read);
        v1.MapGet("/channels/{channelId:guid}/e2ee", ChannelState).RequirePermission(Permissions.Message.Read);
    }

    private static IResult Capabilities(IOptions<E2eeOptions> options) =>
        options.Value.Enabled
            ? Results.Ok(Matrix())
            : Results.NotFound(new { error = E2eeErrors.Disabled });

    private static async Task<IResult> ListDevices(
        HttpContext http,
        VibeChatDbContext db,
        IOptions<E2eeOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound(new { error = E2eeErrors.Disabled });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        if (await RequireTenantAsync(db, http.RequestServices.GetRequiredService<ITenantContext>(), profile.Id, ct) is null)
        {
            return Results.Forbid();
        }

        var rows = await db.E2eeDevices.AsNoTracking()
            .Where(x => x.UserId == profile.Id && x.RevokedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { deviceId = x.Id, fingerprint = x.Fingerprint, createdAt = x.CreatedAt })
            .ToArrayAsync(ct);
        return Results.Ok(new { devices = rows });
    }

    private static async Task<IResult> RegisterDevice(
        RegisterE2eeDeviceRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<E2eeOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound(new { error = E2eeErrors.Disabled });
        }

        var invalid = E2eeRules.ValidatePublicJwk(request.PublicJwk);
        if (request.DeviceId == Guid.Empty || invalid is not null)
        {
            return Results.BadRequest(new { error = invalid ?? E2eeErrors.DeviceInvalid });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        if (await RequireTenantAsync(db, tenant, profile.Id, ct) is null)
        {
            return Results.Forbid();
        }

        var fingerprint = E2eeRules.Fingerprint(request.PublicJwk);
        var existing = await db.E2eeDevices.FirstOrDefaultAsync(x => x.Id == request.DeviceId, ct);
        if (existing is not null && existing.UserId != profile.Id)
        {
            return Results.Json(new { error = E2eeErrors.DeviceInvalid }, statusCode: StatusCodes.Status409Conflict);
        }

        if (existing?.RevokedAt is not null)
        {
            return Results.Json(new { error = E2eeErrors.DeviceInvalid }, statusCode: StatusCodes.Status409Conflict);
        }

        var now = clock.UtcNow;
        var changed = existing is null
            || !string.Equals(existing.PublicJwk, request.PublicJwk.Trim(), StringComparison.Ordinal);
        if (existing is null)
        {
            db.E2eeDevices.Add(new E2eeDevice
            {
                Id = request.DeviceId,
                TenantId = tenant.TenantId,
                UserId = profile.Id,
                PublicJwk = request.PublicJwk.Trim(),
                Fingerprint = fingerprint,
                CreatedAt = now
            });
        }
        else if (changed)
        {
            existing.PublicJwk = request.PublicJwk.Trim();
            existing.Fingerprint = fingerprint;
        }

        if (changed)
        {
            await E2eeDirectory.BumpForUserAsync(db, tenant.TenantId, profile.Id, ct);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { deviceId = request.DeviceId, fingerprint });
    }

    private static async Task<IResult> RevokeDevice(
        Guid deviceId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<E2eeOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound(new { error = E2eeErrors.Disabled });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        if (await RequireTenantAsync(db, tenant, profile.Id, ct) is null)
        {
            return Results.Forbid();
        }

        var device = await db.E2eeDevices.FirstOrDefaultAsync(x => x.Id == deviceId && x.UserId == profile.Id, ct);
        if (device is null)
        {
            return Results.NotFound();
        }

        if (device.RevokedAt is null)
        {
            device.RevokedAt = clock.UtcNow;
            await E2eeDirectory.BumpForUserAsync(db, tenant.TenantId, profile.Id, ct);
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ReadBackup(
        HttpContext http,
        VibeChatDbContext db,
        IOptions<E2eeOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound(new { error = E2eeErrors.Disabled });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        if (await RequireTenantAsync(db, http.RequestServices.GetRequiredService<ITenantContext>(), profile.Id, ct) is null)
        {
            return Results.Forbid();
        }

        var row = await db.E2eeBackups.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
        return row is null
            ? Results.NotFound()
            : Results.Ok(new { blob = row.Blob, warning = E2eeErrors.BackupWarning });
    }

    private static async Task<IResult> WriteBackup(
        WriteE2eeBackupRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<E2eeOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound(new { error = E2eeErrors.Disabled });
        }

        var invalid = E2eeRules.ValidateBackup(request.Blob);
        if (invalid is not null)
        {
            return Results.BadRequest(new { error = invalid });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        if (await RequireTenantAsync(db, tenant, profile.Id, ct) is null)
        {
            return Results.Forbid();
        }

        var row = await db.E2eeBackups.FirstOrDefaultAsync(x => x.UserId == profile.Id, ct);
        var now = clock.UtcNow;
        if (row is null)
        {
            db.E2eeBackups.Add(new E2eeBackup
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                UserId = profile.Id,
                Blob = request.Blob.Trim(),
                UpdatedAt = now
            });
        }
        else
        {
            row.Blob = request.Blob.Trim();
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { warning = E2eeErrors.BackupWarning });
    }

    private static async Task<IResult> VerifyDevice(
        Guid deviceId,
        VerifyE2eeDeviceRequest request,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<E2eeOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound(new { error = E2eeErrors.Disabled });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        if (await RequireTenantAsync(db, tenant, profile.Id, ct) is null)
        {
            return Results.Forbid();
        }

        var device = await db.E2eeDevices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.RevokedAt == null, ct);
        if (device is null || !string.Equals(device.Fingerprint, request.Fingerprint?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = E2eeErrors.DeviceInvalid });
        }

        var row = await db.E2eeVerifications
            .FirstOrDefaultAsync(x => x.UserId == profile.Id && x.DeviceId == deviceId, ct);
        var now = clock.UtcNow;
        if (row is null)
        {
            db.E2eeVerifications.Add(new E2eeVerification
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                UserId = profile.Id,
                DeviceId = deviceId,
                Fingerprint = device.Fingerprint,
                VerifiedAt = now
            });
        }
        else
        {
            row.Fingerprint = device.Fingerprint;
            row.VerifiedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { deviceId, fingerprint = device.Fingerprint, verified = true });
    }

    private static async Task<IResult> ChannelState(
        Guid channelId,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<E2eeOptions> options,
        IClock clock,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound(new { error = E2eeErrors.Disabled });
        }

        var profile = await EnsureProfileAsync(http.User, db, clock, ct);
        var channel = await ResolveChannelAsync(new ChannelId(channelId), profile.Id, db, tenant, ct);
        if (channel is null || channel.Type != ChannelType.ConfidentialE2ee)
        {
            return Results.NotFound();
        }

        var devices = await E2eeDirectory.ActiveDevicesAsync(db, channel.TenantId, channel.Id, ct);
        var verified = await db.E2eeVerifications.AsNoTracking()
            .Where(x => x.UserId == profile.Id)
            .Select(x => new { x.DeviceId, x.Fingerprint })
            .ToListAsync(ct);
        var verifiedIds = verified
            .Where(x => devices.Any(d => d.Id == x.DeviceId && d.Fingerprint == x.Fingerprint))
            .Select(x => x.DeviceId)
            .ToHashSet();
        return Results.Ok(new
        {
            generation = channel.E2eeGeneration,
            protocol = E2eeRules.Protocol,
            envelopeVersion = E2eeRules.EnvelopeVersion,
            reduced = E2eeRules.ReducedCapabilities,
            devices = devices.Select(x => new
            {
                deviceId = x.Id,
                userId = x.UserId.Value,
                publicJwk = x.PublicJwk,
                fingerprint = x.Fingerprint,
                verified = verifiedIds.Contains(x.Id)
            })
        });
    }

    private static async Task<TenantId?> RequireTenantAsync(
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

    private static object Matrix() => new
    {
        enabled = true,
        protocol = E2eeRules.Protocol,
        envelopeVersion = E2eeRules.EnvelopeVersion,
        reduced = E2eeRules.ReducedCapabilities,
        kept = E2eeRules.KeptCapabilities
    };
}

internal sealed record RegisterE2eeDeviceRequest(Guid DeviceId, string PublicJwk);
internal sealed record WriteE2eeBackupRequest(string Blob);
internal sealed record VerifyE2eeDeviceRequest(string Fingerprint);
