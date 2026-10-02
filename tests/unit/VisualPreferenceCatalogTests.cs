using FluentAssertions;
using VibeChat.Identity;

namespace VibeChat.UnitTests;

public sealed class VisualPreferenceCatalogTests
{
    [Fact]
    public void Known_ids_round_trip_and_blank_resets_to_default()
    {
        VisualPreferenceCatalog.TryNormalizeWallpaper(" tide ", out var wallpaper).Should().BeTrue();
        wallpaper.Should().Be("tide");
        VisualPreferenceCatalog.TryNormalizeAccent("ocean", out var accent).Should().BeTrue();
        accent.Should().Be("ocean");

        VisualPreferenceCatalog.TryNormalizeWallpaper("  ", out var clearedWallpaper).Should().BeTrue();
        clearedWallpaper.Should().BeNull();
        VisualPreferenceCatalog.TryNormalizeAccent(null, out var clearedAccent).Should().BeTrue();
        clearedAccent.Should().BeNull();
    }

    [Fact]
    public void Unknown_ids_fall_back_on_read_and_are_rejected_on_write()
    {
        VisualPreferenceCatalog.TryNormalizeWallpaper("javascript:alert(1)", out _).Should().BeFalse();
        VisualPreferenceCatalog.TryNormalizeAccent("#ff00ff", out _).Should().BeFalse();
        VisualPreferenceCatalog.ResolveWallpaper("not-a-wallpaper").Should().BeNull();
        VisualPreferenceCatalog.ResolveAccent("purple").Should().BeNull();
        VisualPreferenceCatalog.ResolveWallpaper("mist").Should().Be("mist");
    }

    [Fact]
    public void Every_accent_keeps_aa_contrast_on_light_and_dark_bubbles()
    {
        VisualPreferenceCatalog.Accents.Should().NotBeEmpty();
        foreach (var entry in VisualPreferenceCatalog.Accents)
        {
            entry.MeetsContrast().Should().BeTrue(because: entry.Id);
            VisualPreferenceCatalog.Contrast(entry.LightBubble, VisualPreferenceCatalog.LightInk)
                .Should().BeGreaterThanOrEqualTo(VisualPreferenceCatalog.MinContrast);
            VisualPreferenceCatalog.Contrast(entry.DarkBubble, VisualPreferenceCatalog.DarkInk)
                .Should().BeGreaterThanOrEqualTo(VisualPreferenceCatalog.MinContrast);
        }
    }
}
