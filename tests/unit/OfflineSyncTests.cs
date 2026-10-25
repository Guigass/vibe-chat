using FluentAssertions;
using VibeChat.Messaging;

namespace VibeChat.UnitTests;

public sealed class OfflineSyncTests
{
    private const string Key = "vibechat-lab-offline-cursor";
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Device = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Flag_without_a_real_key_stays_closed()
    {
        OfflineSyncOptions.IsActive(new OfflineSyncOptions()).Should().BeFalse();
        OfflineSyncOptions.IsActive(new OfflineSyncOptions { Enabled = true, CursorKey = "CHANGE_ME" }).Should().BeFalse();
        OfflineSyncOptions.IsActive(new OfflineSyncOptions { Enabled = true, CursorKey = Key }).Should().BeTrue();
    }

    [Fact]
    public void Cursor_rejects_another_tenant_user_or_device()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var cursor = OfflineSyncCursor.Issue(Tenant, User, Device, now, Guid.Empty, now, Key);

        OfflineSyncCursor.Read(cursor, Guid.NewGuid(), User, Device, now, Key).Status
            .Should().Be(OfflineSyncCursorStatus.Rejected);
        OfflineSyncCursor.Read(cursor, Tenant, Guid.NewGuid(), Device, now, Key).Status
            .Should().Be(OfflineSyncCursorStatus.Rejected);
        OfflineSyncCursor.Read(cursor, Tenant, User, Guid.NewGuid(), now, Key).Status
            .Should().Be(OfflineSyncCursorStatus.Rejected);
        OfflineSyncCursor.Read(cursor + "x", Tenant, User, Device, now, Key).Status
            .Should().Be(OfflineSyncCursorStatus.Rejected);
    }

    [Fact]
    public void Expired_cursor_does_not_count_as_a_valid_delta()
    {
        var issued = DateTimeOffset.Parse("2026-10-01T12:00:00Z");
        var cursor = OfflineSyncCursor.Issue(Tenant, User, Device, issued, Guid.Empty, issued, Key);
        var state = OfflineSyncCursor.Read(cursor, Tenant, User, Device, issued.AddDays(8), Key);
        state.Status.Should().Be(OfflineSyncCursorStatus.Expired);
    }

    [Fact]
    public void Upgrade_is_distinct_from_a_bad_signature()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var cursor = OfflineSyncCursor.Issue(Tenant, User, Device, now, Guid.Empty, now, Key, version: 99);
        OfflineSyncCursor.Read(cursor, Tenant, User, Device, now, Key).Status
            .Should().Be(OfflineSyncCursorStatus.UpgradeRequired);
    }

    [Fact]
    public void Tombstone_strips_the_body()
    {
        var row = new OfflineSyncCandidate(
            "message", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, 0, "segredo", true, DateTimeOffset.UnixEpoch);
        var (changes, tombstones) = OfflineSyncPage.Split([row]);
        changes.Should().BeEmpty();
        tombstones.Should().ContainSingle();
        tombstones[0].Body.Should().BeNull();
    }

    [Fact]
    public void Page_continues_without_skipping_or_repeating()
    {
        var start = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var rows = Enumerable.Range(0, 3).Select(index => new OfflineSyncCandidate(
            "message",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}"),
            index + 1,
            0,
            $"body-{index}",
            false,
            start.AddMinutes(index))).ToList();

        var first = OfflineSyncPage.Continue(rows, DateTimeOffset.UnixEpoch, Guid.Empty, 2, out var hasMore, out var watermark, out var lastId);
        hasMore.Should().BeTrue();
        first.Select(row => row.Seq).Should().Equal(1, 2);

        var second = OfflineSyncPage.Continue(rows, watermark, lastId, 2, out var more, out _, out _);
        more.Should().BeFalse();
        second.Select(row => row.Seq).Should().Equal(3);
        second.Select(row => row.Id).Should().NotIntersectWith(first.Select(row => row.Id));
    }

    [Fact]
    public void Version_token_changes_only_when_the_message_was_edited()
    {
        OfflineSyncProtocol.MessageVersion(null).Should().Be(0);
        var edited = DateTimeOffset.Parse("2026-10-25T12:00:01.250Z");
        OfflineSyncProtocol.MessageVersion(edited).Should().Be(edited.ToUnixTimeMilliseconds());
    }
}
