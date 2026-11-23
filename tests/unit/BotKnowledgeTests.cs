using System.Net;
using FluentAssertions;
using VibeChat.AI;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure.Ai;
using VibeChat.Integrations;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class BotKnowledgeTests
{
    [Fact]
    public void Url_policy_blocks_private_metadata_and_rebinding()
    {
        var allow = new[] { "docs.example", "127.0.0.1" };
        BotKnowledgeUrlPolicy.BlockReason(new Uri("https://docs.example/policy"), allow, [IPAddress.Parse("1.1.1.1")], [IPAddress.Parse("1.1.1.1")])
            .Should().BeNull();
        BotKnowledgeUrlPolicy.BlockReason(new Uri("http://docs.example/policy"), allow, [IPAddress.Parse("1.1.1.1")], [IPAddress.Parse("1.1.1.1")])
            .Should().Be(BotKnowledgeErrors.EgressBlocked);
        BotKnowledgeUrlPolicy.BlockReason(new Uri("https://127.0.0.1/secret"), allow, [IPAddress.Parse("127.0.0.1")], [IPAddress.Parse("127.0.0.1")])
            .Should().Be(BotKnowledgeErrors.EgressBlocked);
        BotKnowledgeUrlPolicy.BlockReason(new Uri("https://10.1.2.3/a"), allow, [IPAddress.Parse("10.1.2.3")], [IPAddress.Parse("10.1.2.3")])
            .Should().Be(BotKnowledgeErrors.EgressBlocked);
        BotKnowledgeUrlPolicy.BlockReason(new Uri("https://169.254.169.254/latest"), allow, [IPAddress.Parse("169.254.169.254")], [IPAddress.Parse("169.254.169.254")])
            .Should().Be(BotKnowledgeErrors.EgressBlocked);
        BotKnowledgeUrlPolicy.BlockReason(new Uri("https://metadata.google.internal/"), allow, [IPAddress.Parse("1.1.1.1")], [IPAddress.Parse("1.1.1.1")])
            .Should().Be(BotKnowledgeErrors.EgressBlocked);
        BotKnowledgeUrlPolicy.BlockReason(new Uri("https://docs.example/policy"), allow, [IPAddress.Parse("1.1.1.1")], [IPAddress.Parse("127.0.0.1")])
            .Should().Be(BotKnowledgeErrors.DnsRebinding);
    }

    [Fact]
    public void Checksum_is_stable_and_revoked_hits_are_dropped()
    {
        var first = BotKnowledgePolicies.Checksum(["aaa", "bbb"]);
        BotKnowledgePolicies.Checksum(["aaa", "bbb"]).Should().Be(first);
        BotKnowledgePolicies.Checksum(["bbb", "aaa"]).Should().NotBe(first);

        var cited = BotKnowledgePolicies.Citations(new (string Item, double Score, bool Allowed)[]
        {
            ("secret", 0.99, false),
            ("weak", 0.1, true),
            ("policy", 0.8, true)
        });
        cited.Should().Equal("policy");
        BotKnowledgePolicies.RejectContentType("application/vnd.vibechat.e2ee").Should().Be(BotKnowledgeErrors.E2eeNotIndexed);
    }

    [Fact]
    public void Event_document_has_no_body_and_matches_the_catalog()
    {
        var document = BotKnowledgePolicies.EventDocument(
            Guid.Parse("00000000-0000-0000-0000-00000000000a"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            1,
            Guid.Parse("00000000-0000-0000-0000-000000000004"),
            new string('c', 64),
            "file",
            "Ready");
        document.Should().NotContain("ferias");
        PublicEventCatalog.Validate(PublicEventCatalog.KnowledgeIndexed, 1, document).Accepted.Should().BeTrue();
    }

    [Fact]
    public void Knowledge_permission_stays_with_workspace_admins()
    {
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Ai.ManageKnowledge);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Ai.ManageKnowledge);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Ai.ManageKnowledge);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Ai.ManageKnowledge);
        RolePermissionCatalog.For(Role.Bot).Should().NotContain(Permissions.Ai.ManageKnowledge);
    }
}
