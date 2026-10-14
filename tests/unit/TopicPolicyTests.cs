using FluentAssertions;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class TopicPolicyTests
{
    [Fact]
    public void Slug_strips_accents_and_stays_local()
    {
        TopicPolicies.Slugify("Reunião de Planejamento!").Should().Be("reuniao-de-planejamento");
        TopicPolicies.Slugify("   ").Should().Be("topico");
    }

    [Fact]
    public void Slug_collision_appends_a_suffix()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "reuniao", "reuniao-2" };
        TopicPolicies.NextSlug("Reunião", taken).Should().Be("reuniao-3");
    }

    [Fact]
    public void Tags_are_normalized_and_capped()
    {
        TopicPolicies.TryNormalizeTags([" Design ", "design", "api"], out var stored, out var error).Should().BeTrue();
        error.Should().BeEmpty();
        stored.Should().Be("design,api");
        TopicPolicies.TryNormalizeTags(["has space"], out _, out var invalid).Should().BeFalse();
        invalid.Should().Be("InvalidTopicTag");
    }

    [Fact]
    public void Quota_stops_at_the_channel_cap()
    {
        TopicPolicies.IsOverQuota(199).Should().BeFalse();
        TopicPolicies.IsOverQuota(TopicPolicies.MaxTopicsPerChannel).Should().BeTrue();
    }

    [Fact]
    public void Participants_keep_the_latest_eight()
    {
        var stored = string.Empty;
        for (var i = 0; i < 10; i++)
        {
            stored = TopicPolicies.RememberParticipant(stored, Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"));
        }

        var ids = TopicPolicies.ReadParticipants(stored);
        ids.Should().HaveCount(8);
        ids[^1].Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000010"));
    }

    [Fact]
    public void Cursor_roundtrips()
    {
        var at = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        TopicPolicies.TryDecodeCursor(TopicPolicies.EncodeCursor(at, id), out var decodedAt, out var decodedId).Should().BeTrue();
        decodedAt.Should().Be(at);
        decodedId.Should().Be(id);
        TopicPolicies.TryDecodeCursor("nope", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Permissions_split_create_and_manage()
    {
        RolePermissionCatalog.For(Role.Member).Should().Contain(Permissions.Conversation.TopicCreate);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Conversation.TopicManage);
        RolePermissionCatalog.For(Role.Guest).Should().Contain(Permissions.Conversation.TopicCreate);
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Conversation.TopicManage);
        RolePermissionCatalog.For(Role.Moderator).Should().Contain(Permissions.Conversation.TopicManage);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Conversation.TopicCreate);
        RolePermissionCatalog.For(Role.Bot).Should().Contain(Permissions.Conversation.TopicCreate);
        RolePermissionCatalog.For(Role.Bot).Should().NotContain(Permissions.Conversation.TopicManage);
    }
}
