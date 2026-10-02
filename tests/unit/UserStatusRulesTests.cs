using FluentAssertions;
using VibeChat.Identity;

namespace VibeChat.UnitTests;

public sealed class UserStatusRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T22:00:00Z");

    [Fact]
    public void Expiration_is_inactive_at_the_deadline()
    {
        UserStatusRules.IsExpired(Now, Now).Should().BeTrue();
        UserStatusRules.IsExpired(Now.AddMinutes(1), Now).Should().BeFalse();
        UserStatusRules.IsExpired(null, Now).Should().BeFalse();

        var row = new UserStatus { ExpiresAt = Now.AddSeconds(-1) };
        UserStatusRules.IsActive(row, Now).Should().BeFalse();
        UserStatusRules.IsActive(null, Now).Should().BeFalse();
    }

    [Fact]
    public void Clear_at_end_of_day_uses_the_user_timezone()
    {
        var end = UserStatusRules.EndOfLocalDay(Now, "America/Sao_Paulo");
        end.Should().Be(DateTimeOffset.Parse("2026-10-03T03:00:00Z"));

        var resolved = UserStatusRules.ResolveExpiresAt(
            explicitExpiresAt: null,
            clearAtEndOfDay: true,
            timeZoneId: "America/Sao_Paulo",
            Now,
            out var error);
        error.Should().BeEmpty();
        resolved.Should().Be(end);
    }

    [Fact]
    public void Explicit_expiry_and_end_of_day_keep_the_earlier_instant()
    {
        var sooner = Now.AddHours(2);
        var resolved = UserStatusRules.ResolveExpiresAt(
            sooner,
            clearAtEndOfDay: true,
            "America/Sao_Paulo",
            Now,
            out var error);
        error.Should().BeEmpty();
        resolved.Should().Be(sooner);

        UserStatusRules.ResolveExpiresAt(Now.AddMinutes(-1), false, "UTC", Now, out var past)
            .Should().BeNull();
        past.Should().Be("StatusExpiresInPast");
    }

    [Fact]
    public void State_allowlist_and_size_limits_reject_invalid_input()
    {
        UserStatusRules.TryNormalize("away", null, "oi", out _, out _, out _, out var stateError)
            .Should().BeFalse();
        stateError.Should().Be("InvalidStatusState");

        UserStatusRules.TryNormalize("custom", null, null, out _, out _, out _, out var empty)
            .Should().BeFalse();
        empty.Should().Be("StatusContentRequired");

        UserStatusRules.TryNormalize("custom", null, new string('a', 81), out _, out _, out _, out var longText)
            .Should().BeFalse();
        longText.Should().Be("StatusTextTooLong");

        UserStatusRules.TryNormalize("focus", new string('x', 17), "foco", out _, out _, out _, out var longEmoji)
            .Should().BeFalse();
        longEmoji.Should().Be("StatusEmojiTooLong");

        UserStatusRules.TryNormalize("focus", null, "linha\nnova", out _, out _, out _, out var control)
            .Should().BeFalse();
        control.Should().Be("StatusTextInvalid");
    }

    [Fact]
    public void Preset_states_receive_a_default_emoji()
    {
        UserStatusRules.TryNormalize("vacation", "  ", "praia", out var state, out var emoji, out var text, out var error)
            .Should().BeTrue();
        error.Should().BeEmpty();
        state.Should().Be(UserStatusState.Vacation);
        emoji.Should().Be("🌴");
        text.Should().Be("praia");
    }

    [Fact]
    public void Availability_comes_from_presence_dnd_and_status()
    {
        UserStatusRules.Derive("online", dndActive: false, null, false).Should().Be(AvailabilityKind.Available);
        UserStatusRules.Derive("away", dndActive: false, UserStatusState.Custom, true).Should().Be(AvailabilityKind.Away);
        UserStatusRules.Derive("offline", dndActive: false, null, false).Should().Be(AvailabilityKind.Offline);
        UserStatusRules.Derive("online", dndActive: false, UserStatusState.Focus, true).Should().Be(AvailabilityKind.Busy);
        UserStatusRules.Derive("online", dndActive: false, UserStatusState.Meeting, true).Should().Be(AvailabilityKind.Busy);
        UserStatusRules.Derive("offline", dndActive: false, UserStatusState.Vacation, true).Should().Be(AvailabilityKind.Vacation);
        UserStatusRules.Derive("online", dndActive: true, UserStatusState.Vacation, true).Should().Be(AvailabilityKind.Busy);
    }

    [Fact]
    public void Dnd_prevails_for_notification_even_when_status_looks_available()
    {
        var availability = UserStatusRules.Derive("online", dndActive: true, UserStatusState.Custom, statusActive: true);
        availability.Should().Be(AvailabilityKind.Busy);
        UserStatusRules.NotificationSuppressedByDnd(dndActive: true, priorityBypass: false).Should().BeTrue();
        UserStatusRules.NotificationSuppressedByDnd(dndActive: true, priorityBypass: true).Should().BeFalse();
        UserStatusRules.NotificationSuppressedByDnd(dndActive: false, priorityBypass: false).Should().BeFalse();
    }
}
