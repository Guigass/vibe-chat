using FluentAssertions;
using VibeChat.Directory;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class ContactGroupPolicyTests
{
    [Theory]
    [InlineData("department", ContactGroupKind.Department)]
    [InlineData("Department", ContactGroupKind.Department)]
    [InlineData("personal", ContactGroupKind.Personal)]
    [InlineData("PERSONAL", ContactGroupKind.Personal)]
    public void Kind_parses_wire_values(string wire, ContactGroupKind expected)
    {
        ContactGroupPolicies.TryParseKind(wire, out var kind).Should().BeTrue();
        kind.Should().Be(expected);
        ContactGroupPolicies.ToWire(kind).Should().Be(expected == ContactGroupKind.Personal ? "personal" : "department");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("space")]
    [InlineData("group")]
    public void Unknown_kind_is_rejected(string? wire)
    {
        ContactGroupPolicies.TryParseKind(wire, out _).Should().BeFalse();
    }

    [Fact]
    public void Name_is_trimmed_and_limited()
    {
        ContactGroupPolicies.NormalizeName("  Vendas  ").Should().Be("Vendas");
        ContactGroupPolicies.NormalizeName("   ").Should().BeNull();
        ContactGroupPolicies.NormalizeName(new string('a', 81)).Should().BeNull();
        ContactGroupPolicies.NormalizeName(new string('a', 80)).Should().HaveLength(80);
    }

    [Fact]
    public void Names_conflict_without_regard_to_case()
    {
        ContactGroupPolicies.NamesConflict("Vendas", " vendas ").Should().BeTrue();
        ContactGroupPolicies.NamesConflict("Vendas", "Estoque").Should().BeFalse();
    }

    [Fact]
    public void Personal_owner_is_the_caller_and_department_has_none()
    {
        var caller = UserId.New();
        ContactGroupPolicies.EffectiveOwner(ContactGroupKind.Personal, caller).Should().Be(caller);
        ContactGroupPolicies.EffectiveOwner(ContactGroupKind.Department, caller).Should().BeNull();
    }

    [Fact]
    public void Read_and_write_follow_kind_and_owner()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();

        ContactGroupPolicies.CanRead(ContactGroupKind.Department, null, other).Should().BeTrue();
        ContactGroupPolicies.CanWrite(ContactGroupKind.Department, null, other, isWorkspaceAdmin: false).Should().BeFalse();
        ContactGroupPolicies.CanWrite(ContactGroupKind.Department, null, other, isWorkspaceAdmin: true).Should().BeTrue();

        ContactGroupPolicies.CanRead(ContactGroupKind.Personal, owner, owner).Should().BeTrue();
        ContactGroupPolicies.CanRead(ContactGroupKind.Personal, owner, other).Should().BeFalse();
        ContactGroupPolicies.CanWrite(ContactGroupKind.Personal, owner, other, isWorkspaceAdmin: true).Should().BeFalse();
        ContactGroupPolicies.CanWrite(ContactGroupKind.Personal, owner, owner, isWorkspaceAdmin: false).Should().BeTrue();
    }
}
