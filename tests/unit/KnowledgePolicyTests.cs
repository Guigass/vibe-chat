using FluentAssertions;
using VibeChat.BuildingBlocks;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class KnowledgePolicyTests
{
    [Fact]
    public void Restricted_markdown_rejects_html_and_unsafe_schemes()
    {
        KnowledgePolicies.IsSafeBody("## Runbook\n\nUse [docs](https://example.com).").Should().BeTrue();
        KnowledgePolicies.IsSafeBody("<script>alert(1)</script>").Should().BeFalse();
        KnowledgePolicies.IsSafeBody("veja <iframe src='x'>").Should().BeFalse();
        KnowledgePolicies.IsSafeBody("[x](javascript:alert(1))").Should().BeFalse();
        KnowledgePolicies.IsSafeBody("data:text/html,hi").Should().BeFalse();
        KnowledgePolicies.IsSafeBody(new string('a', KnowledgePolicies.MaxBodyLength + 1)).Should().BeFalse();
    }

    [Fact]
    public void References_parse_once_and_ignore_unknown_shapes()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var body = $"veja [[message:{id}]] e de novo [[message:{id}]] [[task:{id}]] [[nope:{id}]]";
        var tokens = KnowledgePolicies.ParseReferences(body);
        tokens.Should().HaveCount(2);
        tokens[0].Kind.Should().Be(KnowledgePolicies.RefMessage);
        tokens[1].Kind.Should().Be(KnowledgePolicies.RefTask);
        tokens[0].TargetId.Should().Be(id);
    }

    [Fact]
    public void Slug_is_bounded_and_ascii()
    {
        KnowledgePolicies.Slugify("Página de FAQ!!!").Should().Be("p-gina-de-faq");
        KnowledgePolicies.Slugify("   ").Should().Be("page");
        KnowledgePolicies.Slugify(new string('A', 200)).Length.Should().BeLessThanOrEqualTo(KnowledgePolicies.MaxSlugLength);
    }

    [Theory]
    [InlineData(Role.Member, true, true, false)]
    [InlineData(Role.Auditor, true, false, false)]
    [InlineData(Role.Admin, true, true, true)]
    [InlineData(Role.Guest, false, false, false)]
    public void Permissions_split_view_edit_and_manage(Role role, bool view, bool edit, bool manage)
    {
        var permissions = RolePermissionCatalog.For(role);
        permissions.Contains(Permissions.Knowledge.View).Should().Be(view);
        permissions.Contains(Permissions.Knowledge.Edit).Should().Be(edit);
        permissions.Contains(Permissions.Knowledge.Manage).Should().Be(manage);
    }
}
