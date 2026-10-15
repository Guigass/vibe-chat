using FluentAssertions;
using VibeChat.Messaging;

namespace VibeChat.UnitTests;

public sealed class InboxPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Kinds_have_a_fixed_priority_order()
    {
        InboxPolicies.Priority(InboxItemKind.Announcement).Should().BeLessThan(InboxPolicies.Priority(InboxItemKind.Mention));
        InboxPolicies.Priority(InboxItemKind.Mention).Should().BeLessThan(InboxPolicies.Priority(InboxItemKind.Direct));
        InboxPolicies.Priority(InboxItemKind.Direct).Should().BeLessThan(InboxPolicies.Priority(InboxItemKind.Thread));
        InboxPolicies.Priority(InboxItemKind.Thread).Should().BeLessThan(InboxPolicies.Priority(InboxItemKind.Saved));
        InboxPolicies.Priority(InboxItemKind.Saved).Should().BeLessThan(InboxPolicies.Priority(InboxItemKind.Topic));
        InboxPolicies.Priority(InboxItemKind.Topic).Should().BeLessThan(InboxPolicies.Priority(InboxItemKind.Commitment));
        InboxPolicies.Priority(InboxItemKind.Commitment).Should().BeLessThan(InboxPolicies.Priority(InboxItemKind.Task));
    }

    [Fact]
    public void Item_key_roundtrips()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var key = InboxPolicies.ItemKey(InboxItemKind.Mention, id);
        InboxPolicies.TryParseItemKey(key, out var kind, out var parsed).Should().BeTrue();
        kind.Should().Be(InboxItemKind.Mention);
        parsed.Should().Be(id);
        InboxPolicies.TryParseItemKey("nope", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Snooze_is_private_to_its_window()
    {
        InboxPolicies.IsSnoozeActive(Now.AddHours(1), Now).Should().BeTrue();
        InboxPolicies.IsSnoozeActive(Now.AddMinutes(-1), Now).Should().BeFalse();
        InboxPolicies.IsSnoozeActive(null, Now).Should().BeFalse();
        InboxPolicies.IsSnoozeUntilAllowed(Now.AddDays(30), Now).Should().BeTrue();
        InboxPolicies.IsSnoozeUntilAllowed(Now.AddDays(31), Now).Should().BeFalse();
        InboxPolicies.IsSnoozeUntilAllowed(Now, Now).Should().BeFalse();
    }

    [Fact]
    public void Same_message_appears_once_keeping_the_higher_priority_kind()
    {
        var messageId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var items = new[]
        {
            Sample(InboxItemKind.Saved, messageId, channelId, messageId, Now.AddMinutes(-10)),
            Sample(InboxItemKind.Mention, messageId, channelId, messageId, Now.AddMinutes(-5)),
            Sample(InboxItemKind.Direct, channelId, channelId, Guid.NewGuid(), Now.AddMinutes(-1)),
        };

        var deduped = InboxPolicies.Dedup(items);
        deduped.Should().HaveCount(2);
        deduped.Should().ContainSingle(item => item.Kind == InboxItemKind.Mention && item.MessageId == messageId);
        deduped.Should().ContainSingle(item => item.Kind == InboxItemKind.Direct);
        deduped.Should().NotContain(item => item.Kind == InboxItemKind.Saved);
    }

    [Fact]
    public void Page_does_not_duplicate_or_drop_items_when_a_newer_one_arrives()
    {
        var channelId = Guid.NewGuid();
        var older = Sample(InboxItemKind.Mention, Guid.NewGuid(), channelId, Guid.NewGuid(), Now.AddMinutes(-30));
        var middle = Sample(InboxItemKind.Mention, Guid.NewGuid(), channelId, Guid.NewGuid(), Now.AddMinutes(-20));
        var newest = Sample(InboxItemKind.Mention, Guid.NewGuid(), channelId, Guid.NewGuid(), Now.AddMinutes(-10));
        var sorted = InboxPolicies.Sort([older, middle, newest]);

        var (first, cursor) = InboxPolicies.Page(sorted, cursor: null, limit: 1);
        first.Select(item => item.ItemKey).Should().Equal(newest.ItemKey);
        var (second, next) = InboxPolicies.Page(sorted, cursor, limit: 1);
        second.Select(item => item.ItemKey).Should().Equal(middle.ItemKey);
        var (third, end) = InboxPolicies.Page(sorted, next, limit: 1);
        third.Select(item => item.ItemKey).Should().Equal(older.ItemKey);
        end.Should().BeNull();

        var inserted = Sample(InboxItemKind.Mention, Guid.NewGuid(), channelId, Guid.NewGuid(), Now);
        var withInsert = InboxPolicies.Sort([older, middle, newest, inserted]);
        var (rest, _) = InboxPolicies.Page(withInsert, cursor, limit: 10);
        rest.Select(item => item.ItemKey).Should().Equal(middle.ItemKey, older.ItemKey);
        rest.Should().NotContain(item => item.ItemKey == newest.ItemKey || item.ItemKey == inserted.ItemKey);

        var (refreshed, _) = InboxPolicies.Page(withInsert, cursor: null, limit: 1);
        refreshed.Select(item => item.ItemKey).Should().Equal(inserted.ItemKey);
    }

    [Fact]
    public void Cursor_rejects_garbage()
    {
        InboxPolicies.TryDecodeCursor("not-a-cursor", out _).Should().BeFalse();
        var act = () => InboxPolicies.Page([], "%%%", 10);
        act.Should().Throw<ArgumentException>().WithMessage("InvalidCursor");
    }

    [Fact]
    public void Read_cursor_applies_only_to_attention_kinds()
    {
        InboxPolicies.UsesReadCursor(InboxItemKind.Mention).Should().BeTrue();
        InboxPolicies.UsesReadCursor(InboxItemKind.Direct).Should().BeTrue();
        InboxPolicies.UsesReadCursor(InboxItemKind.Thread).Should().BeTrue();
        InboxPolicies.UsesReadCursor(InboxItemKind.Announcement).Should().BeFalse();
        InboxPolicies.UsesReadCursor(InboxItemKind.Saved).Should().BeFalse();
    }

    private static InboxCandidate Sample(
        InboxItemKind kind,
        Guid sourceId,
        Guid channelId,
        Guid messageId,
        DateTimeOffset sortAt) =>
        new(kind, sourceId, channelId, "geral", messageId, null, 1, "preview", false, sortAt, null);
}
