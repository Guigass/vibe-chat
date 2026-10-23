using System.Security.Cryptography;
using FluentAssertions;
using VibeChat.Integrations;

namespace VibeChat.UnitTests;

public sealed class PluginRegistryTests
{
    [Fact]
    public void Valid_mirror_package_verifies_and_blocks_tamper_checksum_and_remote_refs()
    {
        using var rsa = RSA.Create(2048);
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();
        var trust = Trust(rsa);
        var payload = Payload(PluginManifestRules.IncomingMessagesJson);
        PluginRegistryRules.TryBuildIndex(privatePem, [payload], out var index, out var error).Should().BeTrue(error);

        var verified = PluginRegistryRules.Verify(index, trust);
        verified.Ok.Should().BeTrue();
        verified.Entries.Should().ContainSingle();
        verified.Entries[0].Accepted.Should().BeTrue();
        verified.Entries[0].Source.Should().Be("mirror");
        verified.Entries[0].SignatureFingerprint.Should().HaveLength(16);

        var tampered = index.Replace("Lab Publisher", "Lab PublisheX", StringComparison.Ordinal);
        var forged = PluginRegistryRules.Verify(tampered, trust);
        forged.Ok.Should().BeTrue();
        forged.Entries[0].Accepted.Should().BeFalse();
        forged.Entries[0].Reason.Should().Be(PluginRegistryErrors.InvalidSignature);

        var badChecksum = payload with { Checksum = "sha256=" + new string('0', 64) };
        var checksumPayload = PluginRegistryRules.CanonicalPayload(badChecksum);
        var checksumIndex = PluginRegistryRules.WrapIndex([(checksumPayload, PluginRegistryRules.Sign(privatePem, checksumPayload))]);
        PluginRegistryRules.Verify(checksumIndex, trust).Entries[0].Reason.Should().Be(PluginRegistryErrors.InvalidChecksum);

        var remote = payload with { ArtifactRef = "https://169.254.169.254/latest/meta-data" };
        PluginRegistryRules.TryBuildIndex(privatePem, [remote], out var remoteIndex, out _).Should().BeTrue();
        PluginRegistryRules.Verify(remoteIndex, trust).Entries[0].Reason.Should().Be(PluginRegistryErrors.EgressBlocked);
        PluginRegistryRules.RemoteCatalogBlocked("http://169.254.169.254/latest/meta-data").Should().BeTrue();
        PluginRegistryRules.RemoteCatalogBlocked("https://registry.example/index.json").Should().BeTrue();
        PluginRegistryRules.RemoteCatalogBlocked(null).Should().BeFalse();
    }

    [Fact]
    public void Capability_growth_downgrade_revocation_and_dependencies_are_deterministic()
    {
        PluginRegistryRules.CompareVersion("1.0.0", "1.1.0").Should().BeNegative();
        PluginRegistryRules.CompareVersion("1.2.0", "1.1.9").Should().BePositive();
        PluginRegistryRules.AddedCapabilities(["messages.send"], ["commands.slash", "messages.send"])
            .Should().Equal("commands.slash");
        PluginRegistryRules.Covers([], ["messages.send"]).Should().BeFalse();
        PluginRegistryRules.Covers(["messages.send", "commands.slash"], ["commands.slash"]).Should().BeTrue();

        using var rsa = RSA.Create(2048);
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();
        var trust = Trust(rsa);
        var revoked = Payload(PluginManifestRules.IncomingMessagesJson) with { Revoked = true, Advisory = "yanked" };
        PluginRegistryRules.TryBuildIndex(privatePem, [revoked], out var revokedIndex, out _).Should().BeTrue();
        var revokedView = PluginRegistryRules.Verify(revokedIndex, trust).Entries[0];
        revokedView.Accepted.Should().BeFalse();
        revokedView.Reason.Should().Be(PluginRegistryErrors.VersionRevoked);

        var listed = Payload(PluginManifestRules.IncomingMessagesJson);
        PluginRegistryRules.TryBuildIndex(privatePem, [listed], out var listedIndex, out _).Should().BeTrue();
        PluginRegistryRules.Verify(listedIndex, trust with { Revoked = ["incoming-messages@1.0.0"] })
            .Entries[0].Reason.Should().Be(PluginRegistryErrors.VersionRevoked);

        var unsorted = PluginRegistryRules.CanonicalPayload(listed with { Capabilities = ["messages.send", "commands.slash"], DependsOn = ["b@1.0.0", "a@1.0.0"] });
        var sorted = PluginRegistryRules.CanonicalPayload(listed with { Capabilities = ["commands.slash", "messages.send"], DependsOn = ["a@1.0.0", "b@1.0.0"] });
        unsorted.Should().Be(sorted);

        var missing = listed with { DependsOn = ["other-plugin@1.0.0"] };
        PluginRegistryRules.TryBuildIndex(privatePem, [missing], out var missingIndex, out _).Should().BeTrue();
        PluginRegistryRules.Verify(missingIndex, trust).Ok.Should().BeFalse();
        PluginRegistryRules.Verify(missingIndex, trust).Error.Should().Be(PluginRegistryErrors.InvalidIndex);

        var left = Payload(Manifest("left-plugin", "1.0.0")) with { DependsOn = ["right-plugin@1.0.0"] };
        var right = Payload(Manifest("right-plugin", "1.0.0")) with { DependsOn = ["left-plugin@1.0.0"] };
        PluginRegistryRules.TryBuildIndex(privatePem, [left, right], out var cycle, out _).Should().BeTrue();
        PluginRegistryRules.Verify(cycle, trust).Error.Should().Be(PluginRegistryErrors.InvalidIndex);

        var community = listed with { Source = "allowlisted", CatalogId = "community" };
        PluginRegistryRules.TryBuildIndex(privatePem, [community], out var communityIndex, out _).Should().BeTrue();
        PluginRegistryRules.Verify(communityIndex, trust).Entries[0].Reason.Should().Be(PluginRegistryErrors.UntrustedCatalog);
        var allowed = trust with { AllowlistedCatalogs = ["community"] };
        PluginRegistryRules.Verify(communityIndex, allowed).Entries[0].Accepted.Should().BeTrue();
    }

    private static PluginRegistryTrust Trust(RSA rsa) => new(
        [new RegistryTrustRoot("lab", Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), ["mirror", "official", "private", "community"])],
        [],
        []);

    private static RegistryPayload Payload(string manifestJson)
    {
        PluginManifestRules.TryParse(manifestJson, out var manifest, out var error).Should().BeTrue(error);
        return new RegistryPayload(
            "lab", "mirror", "mirror", manifest!.Id, manifest.Version, "Lab Publisher",
            "mirror://lab/" + manifest.Id + "/" + manifest.Version, "sha256=" + new string('a', 64),
            "built-in-mirror", "1", "1", [], "low", false, null, manifest.Capabilities, manifest.CanonicalJson);
    }

    private static string Manifest(string id, string version) =>
        "{\"schema\":\"vibechat.plugin.manifest.v1\",\"id\":\"" + id + "\",\"name\":\"Registry Note\",\"version\":\""
        + version + "\",\"capabilities\":[\"messages.send\"]}";
}
