using FluentAssertions;
using VibeChat.Integrations;

namespace VibeChat.UnitTests;

public sealed class BridgeRulesTests
{
    [Fact]
    public void Negotiate_intersects_capabilities_and_requires_messages()
    {
        BridgeRules.TryNegotiate(
            ["message", "delete", "file"],
            ["file", "message"],
            out var granted,
            out var error).Should().BeTrue();
        error.Should().BeEmpty();
        granted.Should().Equal("file", "message");

        BridgeRules.TryNegotiate(["delete"], ["delete"], out _, out var missing).Should().BeFalse();
        missing.Should().Be(BridgeErrors.MessagesRequired);
        BridgeRules.TryNegotiate(["message", "puppet"], ["message"], out _, out var unknown).Should().BeFalse();
        unknown.Should().Be(BridgeErrors.CapabilityUnknown);
    }

    [Fact]
    public void Destination_must_be_a_pinned_host_and_urls_are_blocked()
    {
        BridgeRules.HostPinned("Fake.Bridge.Local", ["fake.bridge.local"]).Should().BeTrue();
        BridgeRules.HostPinned("other.example", ["fake.bridge.local"]).Should().BeFalse();
        BridgeRules.HostPinned("https://fake.bridge.local", ["fake.bridge.local"]).Should().BeFalse();
        BridgeRules.HostPinned("fake.bridge.local:443", ["fake.bridge.local"]).Should().BeFalse();
        BridgeRules.EgressBlocked("http://169.254.169.254/latest").Should().BeTrue();
        BridgeRules.EgressBlocked("https://hooks.example/bridge").Should().BeTrue();
        BridgeRules.EgressBlocked("room-1").Should().BeFalse();
        BridgeRules.DeleteRemoteGuaranteed.Should().BeFalse();
    }

    [Fact]
    public void Loop_detects_our_instance_and_outbound_echoes()
    {
        var echoed = new HashSet<string>(StringComparer.Ordinal) { "local:abc:message" };
        BridgeRules.IsLoop("lab-instance", "lab-instance", "remote-1", echoed).Should().BeTrue();
        BridgeRules.IsLoop("other-net", "lab-instance", "local:abc:message", echoed).Should().BeTrue();
        BridgeRules.IsLoop("other-net", "lab-instance", "remote-9", echoed).Should().BeFalse();
    }

    [Fact]
    public void Signature_is_stable_and_link_stays_on_the_pinned_host()
    {
        var canonical = BridgeRules.Canonical(
            "message", "remote-event-1", "lab-instance", "fake.bridge.local", "room-1",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "remote-ada", "Ada", "olá", 4, "remote-event-1");
        var again = BridgeRules.Canonical(
            "message", "remote-event-1", "lab-instance", "fake.bridge.local", "room-1",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "remote-ada", "Ada", "olá", 4, "remote-event-1");
        again.Should().Be(canonical);
        BridgeRules.Sign("secret", canonical).Should().StartWith("hmac-sha256=").And.Be(BridgeRules.Sign("secret", canonical));
        BridgeRules.Sign("other", canonical).Should().NotBe(BridgeRules.Sign("secret", canonical));
        BridgeRules.SafeLink("https://user:pass@fake.bridge.local/m/1", "fake.bridge.local").Should().BeNull();
        BridgeRules.SafeLink("https://evil.example/m/1", "fake.bridge.local").Should().BeNull();
        BridgeRules.SafeLink("https://fake.bridge.local/m/1", "fake.bridge.local").Should().Be("https://fake.bridge.local/m/1");
        BridgeRules.TryRoom("room/secret", out _).Should().BeFalse();
    }
}
