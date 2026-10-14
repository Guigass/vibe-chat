using FluentAssertions;
using VibeChat.Messaging;

namespace VibeChat.UnitTests;

public sealed class CommitmentPolicyTests
{
    [Fact]
    public void Excerpt_hash_is_stable_and_does_not_keep_the_text()
    {
        var body = new string('a', 400);
        var hash = CommitmentPolicies.ExcerptHash(body);
        hash.Should().HaveLength(64);
        hash.Should().Be(CommitmentPolicies.ExcerptHash(body));
        hash.Should().NotContain("aaa");
        CommitmentPolicies.ExcerptHash("<system:pin:1>").Should().Be(CommitmentPolicies.ExcerptHash(string.Empty));
        CommitmentPolicies.IsConvertible("<system:pin:1>").Should().BeFalse();
        CommitmentPolicies.IsConvertible("uma decisão").Should().BeTrue();
    }

    [Fact]
    public void Restricted_visibility_hides_the_item_from_other_readers()
    {
        var owner = new VibeChat.SharedKernel.UserId(Guid.NewGuid());
        var stranger = new VibeChat.SharedKernel.UserId(Guid.NewGuid());
        var item = new Commitment
        {
            CreatedBy = owner,
            OwnerUserId = owner,
            Visibility = CommitmentVisibility.Restricted,
        };

        CommitmentPolicies.CanSee(item, stranger, canReadChannel: true).Should().BeFalse();
        CommitmentPolicies.CanSee(item, owner, canReadChannel: false).Should().BeFalse();
        CommitmentPolicies.CanSee(item, owner, canReadChannel: true).Should().BeTrue();
        CommitmentPolicies.CanMutate(item, stranger, isWorkspaceAdmin: true).Should().BeFalse();
        CommitmentPolicies.CanMutate(item, owner, isWorkspaceAdmin: false).Should().BeTrue();
    }
}
