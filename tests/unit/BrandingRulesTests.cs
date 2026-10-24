using FluentAssertions;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class BrandingRulesTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData("#0f766e")]
    [InlineData("#0d9488")]
    [InlineData("#888888")]
    public void Readable_colors_pass_both_surfaces(string hex)
    {
        BrandingRules.Contrast(hex, BrandingRules.LightSurface).Should().BeGreaterThanOrEqualTo(BrandingRules.MinSurfaceContrast);
        BrandingRules.Contrast(hex, BrandingRules.DarkSurface).Should().BeGreaterThanOrEqualTo(BrandingRules.MinSurfaceContrast);
        BrandingRules.TryNormalizeIdentity("Acme", hex, "#0f766e", out _, out var brand, out _, out var error).Should().BeTrue();
        error.Should().BeNull();
        brand.Should().Be(hex);
    }

    [Theory]
    [InlineData("#ffffff")]
    [InlineData("#000000")]
    [InlineData("#2dd4bf")]
    [InlineData("#bbbbbb")]
    [InlineData("red")]
    [InlineData("javascript:alert(1)")]
    public void Weak_or_arbitrary_colors_do_not_save(string hex)
    {
        BrandingRules.TryNormalizeIdentity("Acme", hex, "#0f766e", out _, out _, out _, out var error).Should().BeFalse();
        error.Should().Be(BrandingRules.ContrastFailed);
    }

    [Fact]
    public void Name_rejects_markup_and_keeps_product_identity_out_of_the_field()
    {
        BrandingRules.TryNormalizeIdentity("<script>", "#0f766e", null, out _, out _, out _, out var error).Should().BeFalse();
        error.Should().Be(BrandingRules.NameInvalid);
        BrandingRules.TryNormalizeIdentity("  ", "#0f766e", null, out var name, out _, out _, out _).Should().BeTrue();
        name.Should().BeNull();
        BrandingRules.ProductName.Should().Be("VibeChat");
    }

    [Fact]
    public void Host_map_uses_only_the_operator_host()
    {
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Acme.Example"] = "11111111-1111-1111-1111-111111111111",
            ["bad"] = "not-a-guid"
        };

        BrandingRules.TryResolveHost("acme.example:443", hosts, out var tenant).Should().BeTrue();
        tenant.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        BrandingRules.TryResolveHost("other.example", hosts, out _).Should().BeFalse();
        BrandingRules.TryResolveHost("bad", hosts, out _).Should().BeFalse();
    }

    [Fact]
    public void Png_fits_the_slot_and_script_svg_and_oversize_do_not()
    {
        BrandingRules.ValidateAsset(BrandingRules.LogoLight, "image/png", Png).Should().BeNull();
        BrandingRules.ValidateAsset(BrandingRules.Favicon, "image/svg+xml", "<svg><script>"u8.ToArray()).Should().Be(BrandingRules.AssetTypeNotAllowed);
        var scripted = Png.Concat("<script>"u8.ToArray()).ToArray();
        BrandingRules.ValidateAsset(BrandingRules.LogoLight, "image/png", scripted).Should().Be(BrandingRules.AssetRejected);
        var wide = PngHeader(800, 16);
        BrandingRules.ValidateAsset(BrandingRules.LogoLight, "image/png", wide).Should().Be(BrandingRules.AssetDimensions);
        BrandingRules.ValidateAsset("stylesheet", "image/png", Png).Should().Be(BrandingRules.AssetKindInvalid);
    }

    [Fact]
    public void Admin_role_holds_brand_scope_and_member_auditor_do_not()
    {
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Admin.Brand);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Admin.Brand);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Admin.Brand);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Admin.Brand);
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Admin.Brand);
    }

    private static byte[] PngHeader(int width, int height)
    {
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        var chunk = Chunk("IHDR"u8.ToArray(), ihdr);
        var end = Chunk("IEND"u8.ToArray(), []);
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        return sig.Concat(chunk).Concat(end).ToArray();
    }

    private static byte[] Chunk(byte[] type, byte[] data)
    {
        var length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        return length.Concat(type).Concat(data).Concat(new byte[4]).ToArray();
    }
}
