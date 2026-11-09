using FluentAssertions;
using VibeChat.Infrastructure.Messaging;
using VibeChat.Messaging;

namespace VibeChat.UnitTests;

public sealed class CanvasCrdtTests
{
    [Fact]
    public void Flag_defaults_off_and_label_strips_controls()
    {
        new CanvasOptions().Enabled.Should().BeFalse();
        CanvasPolicies.WireVersion.Should().Be(1);
        CanvasPolicies.Label("Ana\u0000 Silva", Guid.Empty).Should().Be("Ana Silva");
        CanvasPolicies.Label("   ", Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"))
            .Should().Be("aaaaaaaabbbbccccddddeeeeeeeeeeee"[..8]);
    }

    [Fact]
    public void Partitioned_inserts_converge_and_repeat_is_stable()
    {
        var origin = CanvasCrdt.Create("Olá");
        CanvasCrdt.Read(origin).Should().Be("Olá");
        var left = CanvasCrdt.Insert(origin, 1001, (uint)"Olá".Length, " Alice");
        var right = CanvasCrdt.Insert(origin, 1002, 0, "Bob ");

        CanvasCrdt.TryApply(origin, left, out var withLeft, out _).Should().BeTrue();
        CanvasCrdt.TryApply(withLeft, right, out var leftThenRight, out var textA).Should().BeTrue();
        CanvasCrdt.TryApply(origin, right, out var withRight, out _).Should().BeTrue();
        CanvasCrdt.TryApply(withRight, left, out var rightThenLeft, out var textB).Should().BeTrue();

        textA.Should().Be(textB);
        textA.Should().Contain("Alice").And.Contain("Bob").And.Contain("Olá");
        CanvasCrdt.TryApply(leftThenRight, left, out var repeated, out var textC).Should().BeTrue();
        textC.Should().Be(textA);
        Convert.ToBase64String(repeated).Should().NotBeNull();
    }

    [Fact]
    public void Garbage_update_is_rejected()
    {
        var origin = CanvasCrdt.Create("base");
        CanvasCrdt.TryApply(origin, [1, 2, 3, 4], out var next, out var text).Should().BeFalse();
        next.Should().Equal(origin);
        text.Should().BeEmpty();
    }
}
