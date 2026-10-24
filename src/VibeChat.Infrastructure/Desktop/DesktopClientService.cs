using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed record DesktopWriteResult(int Status, string? Error, DesktopDeviceView? Device);

public sealed class DesktopClientService(
    VibeChatDbContext db,
    IAuditWriter audit,
    IIdempotencyStore idempotency,
    IClock clock,
    ITenantContext tenant)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DesktopDeviceView>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.DesktopDevices.AsNoTracking()
            .Where(x => x.UserId == tenant.UserId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(DesktopRules.ToView).ToArray();
    }

    public async Task<DesktopWriteResult> RegisterAsync(
        string? platform,
        string? appVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!DesktopRules.IsPlatform(platform))
        {
            return Fail(400, DesktopErrors.PlatformInvalid);
        }

        if (!DesktopRules.IsVersion(appVersion))
        {
            return Fail(400, DesktopErrors.VersionInvalid);
        }

        var hash = Hash("register", platform!, appVersion!);
        var replay = await ReplayAsync(idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var owned = await db.DesktopDevices.CountAsync(x => x.UserId == tenant.UserId, cancellationToken);
        if (owned >= DesktopRules.MaxDevicesPerUser)
        {
            return Fail(409, DesktopErrors.Limit);
        }

        var row = new DesktopDevice
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            UserId = tenant.UserId,
            Platform = platform!,
            AppVersion = appVersion!,
            CreatedAt = clock.UtcNow
        };
        db.DesktopDevices.Add(row);
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.DesktopDeviceRegister, cancellationToken);
    }

    public async Task<DesktopWriteResult> PreferencesAsync(
        Guid deviceId,
        bool launchAtLogin,
        bool notifications,
        bool tray,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var hash = Hash("prefs", deviceId.ToString("D"), launchAtLogin.ToString(), notifications.ToString(), tray.ToString());
        var replay = await ReplayAsync(idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var row = await db.DesktopDevices.FirstOrDefaultAsync(x => x.Id == deviceId, cancellationToken);
        if (row is null)
        {
            return Fail(404, DesktopErrors.NotFound);
        }

        if (row.UserId != tenant.UserId)
        {
            return Fail(403, DesktopErrors.Forbidden);
        }

        if (row.RevokedAt is not null)
        {
            return Fail(409, DesktopErrors.Revoked);
        }

        row.LaunchAtLogin = launchAtLogin;
        row.NotificationsEnabled = notifications;
        row.TrayEnabled = tray;
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.DesktopDevicePreferences, cancellationToken);
    }

    public async Task<DesktopWriteResult> LogoutAsync(
        Guid deviceId,
        bool callerIsAdmin,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var hash = Hash("logout", deviceId.ToString("D"));
        var replay = await ReplayAsync(idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var row = await db.DesktopDevices.FirstOrDefaultAsync(x => x.Id == deviceId, cancellationToken);
        if (row is null)
        {
            return Fail(404, DesktopErrors.NotFound);
        }

        if (row.UserId != tenant.UserId && !callerIsAdmin)
        {
            return Fail(403, DesktopErrors.Forbidden);
        }

        row.RevokedAt ??= clock.UtcNow;
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.DesktopDeviceLogout, cancellationToken);
    }

    private async Task<DesktopWriteResult> CommitAsync(
        DesktopDevice row,
        string idempotencyKey,
        string hash,
        string action,
        CancellationToken cancellationToken)
    {
        var view = DesktopRules.ToView(row);
        audit.Add(new AuditEvent
        {
            TenantId = tenant.TenantId,
            ActorUserId = tenant.HasUser ? tenant.UserId : null,
            Action = action,
            EntityType = "desktop_device",
            EntityId = row.Id.ToString("D"),
            MetadataJson = "{}"
        });
        await idempotency.StoreAsync(new IdempotencyRecord(
            tenant.TenantId,
            Key(idempotencyKey),
            hash,
            JsonSerializer.Serialize(view, Json),
            clock.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new DesktopWriteResult(200, null, view);
    }

    private async Task<DesktopWriteResult?> ReplayAsync(string idempotencyKey, string hash, CancellationToken cancellationToken)
    {
        var existing = await idempotency.FindAsync(tenant.TenantId, Key(idempotencyKey), cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            return Fail(409, DesktopErrors.IdempotencyConflict);
        }

        var view = JsonSerializer.Deserialize<DesktopDeviceView>(existing.ResultJson, Json);
        return new DesktopWriteResult(200, null, view);
    }

    private static string Key(string key) => "desktop:" + key;

    private static string Hash(params string[] parts) =>
        DesktopPackaging.Hash(string.Join('\n', parts));

    private static DesktopWriteResult Fail(int status, string error) => new(status, error, null);
}

internal static class DesktopServiceCollectionExtensions
{
    internal static IServiceCollection AddDesktopInfrastructure(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<DesktopOptions>(configuration.GetSection(DesktopOptions.SectionName));
        services.AddScoped<DesktopClientService>();
        return services;
    }
}
