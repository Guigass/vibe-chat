using FluentAssertions;
using VibeChat.Identity;

namespace VibeChat.UnitTests;

public sealed class MemberProfileRulesTests
{
    [Fact]
    public void Normalize_trims_and_clears_blank_optional_fields()
    {
        var ok = MemberProfileRules.TryNormalize(
            "  Ada Lovelace  ",
            "  ",
            " escreve código ",
            null,
            out var name,
            out var job,
            out var about,
            out var highlight,
            out var error);

        ok.Should().BeTrue();
        error.Should().BeEmpty();
        name.Should().Be("Ada Lovelace");
        job.Should().BeNull();
        about.Should().Be("escreve código");
        highlight.Should().BeNull();
    }

    [Theory]
    [InlineData("", MemberProfileRules.DisplayNameRequired)]
    [InlineData("   ", MemberProfileRules.DisplayNameRequired)]
    public void Display_name_is_required(string name, string expected)
    {
        MemberProfileRules.TryNormalize(name, null, null, null, out _, out _, out _, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(expected);
    }

    [Fact]
    public void Length_limits_reject_overflow()
    {
        MemberProfileRules.TryNormalize(new string('a', 81), null, null, null, out _, out _, out _, out _, out var name)
            .Should().BeFalse();
        name.Should().Be(MemberProfileRules.DisplayNameTooLong);

        MemberProfileRules.TryNormalize("Ada", new string('b', 81), null, null, out _, out _, out _, out _, out var job)
            .Should().BeFalse();
        job.Should().Be(MemberProfileRules.JobTitleTooLong);

        MemberProfileRules.TryNormalize("Ada", null, new string('c', 501), null, out _, out _, out _, out _, out var about)
            .Should().BeFalse();
        about.Should().Be(MemberProfileRules.AboutTooLong);

        MemberProfileRules.TryNormalize("Ada", null, null, new string('d', 161), out _, out _, out _, out _, out var highlight)
            .Should().BeFalse();
        highlight.Should().Be(MemberProfileRules.HighlightTooLong);
    }

    [Fact]
    public void Control_characters_are_rejected()
    {
        MemberProfileRules.TryNormalize("Ada", "dev\nops", null, null, out _, out _, out _, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(MemberProfileRules.ProfileTextInvalid);
    }

    [Fact]
    public void Png_magic_must_match_declared_type()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        using var ok = new MemoryStream(png);
        MemberProfileRules.TryReadImage(ok, "image/png", out var bytes, out var type, out var error).Should().BeTrue();
        type.Should().Be("image/png");
        bytes.Should().Equal(png);
        error.Should().BeEmpty();

        using var mismatch = new MemoryStream(png);
        MemberProfileRules.TryReadImage(mismatch, "image/jpeg", out _, out _, out var denied).Should().BeFalse();
        denied.Should().Be(MemberProfileRules.AvatarTypeNotAllowed);

        using var empty = new MemoryStream();
        MemberProfileRules.TryReadImage(empty, "image/png", out _, out _, out var missing).Should().BeFalse();
        missing.Should().Be(MemberProfileRules.AvatarEmpty);
    }

    [Fact]
    public void Storage_key_stays_inside_the_tenant_user_prefix()
    {
        var tenant = new VibeChat.SharedKernel.TenantId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var user = new VibeChat.SharedKernel.UserId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var key = MemberProfileRules.BuildStorageKey(tenant, user);
        MemberProfileRules.OwnsKey(tenant, user, key).Should().BeTrue();
        MemberProfileRules.OwnsKey(tenant, user, "../etc/passwd").Should().BeFalse();
        MemberProfileRules.OwnsKey(tenant, user, "other/profiles/nope").Should().BeFalse();
    }
}
