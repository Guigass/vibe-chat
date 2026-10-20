using System.Text.Json;
using FluentAssertions;
using VibeChat.Administration;

namespace VibeChat.UnitTests;

public sealed class ContentAuditRulesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Missing_row_keeps_compliance_on(bool? stored, bool enabled)
    {
        ContentAuditRules.IsEnabled(stored).Should().Be(enabled);
    }

    [Theory]
    [InlineData("ConfidentialE2EE", true)]
    [InlineData(" confidentiale2ee ", true)]
    [InlineData("Public", false)]
    [InlineData(null, false)]
    public void Wire_type_matches_only_the_confidential_channel(string? type, bool match)
    {
        ContentAuditRules.IsConfidentialE2eeType(type).Should().Be(match);
    }

    [Fact]
    public void E2ee_creation_is_blocked_while_audit_is_on_and_unavailable_without_the_protocol()
    {
        ContentAuditRules.E2eeCreationError(true).Should().Be(ContentAuditRules.BlocksE2ee);
        ContentAuditRules.E2eeCreationError(false).Should().Be(ContentAuditRules.E2eeUnavailable);
    }

    [Fact]
    public void Turning_audit_off_needs_confirmation_and_refuses_an_active_body_hold()
    {
        ContentAuditRules.DisableError(true, true, false, true).Should().BeNull();
        ContentAuditRules.DisableError(false, true, false, true).Should().BeNull();
        ContentAuditRules.DisableError(true, false, false, false).Should().Be(ContentAuditRules.ConfirmationRequired);
        ContentAuditRules.DisableError(true, false, true, true).Should().Be(ContentAuditRules.BlockedByLegalHold);
        ContentAuditRules.DisableError(true, false, true, false).Should().BeNull();
    }

    [Fact]
    public void Delete_snapshot_keeps_body_only_while_audit_is_on()
    {
        var channelId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var when = DateTimeOffset.Parse("2026-10-20T12:00:00Z");

        using var withBody = JsonDocument.Parse(ContentAuditRules.DeleteMetadataJson(
            channelId, null, 7, authorId, when, "segredo", true));
        withBody.RootElement.GetProperty("body").GetString().Should().Be("segredo");
        withBody.RootElement.GetProperty("sequence").GetInt64().Should().Be(7);
        withBody.RootElement.GetProperty("authorId").GetGuid().Should().Be(authorId);
        withBody.RootElement.GetProperty("channelId").GetGuid().Should().Be(channelId);

        var redacted = ContentAuditRules.DeleteMetadataJson(
            channelId, null, 7, authorId, when, "segredo", false);
        redacted.Should().NotContain("segredo");
        using var withoutBody = JsonDocument.Parse(redacted);
        withoutBody.RootElement.TryGetProperty("body", out _).Should().BeFalse();
        withoutBody.RootElement.GetProperty("sequence").GetInt64().Should().Be(7);
        withoutBody.RootElement.GetProperty("authorId").GetGuid().Should().Be(authorId);
    }
}
