using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed record MobileWriteResult(int Status, string? Error, MobileDeviceView? Device);

public sealed class MobileClientService(
    VibeChatDbContext db,
    IAuditWriter audit,
    IIdempotencyStore idempotency,
    IClock clock,
    ITenantContext tenant)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<MobileDeviceView>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.MobileDevices.AsNoTracking()
            .Where(x => x.UserId == tenant.UserId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(MobileRules.ToView).ToArray();
    }

    public async Task<MobileWriteResult> RegisterAsync(
        string? platform,
        string? appVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!MobileRules.IsPlatform(platform))
        {
            return Fail(400, MobileErrors.PlatformInvalid);
        }

        if (!DesktopRules.IsVersion(appVersion))
        {
            return Fail(400, MobileErrors.VersionInvalid);
        }

        var hash = Hash("register", platform!, appVersion!);
        var replay = await ReplayAsync(idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var owned = await db.MobileDevices.CountAsync(x => x.UserId == tenant.UserId, cancellationToken);
        if (owned >= MobileRules.MaxDevicesPerUser)
        {
            return Fail(409, MobileErrors.Limit);
        }

        var row = new MobileDevice
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            UserId = tenant.UserId,
            Platform = platform!,
            AppVersion = appVersion!,
            CreatedAt = clock.UtcNow
        };
        db.MobileDevices.Add(row);
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.MobileDeviceRegister, cancellationToken);
    }

    public async Task<MobileWriteResult> PreferencesAsync(
        Guid deviceId,
        bool biometricLock,
        bool notifications,
        bool camera,
        bool filePicker,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var hash = Hash(
            "prefs",
            deviceId.ToString("D"),
            biometricLock.ToString(),
            notifications.ToString(),
            camera.ToString(),
            filePicker.ToString());
        var replay = await ReplayAsync(idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var row = await db.MobileDevices.FirstOrDefaultAsync(x => x.Id == deviceId, cancellationToken);
        if (row is null)
        {
            return Fail(404, MobileErrors.NotFound);
        }

        if (row.UserId != tenant.UserId)
        {
            return Fail(403, MobileErrors.Forbidden);
        }

        if (row.RevokedAt is not null)
        {
            return Fail(409, MobileErrors.Revoked);
        }

        row.BiometricLock = biometricLock;
        row.NotificationsEnabled = notifications;
        row.CameraEnabled = camera;
        row.FilePickerEnabled = filePicker;
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.MobileDevicePreferences, cancellationToken);
    }

    public async Task<MobileWriteResult> LogoutAsync(
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

        var row = await db.MobileDevices.FirstOrDefaultAsync(x => x.Id == deviceId, cancellationToken);
        if (row is null)
        {
            return Fail(404, MobileErrors.NotFound);
        }

        if (row.UserId != tenant.UserId && !callerIsAdmin)
        {
            return Fail(403, MobileErrors.Forbidden);
        }

        row.RevokedAt ??= clock.UtcNow;
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.MobileDeviceLogout, cancellationToken);
    }

    private async Task<MobileWriteResult> CommitAsync(
        MobileDevice row,
        string idempotencyKey,
        string hash,
        string action,
        CancellationToken cancellationToken)
    {
        var view = MobileRules.ToView(row);
        audit.Add(new AuditEvent
        {
            TenantId = tenant.TenantId,
            ActorUserId = tenant.HasUser ? tenant.UserId : null,
            Action = action,
            EntityType = "mobile_device",
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
        return new MobileWriteResult(200, null, view);
    }

    private async Task<MobileWriteResult?> ReplayAsync(string idempotencyKey, string hash, CancellationToken cancellationToken)
    {
        var existing = await idempotency.FindAsync(tenant.TenantId, Key(idempotencyKey), cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            return Fail(409, MobileErrors.IdempotencyConflict);
        }

        var view = JsonSerializer.Deserialize<MobileDeviceView>(existing.ResultJson, Json);
        return new MobileWriteResult(200, null, view);
    }

    private static string Key(string key) => "mobile:" + key;

    private static string Hash(params string[] parts) =>
        DesktopPackaging.Hash(string.Join('\n', parts));

    private static MobileWriteResult Fail(int status, string error) => new(status, error, null);
}

internal static class MobileServiceCollectionExtensions
{
    internal static IServiceCollection AddMobileInfrastructure(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<MobileOptions>(configuration.GetSection(MobileOptions.SectionName));
        services.AddScoped<MobileClientService>();
        return services;
    }
}
