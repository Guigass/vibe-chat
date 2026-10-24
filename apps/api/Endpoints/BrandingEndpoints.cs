using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class BrandingEndpoints
{
    private static readonly string ProductVersion =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "0.1.0";

    internal static void MapBranding(this RouteGroupBuilder v1)
    {
        v1.MapGet("/public/branding", ReadPublicAsync)
            .AllowAnonymous()
            .AllowPermissionGateExempt("host-mapped public brand (B-140)");
        v1.MapGet("/public/branding/assets/{kind}", ReadPublicAssetAsync)
            .AllowAnonymous()
            .AllowPermissionGateExempt("host-mapped public brand asset (B-140)");
        v1.MapGet("/branding", ReadSessionAsync)
            .AllowPermissionGateExempt("membership tenant brand (B-140)");
        v1.MapGet("/branding/assets/{kind}", ReadSessionAssetAsync)
            .AllowPermissionGateExempt("membership tenant brand asset (B-140)");
        v1.MapGet("/admin/branding", ReadAdminAsync)
            .RequirePermission(Permissions.Admin.Brand);
        v1.MapPut("/admin/branding", UpdateAsync)
            .RequirePermission(Permissions.Admin.Brand);
        v1.MapPost("/admin/branding/assets/{kind}", UploadAsync)
            .DisableAntiforgery()
            .RequirePermission(Permissions.Admin.Brand);
        v1.MapDelete("/admin/branding/assets/{kind}", ClearAsync)
            .RequirePermission(Permissions.Admin.Brand);
        v1.MapPost("/admin/branding/rollback", RollbackAsync)
            .RequirePermission(Permissions.Admin.Brand);
    }

    private static async Task<IResult> ReadPublicAsync(
        HttpContext http,
        IOptions<BrandingOptions> options,
        VibeChatDbContext db,
        ITenantContext tenant,
        BrandingService branding,
        CancellationToken ct)
    {
        if (!await UseHostTenantAsync(http, options.Value, db, tenant, ct))
        {
            return Results.Ok(Body(Empty, publicAsset: true));
        }

        return Results.Ok(Body(await branding.ReadAsync(ct), publicAsset: true));
    }

    private static async Task<IResult> ReadPublicAssetAsync(
        string kind,
        HttpContext http,
        IOptions<BrandingOptions> options,
        VibeChatDbContext db,
        ITenantContext tenant,
        BrandingService branding,
        CancellationToken ct)
    {
        if (!BrandingRules.IsKind(kind) || !await UseHostTenantAsync(http, options.Value, db, tenant, ct))
        {
            return Results.NotFound();
        }

        var view = await branding.ReadAsync(ct);
        return await FileAsync(http, branding, kind, view.AssetVersion, publicAsset: true, ct);
    }

    private static async Task<IResult> ReadSessionAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        BrandingService branding,
        CancellationToken ct)
    {
        if (!await UseSessionTenantAsync(http, db, tenant, clock, ct))
        {
            return Results.Ok(Body(Empty, publicAsset: false));
        }

        return Results.Ok(Body(await branding.ReadAsync(ct), publicAsset: false));
    }

    private static async Task<IResult> ReadSessionAssetAsync(
        string kind,
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        BrandingService branding,
        CancellationToken ct)
    {
        if (!BrandingRules.IsKind(kind) || !await UseSessionTenantAsync(http, db, tenant, clock, ct))
        {
            return Results.NotFound();
        }

        var view = await branding.ReadAsync(ct);
        return await FileAsync(http, branding, kind, view.AssetVersion, publicAsset: false, ct);
    }

    private static async Task<IResult> ReadAdminAsync(BrandingService branding, CancellationToken ct) =>
        Results.Ok(Body(await branding.ReadAsync(ct), publicAsset: false));

    private static async Task<IResult> UpdateAsync(
        BrandUpdateBody body,
        HttpContext http,
        BrandingService branding,
        CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Results.BadRequest(new { error = BrandingRules.IdempotencyKeyRequired });
        }

        var result = await branding.UpdateAsync(body.DisplayName, body.BrandColor, body.AccentColor, key, ct);
        return Written(result, publicAsset: false);
    }

    private static async Task<IResult> UploadAsync(
        string kind,
        IFormFile? file,
        HttpContext http,
        BrandingService branding,
        CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Results.BadRequest(new { error = BrandingRules.IdempotencyKeyRequired });
        }

        if (file is null)
        {
            return Results.BadRequest(new { error = BrandingRules.AssetRejected });
        }

        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var copy = new byte[8192];
        var remaining = BrandingRules.LogoMaxBytes + 1;
        while (remaining > 0)
        {
            var read = await input.ReadAsync(copy.AsMemory(0, Math.Min(copy.Length, remaining)), ct);
            if (read == 0)
            {
                break;
            }

            buffer.Write(copy, 0, read);
            remaining -= read;
        }

        var result = await branding.UploadAsync(kind, file.ContentType, buffer.ToArray(), key, ct);
        return Written(result, publicAsset: false);
    }

    private static async Task<IResult> ClearAsync(
        string kind,
        HttpContext http,
        BrandingService branding,
        CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Results.BadRequest(new { error = BrandingRules.IdempotencyKeyRequired });
        }

        return Written(await branding.ClearAssetAsync(kind, key, ct), publicAsset: false);
    }

    private static async Task<IResult> RollbackAsync(HttpContext http, BrandingService branding, CancellationToken ct)
    {
        if (!TryKey(http, out var key))
        {
            return Results.BadRequest(new { error = BrandingRules.IdempotencyKeyRequired });
        }

        return Written(await branding.RollbackAsync(key, ct), publicAsset: false);
    }

    private static async Task<bool> UseHostTenantAsync(
        HttpContext http,
        BrandingOptions options,
        VibeChatDbContext db,
        ITenantContext tenant,
        CancellationToken ct)
    {
        if (!BrandingRules.TryResolveHost(http.Request.Host.Value, options.Hosts, out var tenantId))
        {
            return false;
        }

        tenant.SetTenant(new TenantId(tenantId));
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        return true;
    }

    private static async Task<bool> UseSessionTenantAsync(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IClock clock,
        CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        await RequestAuth.BeginRlsUserAsync(db, tenant, profile.Id, ct);
        var tenantId = await db.WorkspaceMembers.IgnoreQueryFilters()
            .Where(x => x.UserId == profile.Id)
            .Select(x => x.TenantId)
            .FirstOrDefaultAsync(ct);
        if (tenantId.Value == Guid.Empty)
        {
            tenantId = await db.ChannelMembers.IgnoreQueryFilters()
                .Where(x => x.UserId == profile.Id && x.LeftAt == null)
                .Select(x => x.TenantId)
                .FirstOrDefaultAsync(ct);
        }

        if (tenantId.Value == Guid.Empty)
        {
            return false;
        }

        tenant.SetTenant(tenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        return true;
    }

    private static async Task<IResult> FileAsync(
        HttpContext http,
        BrandingService branding,
        string kind,
        int version,
        bool publicAsset,
        CancellationToken ct)
    {
        var etag = $"\"{version}\"";
        if (http.Request.Headers.IfNoneMatch.ToString() == etag)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        var opened = await branding.OpenAsync(kind, ct);
        if (opened is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.CacheControl = publicAsset ? "public, max-age=300" : "private, max-age=300";
        http.Response.Headers.ETag = etag;
        return Results.File(opened.Value.Bytes, opened.Value.ContentType);
    }

    private static IResult Written(BrandWriteResult result, bool publicAsset) =>
        result.Error is null
            ? Results.Ok(Body(result.View ?? Empty, publicAsset))
            : Results.Json(new { error = result.Error }, statusCode: result.Status);

    private static object Body(BrandView view, bool publicAsset) => new
    {
        displayName = view.DisplayName,
        brandColor = view.BrandColor,
        accentColor = view.AccentColor,
        logoLightUrl = Url(view.HasLogoLight, BrandingRules.LogoLight, view.AssetVersion, publicAsset),
        logoDarkUrl = Url(view.HasLogoDark, BrandingRules.LogoDark, view.AssetVersion, publicAsset),
        faviconUrl = Url(view.HasFavicon, BrandingRules.Favicon, view.AssetVersion, publicAsset),
        assetVersion = view.AssetVersion,
        hasRollback = view.HasRollback,
        productName = BrandingRules.ProductName,
        productVersion = ProductVersion,
        fallback = view.DisplayName is null && view.BrandColor is null && view.AccentColor is null
            && !view.HasLogoLight && !view.HasLogoDark && !view.HasFavicon
    };

    private static string? Url(bool present, string kind, int version, bool publicAsset)
    {
        if (!present)
        {
            return null;
        }

        var root = publicAsset ? "/api/v1/public/branding/assets" : "/api/v1/branding/assets";
        return $"{root}/{kind}?v={version}";
    }

    private static bool TryKey(HttpContext http, out string key)
    {
        key = string.Empty;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count != 1)
        {
            return false;
        }

        key = values[0]?.Trim() ?? string.Empty;
        return key.Length is > 0 and <= 120;
    }

    private static BrandView Empty { get; } = new(null, null, null, false, false, false, 0, false, null, null, null);

    private sealed class BrandUpdateBody
    {
        public string? DisplayName { get; set; }
        public string? BrandColor { get; set; }
        public string? AccentColor { get; set; }
    }
}
