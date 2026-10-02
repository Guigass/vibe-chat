using VibeChat.SharedKernel;

namespace VibeChat.Identity;

/// <summary>
/// Closed wallpaper and accent catalogs (B-185). Unknown ids fall back to the
/// product default on read and are rejected on write. Accent entries are
/// pre-checked for WCAG AA against the active theme's bubble text.
/// </summary>
public static class VisualPreferenceCatalog
{
    public const double MinContrast = 4.5;
    public const string InvalidWallpaper = "InvalidWallpaper";
    public const string InvalidAccent = "InvalidAccent";

    public const string LightInk = "#1c1917";
    public const string LightMuted = "#57534e";
    public const string DarkInk = "#f5f5f4";
    public const string DarkMuted = "#e7e5e4";

    public static readonly IReadOnlyList<string> WallpaperIds = ["tide", "mist", "ember", "slate"];

    public static readonly IReadOnlyList<AccentPaletteEntry> Accents =
    [
        new("ocean", "#dbeafe", "#1e3a5f"),
        new("amber", "#fef3c7", "#78350f"),
        new("rose", "#ffe4e6", "#9f1239"),
        new("slate", "#f5f5f4", "#44403c")
    ];

    public static bool TryNormalizeWallpaper(string? raw, out string? id)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            id = null;
            return true;
        }

        var trimmed = raw.Trim();
        if (WallpaperIds.Contains(trimmed, StringComparer.Ordinal))
        {
            id = trimmed;
            return true;
        }

        id = null;
        return false;
    }

    public static bool TryNormalizeAccent(string? raw, out string? id)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            id = null;
            return true;
        }

        var trimmed = raw.Trim();
        if (Accents.Any(entry => string.Equals(entry.Id, trimmed, StringComparison.Ordinal) && entry.MeetsContrast()))
        {
            id = trimmed;
            return true;
        }

        id = null;
        return false;
    }

    /// <summary>Stored id no longer in the catalog reads as the product default.</summary>
    public static string? ResolveWallpaper(string? stored) =>
        TryNormalizeWallpaper(stored, out var id) ? id : null;

    public static string? ResolveAccent(string? stored) =>
        TryNormalizeAccent(stored, out var id) ? id : null;

    public static double Contrast(string hexA, string hexB)
    {
        var lighter = Math.Max(RelativeLuminance(hexA), RelativeLuminance(hexB));
        var darker = Math.Min(RelativeLuminance(hexA), RelativeLuminance(hexB));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }

    private static double Channel(byte value)
    {
        var s = value / 255d;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static (byte R, byte G, byte B) Parse(string hex)
    {
        var value = hex.TrimStart('#');
        return (
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value[2..4], 16),
            Convert.ToByte(value[4..6], 16));
    }
}

public sealed record AccentPaletteEntry(string Id, string LightBubble, string DarkBubble)
{
    public bool MeetsContrast() =>
        VisualPreferenceCatalog.Contrast(LightBubble, VisualPreferenceCatalog.LightInk) >= VisualPreferenceCatalog.MinContrast
        && VisualPreferenceCatalog.Contrast(LightBubble, VisualPreferenceCatalog.LightMuted) >= VisualPreferenceCatalog.MinContrast
        && VisualPreferenceCatalog.Contrast(DarkBubble, VisualPreferenceCatalog.DarkInk) >= VisualPreferenceCatalog.MinContrast
        && VisualPreferenceCatalog.Contrast(DarkBubble, VisualPreferenceCatalog.DarkMuted) >= VisualPreferenceCatalog.MinContrast;
}

/// <summary>Personal wallpaper and accent for one user inside one tenant (B-185).</summary>
public sealed class UserVisualPreference
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public string? ChatWallpaperId { get; set; }
    public string? AccentColorId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
