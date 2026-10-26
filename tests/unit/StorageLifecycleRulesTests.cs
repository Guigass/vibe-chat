using FluentAssertions;
using VibeChat.Files;

namespace VibeChat.UnitTests;

public sealed class StorageLifecycleRulesTests
{
    private const string SecretKey = "tenants/secret-object-key";
    private const string CdnKey = "vibechat-lab-cdn-key";

    [Fact]
    public void Concurrent_reservation_stops_at_the_hard_limit()
    {
        StorageLifecycleRules.TryReserve(0, 0, 100, 60).Should().BeTrue();
        StorageLifecycleRules.TryReserve(0, 60, 100, 60).Should().BeFalse();
        StorageLifecycleRules.TryReserve(40, 50, 100, 11).Should().BeFalse();
    }

    [Fact]
    public void Hold_and_reference_block_purge_and_gc()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var due = now.AddMinutes(-1);

        StorageLifecycleRules.CanPurge(referenced: false, held: true, StorageObjectStates.PurgePending, due, now).Should().BeFalse();
        StorageLifecycleRules.CanPurge(referenced: true, held: false, StorageObjectStates.PurgePending, due, now).Should().BeFalse();
        StorageLifecycleRules.CanPurge(referenced: false, held: false, StorageObjectStates.PurgePending, due, now).Should().BeTrue();
        StorageLifecycleRules.IsGcCandidate(referenced: true, held: false, StorageObjectStates.Hot, due, now).Should().BeFalse();
        StorageLifecycleRules.IsGcCandidate(referenced: false, held: true, StorageObjectStates.Hot, due, now).Should().BeFalse();
        StorageLifecycleRules.IsGcCandidate(referenced: false, held: false, StorageObjectStates.Hot, due, now).Should().BeTrue();
        StorageLifecycleRules.IsGcCandidate(referenced: false, held: false, StorageObjectStates.Hot, now.AddHours(1), now).Should().BeFalse();
    }

    [Fact]
    public void Cdn_token_expires_and_does_not_enumerate_the_storage_key()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var token = StorageLifecycleRules.IssueCdnToken(Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(1), CdnKey);
        var url = $"/api/v1/storage/cdn/{token}";

        StorageLifecycleRules.EnumeratesKey(url, SecretKey).Should().BeFalse();
        StorageLifecycleRules.ReadCdnToken(token, CdnKey, now).Ok.Should().BeTrue();
        StorageLifecycleRules.ReadCdnToken(token, CdnKey, now.AddMinutes(2)).Expired.Should().BeTrue();
        StorageLifecycleRules.ReadCdnToken(token + "x", CdnKey, now).Ok.Should().BeFalse();
        new StorageLifecycleOptions().CdnEnabled.Should().BeFalse();
        StorageLifecycleRules.CdnKeyUsable("CHANGE_ME").Should().BeFalse();
    }

    [Fact]
    public void Reconciliation_corrects_the_ledger_and_restore_keeps_a_tombstone()
    {
        var measured = StorageLifecycleRules.Measure(
        [
            new(true, StorageObjectStates.Hot, 40),
            new(true, StorageObjectStates.Archived, 10),
            new(false, StorageObjectStates.Hot, 7),
            new(true, StorageObjectStates.Purged, 99)
        ]);

        measured.Used.Should().Be(50);
        measured.Reserved.Should().Be(7);
        StorageLifecycleRules.RestoreBlocked(tombstoneExists: true).Should().BeTrue();
        StorageLifecycleRules.RestoreBlocked(tombstoneExists: false).Should().BeFalse();
        StorageLifecycleRules.Trend(50, 1).Should().Be("up");
        StorageLifecycleRules.Action(50, 40, 80).Should().Be("warning");
        StorageLifecycleRules.Action(80, 40, 80).Should().Be("blocked");
    }
}
