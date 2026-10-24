using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public Dictionary<string, string> Hosts { get; set; } = [];
}

public sealed record BrandView(
    string? DisplayName,
    string? BrandColor,
    string? AccentColor,
    bool HasLogoLight,
    bool HasLogoDark,
    bool HasFavicon,
    int AssetVersion,
    bool HasRollback,
    string? LogoLightContentType,
    string? LogoDarkContentType,
    string? FaviconContentType);

public sealed record BrandWriteResult(int Status, string? Error, BrandView? View);

public sealed class BrandingService(
    VibeChatDbContext db,
    IObjectStorage storage,
    IMalwareScanner scanner,
    IAuditWriter audit,
    IIdempotencyStore idempotency,
    IClock clock,
    ITenantContext tenant)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<BrandView> ReadAsync(CancellationToken cancellationToken)
    {
        var row = await db.BrandSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId, cancellationToken);
        return row is null ? Empty() : ToView(row);
    }

    public async Task<BrandWriteResult> UpdateAsync(
        string? displayName,
        string? brandColor,
        string? accentColor,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!BrandingRules.TryNormalizeIdentity(displayName, brandColor, accentColor, out var name, out var brand, out var accent, out var error))
        {
            return Fail(400, error);
        }

        var hash = BrandingRules.Hash($"update\n{name}\n{brand}\n{accent}");
        var replay = await ReplayAsync(idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var row = await LoadAsync(cancellationToken);
        row.PreviousJson = BrandingRules.Capture(row);
        row.DisplayName = name;
        row.BrandColor = brand;
        row.AccentColor = accent;
        row.AssetVersion++;
        Touch(row);
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.BrandUpdate, cancellationToken);
    }

    public async Task<BrandWriteResult> UploadAsync(
        string kind,
        string? contentType,
        byte[] content,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var error = BrandingRules.ValidateAsset(kind, contentType, content);
        if (error is not null)
        {
            return Fail(400, error);
        }

        await using var stream = new MemoryStream(content, writable: false);
        var scan = await scanner.ScanAsync(stream, contentType ?? string.Empty, cancellationToken);
        if (scan.Status is AttachmentScanStatus.Quarantined or AttachmentScanStatus.ScanFailed)
        {
            return Fail(400, BrandingRules.AssetRejected);
        }

        var hash = BrandingRules.Hash($"asset\n{kind}\n{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content))}");
        var replay = await ReplayAsync(idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var row = await LoadAsync(cancellationToken);
        var version = Math.Max(1, row.AssetVersion + 1);
        var key = BrandingRules.StorageKey(tenant.TenantId.Value, kind, version);
        await using var payload = new MemoryStream(content, writable: false);
        await storage.PutObjectAsync(key, payload, NormalizeStoredType(contentType), cancellationToken);
        row.PreviousJson = BrandingRules.Capture(row);
        Assign(row, kind, key, NormalizeStoredType(contentType));
        row.AssetVersion = version;
        Touch(row);
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.BrandAsset, cancellationToken);
    }

    public async Task<BrandWriteResult> ClearAssetAsync(string kind, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!BrandingRules.IsKind(kind))
        {
            return Fail(400, BrandingRules.AssetKindInvalid);
        }

        var hash = BrandingRules.Hash($"clear\n{kind}");
        var replay = await ReplayAsync(idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var row = await LoadAsync(cancellationToken);
        row.PreviousJson = BrandingRules.Capture(row);
        Assign(row, kind, null, null);
        row.AssetVersion++;
        Touch(row);
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.BrandAsset, cancellationToken);
    }

    public async Task<BrandWriteResult> RollbackAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var hash = BrandingRules.Hash("rollback");
        var replay = await ReplayAsync(idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var row = await db.BrandSettings.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId, cancellationToken);
        var snapshot = BrandingRules.ReadSnapshot(row?.PreviousJson);
        if (row is null || snapshot is null)
        {
            return Fail(409, BrandingRules.RollbackEmpty);
        }

        BrandingRules.Apply(row, snapshot);
        row.PreviousJson = null;
        row.AssetVersion++;
        Touch(row);
        return await CommitAsync(row, idempotencyKey, hash, AuditActions.BrandRollback, cancellationToken);
    }

    public async Task<(byte[] Bytes, string ContentType)?> OpenAsync(string kind, CancellationToken cancellationToken)
    {
        if (!BrandingRules.IsKind(kind))
        {
            return null;
        }

        var row = await db.BrandSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var key = Key(row, kind);
        var type = Type(row, kind);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        await using var stream = await storage.GetObjectAsync(key, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        await using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return (copy.ToArray(), type);
    }

    private async Task<BrandSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var row = await db.BrandSettings.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new BrandSettings { TenantId = tenant.TenantId, AssetVersion = 0 };
        db.BrandSettings.Add(row);
        return row;
    }

    private void Touch(BrandSettings row)
    {
        if (row.AssetVersion < 1)
        {
            row.AssetVersion = 1;
        }

        row.UpdatedAt = clock.UtcNow;
        row.UpdatedBy = tenant.HasUser ? tenant.UserId.Value : null;
    }

    private async Task<BrandWriteResult> CommitAsync(
        BrandSettings row,
        string idempotencyKey,
        string hash,
        string action,
        CancellationToken cancellationToken)
    {
        var view = ToView(row);
        audit.Add(new AuditEvent
        {
            TenantId = tenant.TenantId,
            ActorUserId = tenant.HasUser ? tenant.UserId : null,
            Action = action,
            EntityType = "brand_settings",
            EntityId = tenant.TenantId.Value.ToString(),
            MetadataJson = "{}"
        });
        await idempotency.StoreAsync(new IdempotencyRecord(
            tenant.TenantId,
            IdempotencyKey(idempotencyKey),
            hash,
            JsonSerializer.Serialize(view, Json),
            clock.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new BrandWriteResult(200, null, view);
    }

    private async Task<BrandWriteResult?> ReplayAsync(string idempotencyKey, string hash, CancellationToken cancellationToken)
    {
        var existing = await idempotency.FindAsync(tenant.TenantId, IdempotencyKey(idempotencyKey), cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            return Fail(409, BrandingRules.IdempotencyConflict);
        }

        var view = JsonSerializer.Deserialize<BrandView>(existing.ResultJson, Json);
        return new BrandWriteResult(200, null, view);
    }

    private static string IdempotencyKey(string key) => $"brand:{key}";

    private static BrandWriteResult Fail(int status, string? error) => new(status, error, null);

    private static BrandView Empty() => new(null, null, null, false, false, false, 0, false, null, null, null);

    private static BrandView ToView(BrandSettings row) => new(
        row.DisplayName,
        row.BrandColor,
        row.AccentColor,
        row.LogoLightKey is not null,
        row.LogoDarkKey is not null,
        row.FaviconKey is not null,
        row.AssetVersion,
        BrandingRules.ReadSnapshot(row.PreviousJson) is not null,
        row.LogoLightContentType,
        row.LogoDarkContentType,
        row.FaviconContentType);

    private static void Assign(BrandSettings row, string kind, string? key, string? contentType)
    {
        switch (kind)
        {
            case BrandingRules.LogoLight:
                row.LogoLightKey = key;
                row.LogoLightContentType = contentType;
                break;
            case BrandingRules.LogoDark:
                row.LogoDarkKey = key;
                row.LogoDarkContentType = contentType;
                break;
            default:
                row.FaviconKey = key;
                row.FaviconContentType = contentType;
                break;
        }
    }

    private static string? Key(BrandSettings row, string kind) => kind switch
    {
        BrandingRules.LogoLight => row.LogoLightKey,
        BrandingRules.LogoDark => row.LogoDarkKey,
        _ => row.FaviconKey
    };

    private static string? Type(BrandSettings row, string kind) => kind switch
    {
        BrandingRules.LogoLight => row.LogoLightContentType,
        BrandingRules.LogoDark => row.LogoDarkContentType,
        _ => row.FaviconContentType
    };

    private static string NormalizeStoredType(string? contentType)
    {
        var type = (contentType ?? "image/png").Trim().ToLowerInvariant();
        var separator = type.IndexOf(';');
        if (separator >= 0)
        {
            type = type[..separator].Trim();
        }

        return type == "image/jpg" ? "image/jpeg" : type;
    }
}

internal static class BrandingServiceCollectionExtensions
{
    internal static IServiceCollection AddBrandingInfrastructure(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<BrandingOptions>(configuration.GetSection(BrandingOptions.SectionName));
        services.AddScoped<BrandingService>();
        return services;
    }
}
