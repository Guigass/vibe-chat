using FluentAssertions;
using VibeChat.Identity;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class ScimProvisioningRulesTests
{
    [Fact]
    public void Mapped_roles_exclude_owners_admin_and_bot()
    {
        ScimPolicies.IsMappedRole(Role.Member).Should().BeTrue();
        ScimPolicies.IsMappedRole(Role.Moderator).Should().BeTrue();
        ScimPolicies.IsMappedRole(Role.Auditor).Should().BeTrue();
        ScimPolicies.IsMappedRole(Role.Guest).Should().BeTrue();
        ScimPolicies.IsMappedRole(Role.Admin).Should().BeFalse();
        ScimPolicies.IsMappedRole(Role.WorkspaceOwner).Should().BeFalse();
        ScimPolicies.IsMappedRole(Role.PlatformOwner).Should().BeFalse();
        ScimPolicies.IsMappedRole(Role.Bot).Should().BeFalse();
    }

    [Fact]
    public void Token_hash_is_not_the_secret_and_uses_the_prefix()
    {
        var raw = ScimTokenCodec.CreateRaw();
        raw.Should().StartWith(ScimPolicies.TokenPrefix);
        var hash = ScimTokenCodec.Hash(raw);
        hash.Should().NotBe(raw);
        hash.Should().HaveLength(64);
        ScimTokenCodec.Last4(raw).Should().Be(raw[^4..]);
    }

    [Fact]
    public void Filter_accepts_eq_and_rejects_other_operators()
    {
        ScimFilter.TryParse(null, out _, out _, out var empty).Should().BeTrue();
        empty.Should().BeEmpty();
        ScimFilter.TryParse("userName eq \"carol@corp.test\"", out var field, out var value, out _)
            .Should().BeTrue();
        field.Should().Be(ScimFilterField.UserName);
        value.Should().Be("carol@corp.test");
        ScimFilter.TryParse("userName sw \"a\"", out _, out _, out var error).Should().BeFalse();
        error.Should().Be("invalidFilter");
    }

    [Fact]
    public void Page_and_bulk_limits_are_bounded()
    {
        ScimPolicies.NormalizePage(0, 10_000).Should().Be((1, ScimPolicies.PageMax));
        ScimPolicies.NormalizePage(3, 5).Should().Be((3, 5));
        ScimPolicies.BulkMax.Should().Be(20);
    }

    [Fact]
    public void User_name_email_is_normalized_and_control_chars_are_rejected()
    {
        ScimPolicies.NormalizeUserName("  Carol@Corp.Test ").Should().Be("carol@corp.test");
        ScimPolicies.NormalizeUserName("bad\nname").Should().BeNull();
        ScimPolicies.NormalizeExternalId("  ext-1 ").Should().Be("ext-1");
        ScimPolicies.ExternalIdRejected("ok").Should().BeFalse();
        ScimPolicies.ExternalIdRejected(new string('x', 300)).Should().BeTrue();
    }
}
