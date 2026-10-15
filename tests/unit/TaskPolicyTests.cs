using FluentAssertions;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class TaskPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Version_bump_rejects_a_stale_expected_value()
    {
        TaskPolicies.TryBump(2, 2, out var next).Should().BeTrue();
        next.Should().Be(3);
        TaskPolicies.TryBump(2, 1, out var stale).Should().BeFalse();
        stale.Should().Be(2);
    }

    [Fact]
    public void Overdue_emits_once_and_skips_completed_tasks()
    {
        TaskPolicies.ShouldEmitOverdue(Now.AddMinutes(-1), TaskState.Open, null, Now).Should().BeTrue();
        TaskPolicies.ShouldEmitOverdue(Now.AddMinutes(-1), TaskState.Open, Now, Now).Should().BeFalse();
        TaskPolicies.ShouldEmitOverdue(Now.AddMinutes(-1), TaskState.Completed, null, Now).Should().BeFalse();
        TaskPolicies.IsOverdue(Now.AddMinutes(-1), TaskState.InProgress, Now).Should().BeTrue();
    }

    [Fact]
    public void Checklist_keeps_order_and_assignees_stay_bounded()
    {
        var ok = TaskPolicies.TryNormalizeChecklist(
            [(" segundo ", false), ("primeiro", true)],
            out var items);
        ok.Should().BeTrue();
        items.Select(item => item.Text).Should().Equal("segundo", "primeiro");

        var tooMany = Enumerable.Range(0, TaskPolicies.MaxAssignees + 1).Select(_ => Guid.NewGuid());
        TaskPolicies.TryNormalizeAssignees(tooMany, out _).Should().BeFalse();
    }

    [Fact]
    public void Former_reader_cannot_see_the_task()
    {
        var owner = new UserId(Guid.NewGuid());
        var stranger = new UserId(Guid.NewGuid());
        var item = new MessageTask
        {
            CreatedBy = owner,
            Visibility = CommitmentVisibility.Channel,
        };

        TaskPolicies.CanSee(item, stranger, canReadChannel: false, isAssignee: true).Should().BeFalse();
        TaskPolicies.CanSee(item, stranger, canReadChannel: true, isAssignee: false).Should().BeTrue();
        TaskPolicies.CanMutate(item, stranger, isAssignee: false, isWorkspaceAdmin: false).Should().BeFalse();
    }
}
