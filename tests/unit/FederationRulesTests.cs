using FluentAssertions;
using VibeChat.Integrations;

namespace VibeChat.UnitTests;

public sealed class FederationRulesTests
{
    [Fact]
    public void Negotiate_requires_messages_and_version_1()
    {
        FederationRules.TryNegotiate(["message"], ["message", "delete"], 1, out var granted, out _).Should().BeTrue();
        granted.Should().Equal("message");
        FederationRules.TryNegotiate(["delete"], ["delete"], 1, out _, out var missing).Should().BeFalse();
        missing.Should().Be(FederationErrors.MessagesRequired);
        FederationRules.TryNegotiate(["message"], ["message"], 2, out _, out var version).Should().BeFalse();
        version.Should().Be(FederationErrors.VersionUnsupported);
        FederationRules.TryNegotiate(["puppet"], ["message"], 1, out _, out var unknown).Should().BeFalse();
        unknown.Should().Be(FederationErrors.CapabilityUnknown);
    }

    [Fact]
    public void Domain_allowlist_rejects_urls_and_unpinned_hosts()
    {
        FederationRules.DomainAllowed("peer.example", ["peer.example"]).Should().BeTrue();
        FederationRules.DomainAllowed("PEER.EXAMPLE", ["peer.example"]).Should().BeTrue();
        FederationRules.DomainAllowed("other.example", ["peer.example"]).Should().BeFalse();
        FederationRules.DomainAllowed("https://peer.example", ["peer.example"]).Should().BeFalse();
        FederationRules.EgressBlocked("https://169.254.169.254/latest").Should().BeTrue();
        FederationRules.EgressBlocked("peer.example").Should().BeFalse();
        FederationRules.DeleteRemoteGuaranteed.Should().BeFalse();
    }

    [Fact]
    public void Signature_rejects_a_tampered_envelope_and_accepts_the_canonical_one()
    {
        var canonical = FederationRules.Canonical(
            "message", "event-0001", "remote-1", "peer.example", "lab-instance", "room-1",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "remote-ada", "Remote Ada", "olá",
            1, 1_700_000_000, "k1", "event-0001");
        var again = FederationRules.Canonical(
            "message", "event-0001", "remote-1", "peer.example", "lab-instance", "room-1",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "remote-ada", "Remote Ada", "olá",
            1, 1_700_000_000, "k1", "event-0001");
        again.Should().Be(canonical);
        var signature = FederationRules.Sign("secret", canonical);
        FederationRules.SignatureMatches(signature, "secret", canonical).Should().BeTrue();
        FederationRules.SignatureMatches(signature, "other", canonical).Should().BeFalse();
        FederationRules.SignatureMatches(signature[..^1] + (signature[^1] == 'a' ? "b" : "a"), "secret", canonical).Should().BeFalse();
    }

    [Fact]
    public void Expiry_and_key_rotation_stay_inside_the_window()
    {
        var now = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
        FederationRules.ExpiryAcceptable(now.AddMinutes(2).ToUnixTimeSeconds(), now, 300, 600).Should().BeTrue();
        FederationRules.ExpiryAcceptable(now.AddMinutes(-10).ToUnixTimeSeconds(), now, 300, 600).Should().BeFalse();
        FederationRules.ExpiryAcceptable(now.AddMinutes(30).ToUnixTimeSeconds(), now, 300, 600).Should().BeFalse();
        FederationRules.NextKeyId("k1").Should().Be("k2");
        FederationRules.IsLoop("lab-instance", "lab-instance").Should().BeTrue();
        FederationRules.IsLoop("other-instance", "lab-instance").Should().BeFalse();
    }
}
