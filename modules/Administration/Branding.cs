using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeChat.Files;
using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>
/// Tenant brand limited to name, logos and two colors (B-140 / D-24).
/// No arbitrary CSS, JS or HTML.
/// </summary>
public sealed class BrandSettings
{
    public TenantId TenantId { get; set; }
    public string? DisplayName { get; set; }
    public string? BrandColor { get; set; }
    public string? AccentColor { get; set; }
    public string? LogoLightKey { get; set; }
    public string? LogoDarkKey { get; set; }
    public string? FaviconKey { get; set; }
    public string? LogoLightContentType { get; set; }
    public string? LogoDarkContentType { get; set; }
    public string? FaviconContentType { get; set; }
    public int AssetVersion { get; set; }
    public string? PreviousJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public static class BrandingRules
{
    public const string ProductName = "VibeChat";
    public const string LogoLight = "logo-light";
    public const string LogoDark = "logo-dark";
    public const string Favicon = "favicon";

    public const string NameInvalid = "BrandNameInvalid";
    public const string ContrastFailed = "BrandContrastFailed";
    public const string AssetKindInvalid = "BrandAssetKindInvalid";
    public const string AssetTypeNotAllowed = "BrandAssetTypeNotAllowed";
    public const string AssetTooLarge = "BrandAssetTooLarge";
    public const string AssetDimensions = "BrandAssetDimensions";
    public const string AssetRejected = "BrandAssetRejected";
    public const string RollbackEmpty = "BrandRollbackEmpty";
    public const string IdempotencyKeyRequired = "IdempotencyKeyRequired";
    public const string IdempotencyConflict = "BrandIdempotencyConflict";

    public const int MaxNameLength = 40;
    public const int LogoMaxBytes = 256 * 1024;
    public const int FaviconMaxBytes = 64 * 1024;
    public const int MaxEdgePx = 512;
    public const double MinAspect = 0.25;
    public const double MaxAspect = 4;
    public const double MinSurfaceContrast = 3.0;

    public static readonly string LightSurface = "#f8fafc";
    public static readonly string DarkSurface = "#0c0f12";

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool TryResolveHost(string? host, IReadOnlyDictionary<string, string>? hosts, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(host) || hosts is null || hosts.Count == 0)
        {
            return false;
        }

        var name = NormalizeHost(host);
        if (name.Length == 0)
        {
            return false;
        }

        foreach (var pair in hosts)
        {
            if (!string.Equals(NormalizeHost(pair.Key), name, StringComparison.Ordinal)
                || !Guid.TryParse(pair.Value, out var id)
                || id == Guid.Empty)
            {
                continue;
            }

            tenantId = id;
            return true;
        }

        return false;
    }

    public static string NormalizeHost(string host)
    {
        var value = host.Trim().TrimEnd('.').ToLowerInvariant();
        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
        {
            value = value[..colon];
        }

        return value;
    }

    public static bool TryNormalizeIdentity(
        string? displayName,
        string? brandColor,
        string? accentColor,
        out string? name,
        out string? brand,
        out string? accent,
        out string? error)
    {
        name = null;
        brand = null;
        accent = null;
        error = null;

        if (!TryName(displayName, out name, out error))
        {
            return false;
        }

        if (!TryColor(brandColor, out brand, out error) || !TryColor(accentColor, out accent, out error))
        {
            return false;
        }

        return true;
    }

    public static string? ValidateAsset(string? kind, string? contentType, ReadOnlySpan<byte> content)
    {
        if (!IsKind(kind))
        {
            return AssetKindInvalid;
        }

        if (content.IsEmpty)
        {
            return AssetRejected;
        }

        var max = kind == Favicon ? FaviconMaxBytes : LogoMaxBytes;
        if (content.Length > max)
        {
            return AssetTooLarge;
        }

        if (!TryNormalizeType(contentType, out var type) || !MagicMatches(type, content))
        {
            return AssetTypeNotAllowed;
        }

        if (ContainsActiveContent(content))
        {
            return AssetRejected;
        }

        var scan = MalwareScanPolicies.Inspect(content, type);
        if (scan.Status is not AttachmentScanStatus.Clean and not AttachmentScanStatus.NotRequired)
        {
            return AssetRejected;
        }

        if (!TryDimensions(type, content, out var width, out var height) || !FitsSlot(width, height))
        {
            return AssetDimensions;
        }

        return null;
    }

    public static bool IsKind(string? kind) =>
        kind is LogoLight or LogoDark or Favicon;

    public static string StorageKey(Guid tenantId, string kind, int version) =>
        $"branding/{tenantId:N}/{kind}/v{version}";

    public static string Hash(string material)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes);
    }

    public static string Capture(BrandSettings row) =>
        JsonSerializer.Serialize(new BrandSnapshot
        {
            DisplayName = row.DisplayName,
            BrandColor = row.BrandColor,
            AccentColor = row.AccentColor,
            LogoLightKey = row.LogoLightKey,
            LogoDarkKey = row.LogoDarkKey,
            FaviconKey = row.FaviconKey,
            LogoLightContentType = row.LogoLightContentType,
            LogoDarkContentType = row.LogoDarkContentType,
            FaviconContentType = row.FaviconContentType
        }, Json);

    public static BrandSnapshot? ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<BrandSnapshot>(json, Json);
    }

    public static void Apply(BrandSettings row, BrandSnapshot snapshot)
    {
        row.DisplayName = snapshot.DisplayName;
        row.BrandColor = snapshot.BrandColor;
        row.AccentColor = snapshot.AccentColor;
        row.LogoLightKey = snapshot.LogoLightKey;
        row.LogoDarkKey = snapshot.LogoDarkKey;
        row.FaviconKey = snapshot.FaviconKey;
        row.LogoLightContentType = snapshot.LogoLightContentType;
        row.LogoDarkContentType = snapshot.LogoDarkContentType;
        row.FaviconContentType = snapshot.FaviconContentType;
    }

    public static bool FitsSlot(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxEdgePx || height > MaxEdgePx)
        {
            return false;
        }

        var aspect = width / (double)height;
        return aspect >= MinAspect && aspect <= MaxAspect;
    }

    private static bool TryName(string? raw, out string? name, out string? error)
    {
        name = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        error = null;
        if (name is null)
        {
            return true;
        }

        if (name.Length > MaxNameLength || name.Any(char.IsControl) || name.Contains('<') || name.Contains('>'))
        {
            error = NameInvalid;
            name = null;
            return false;
        }

        return true;
    }

    private static bool TryColor(string? raw, out string? color, out string? error)
    {
        color = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var value = raw.Trim().ToLowerInvariant();
        if (value.Length != 7 || value[0] != '#')
        {
            error = ContrastFailed;
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                error = ContrastFailed;
                return false;
            }
        }

        if (Contrast(value, LightSurface) < MinSurfaceContrast || Contrast(value, DarkSurface) < MinSurfaceContrast)
        {
            error = ContrastFailed;
            return false;
        }

        color = value;
        return true;
    }

    public static double Contrast(string hex, string other)
    {
        var left = Luminance(hex);
        var right = Luminance(other);
        var lighter = Math.Max(left, right);
        var darker = Math.Min(left, right);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        var r = Channel(hex, 1);
        var g = Channel(hex, 3);
        var b = Channel(hex, 5);
        return (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));
    }

    private static int Channel(string hex, int index) =>
        int.Parse(hex.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static double Linear(int channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static bool TryNormalizeType(string? contentType, out string type)
    {
        type = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        var separator = type.IndexOf(';');
        if (separator >= 0)
        {
            type = type[..separator].Trim();
        }

        if (type is "image/jpg")
        {
            type = "image/jpeg";
        }

        return type is "image/png" or "image/jpeg";
    }

    private static bool MagicMatches(string type, ReadOnlySpan<byte> content)
    {
        if (type == "image/png")
        {
            return content.Length >= PngMagic.Length && content.StartsWith(PngMagic);
        }

        return content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;
    }

    private static bool ContainsActiveContent(ReadOnlySpan<byte> content)
    {
        var sample = content.Length > 8192 ? content[..8192] : content;
        var text = Encoding.ASCII.GetString(sample);
        return text.Contains("<script", StringComparison.OrdinalIgnoreCase)
            || text.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<?php", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDimensions(string type, ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        return type == "image/png" ? TryPngSize(content, out width, out height) : TryJpegSize(content, out width, out height);
    }

    private static bool TryPngSize(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (content.Length < 24 || !content.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(content.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(content.Slice(20, 4));
        return width > 0 && height > 0;
    }

    private static bool TryJpegSize(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        var index = 2;
        while (index + 8 < content.Length)
        {
            if (content[index] != 0xFF)
            {
                index++;
                continue;
            }

            var marker = content[index + 1];
            if (marker is 0xD8 or 0xD9 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                index += 2;
                continue;
            }

            if (index + 3 >= content.Length)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(index + 2, 2));
            if (length < 2)
            {
                return false;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                if (index + 8 >= content.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(index + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(index + 7, 2));
                return width > 0 && height > 0;
            }

            index += 2 + length;
        }

        return false;
    }
}

public sealed class BrandSnapshot
{
    public string? DisplayName { get; set; }
    public string? BrandColor { get; set; }
    public string? AccentColor { get; set; }
    public string? LogoLightKey { get; set; }
    public string? LogoDarkKey { get; set; }
    public string? FaviconKey { get; set; }
    public string? LogoLightContentType { get; set; }
    public string? LogoDarkContentType { get; set; }
    public string? FaviconContentType { get; set; }
}
