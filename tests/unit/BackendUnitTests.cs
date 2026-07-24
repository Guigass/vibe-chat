using FluentAssertions;
using VibeChat.AI;
using VibeChat.BuildingBlocks;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class BackendUnitTests
{
    [Fact]
    public void Message_idempotency_hash_is_stable_for_same_command()
    {
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var channelId = ChannelId.New();
        var messageId = MessageId.New();
        var command = new SendMessageCommand(tenantId, userId, channelId, messageId, "idem-1", "hello", null, null);

        var first = MessageIdempotency.ComputeRequestHash(command);
        var second = MessageIdempotency.ComputeRequestHash(command);

        first.Should().Be(second);
        first.Should().HaveLength(64);
    }

    [Fact]
    public void Message_idempotency_hash_changes_when_body_changes()
    {
        var command = new SendMessageCommand(TenantId.New(), UserId.New(), ChannelId.New(), MessageId.New(), "idem-1", "hello", null, null);
        var changed = command with { Body = "hello again" };

        MessageIdempotency.ComputeRequestHash(command).Should().NotBe(MessageIdempotency.ComputeRequestHash(changed));
    }

    [Fact]
    public void Permission_catalog_allows_member_to_send_but_not_view_admin_dashboard()
    {
        var permissions = RolePermissionCatalog.For(Role.Member);

        permissions.Should().Contain(Permissions.Message.Send);
        permissions.Should().NotContain(Permissions.Admin.Dashboard);
    }

    [Fact]
    public void Message_sequences_are_orderable_per_conversation()
    {
        var channelId = ChannelId.New();
        var messages = new[]
        {
            new Message { ConversationId = channelId, Sequence = 3, Body = "c" },
            new Message { ConversationId = channelId, Sequence = 1, Body = "a" },
            new Message { ConversationId = channelId, Sequence = 2, Body = "b" }
        };

        messages.OrderBy(x => x.Sequence).Select(x => x.Body).Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task Mock_ai_summarizes_without_requiring_external_provider()
    {
        var provider = new MockAiProvider();

        var response = await provider.CompleteAsync(new AiCompletionRequest("summarize", "one\ntwo\nthree"), CancellationToken.None);

        response.Text.Should().StartWith("Mock summary:");
        response.Text.Should().NotContain("one");
        response.PromptTokens.Should().BeGreaterThan(0);
    }
}
