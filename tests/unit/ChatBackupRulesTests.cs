using FluentAssertions;
using VibeChat.Administration;

namespace VibeChat.UnitTests;

public sealed class ChatBackupRulesTests
{
    [Fact]
    public void Flag_and_drive_default_off()
    {
        var options = new ChatBackupOptions();
        options.Enabled.Should().BeFalse();
        options.DriveEnabled.Should().BeFalse();
        options.DriveEndpoint.Should().BeEmpty();
    }

    [Fact]
    public void Path_traversal_and_metadata_host_are_rejected()
    {
        ChatBackupRules.ValidateRemotePath("../etc/passwd").Should().Be(ChatBackupErrors.UnsafePath);
        ChatBackupRules.ValidateRemotePath("backups/nightly").Should().BeNull();
        ChatBackupRules.ValidateHost("169.254.169.254").Should().Be(ChatBackupErrors.Invalid);
        ChatBackupRules.RemoteObjectKey("backups", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
            .Should().Be("backups/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.zip");
    }

    [Fact]
    public void Schedule_retention_and_circuit_follow_the_bounds()
    {
        ChatBackupRules.ScheduleValid(5, null).Should().BeTrue();
        ChatBackupRules.ScheduleValid(1, null).Should().BeFalse();
        ChatBackupRules.ScheduleValid(0, "0 * * * *").Should().BeTrue();
        ChatBackupRules.ScheduleValid(5, "0 * * * *").Should().BeFalse();
        var next = ChatBackupRules.NextCron("0 * * * *", new DateTimeOffset(2026, 10, 26, 17, 30, 0, TimeSpan.Zero));
        next.Should().Be(new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var runs = new (Guid Id, DateTimeOffset FinishedAt)[]
        {
            (Guid.Parse("00000000-0000-0000-0000-000000000001"), now.AddDays(-10)),
            (Guid.Parse("00000000-0000-0000-0000-000000000002"), now.AddDays(-1)),
            (Guid.Parse("00000000-0000-0000-0000-000000000003"), now)
        };
        ChatBackupRules.ExpiredRuns(runs, 2, 0, now).Should().Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        ChatBackupRules.ExpiredRuns(runs, 7, 3, now).Should().Contain(Guid.Parse("00000000-0000-0000-0000-000000000001"));

        var first = ChatBackupRules.RecordFailure(0, now);
        first.OpenUntil.Should().BeNull();
        var third = ChatBackupRules.RecordFailure(2, now);
        third.OpenUntil.Should().Be(now.AddSeconds(60));
        ChatBackupRules.IsOpen(third.OpenUntil, now.AddSeconds(30)).Should().BeTrue();
    }

    [Fact]
    public void Package_round_trip_rejects_a_bad_checksum_and_a_traversing_name()
    {
        var messageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var body = "hello-backup"u8.ToArray();
        var messages = JsonSerializerMessages(messageId);
        var attachments = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new[]
            {
                new ChatBackupAttachmentEntry(attachmentId, messageId, Guid.NewGuid(), "note.txt", "text/plain", body.Length, ChatBackupRules.Sha256Hex(body), DateTimeOffset.UnixEpoch)
            },
            ChatBackupPackage.Json);
        var entries = new Dictionary<string, byte[]>
        {
            [ChatBackupPackage.MessagesEntry] = messages,
            [ChatBackupPackage.AttachmentsEntry] = attachments,
            [ChatBackupPackage.ObjectName(attachmentId)] = body
        };
        var zip = ChatBackupPackage.Compose(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, true, true, entries);
        var read = ChatBackupPackage.Read(zip);
        read.Ok.Should().BeTrue();
        read.Messages.Should().ContainSingle(x => x.Id == messageId && x.Body == "hello-backup");
        read.Objects[ChatBackupPackage.ObjectName(attachmentId)].Should().Equal(body);

        zip[zip.Length / 2] ^= 0x5A;
        ChatBackupPackage.Read(zip).Error.Should().BeOneOf(ChatBackupErrors.ChecksumMismatch, ChatBackupErrors.PackageInvalid);
        ChatBackupPackage.SafeEntry("objects/../secret").Should().BeFalse();
        ChatBackupRules.Sanitize("password=s3cret-value", "s3cret-value").Should().Be(ChatBackupErrors.DestinationFailed);
    }

    private static byte[] JsonSerializerMessages(Guid messageId) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new[]
            {
                new ChatBackupMessageEntry(messageId, Guid.NewGuid(), 1, Guid.NewGuid(), "hello-backup", false, null, null, DateTimeOffset.UnixEpoch, null, null, null)
            },
            ChatBackupPackage.Json);
}
