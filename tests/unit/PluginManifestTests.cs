using FluentAssertions;
using VibeChat.Integrations;

namespace VibeChat.UnitTests;

public sealed class PluginManifestTests
{
    [Fact]
    public void Builtin_incoming_messages_is_schema_v1_send_only()
    {
        PluginManifestRules.TryGetBuiltin(PluginManifestRules.BuiltinIncomingMessagesId, out var manifest, out var error)
            .Should().BeTrue(error);
        manifest!.Schema.Should().Be(PluginManifestRules.SchemaV1);
        manifest.Id.Should().Be("incoming-messages");
        manifest.Capabilities.Should().Equal(PluginManifestRules.CapabilityMessagesSend);
        manifest.CanonicalJson.Should().NotContain("dll").And.NotContain("script");
    }

    [Fact]
    public void Unknown_capability_is_rejected()
    {
        const string json = """
            {"schema":"vibechat.plugin.manifest.v1","id":"pager","name":"Pager","version":"1.0.0","capabilities":["messages.read"]}
            """;
        PluginManifestRules.TryParse(json, out _, out var error).Should().BeFalse();
        error.Should().Be(PluginManifestRules.UnknownCapability);
    }

    [Fact]
    public void Extra_property_and_empty_capabilities_are_invalid()
    {
        const string extra = """
            {"schema":"vibechat.plugin.manifest.v1","id":"pager","name":"Pager","version":"1.0.0","capabilities":["messages.send"],"entry":"plugin.js"}
            """;
        PluginManifestRules.TryParse(extra, out _, out var extraError).Should().BeFalse();
        extraError.Should().Be(PluginManifestRules.Invalid);

        const string empty = """
            {"schema":"vibechat.plugin.manifest.v1","id":"pager","name":"Pager","version":"1.0.0","capabilities":[]}
            """;
        PluginManifestRules.TryParse(empty, out _, out var emptyError).Should().BeFalse();
        emptyError.Should().Be(PluginManifestRules.Invalid);
    }

    [Fact]
    public void Unknown_builtin_is_rejected()
    {
        PluginManifestRules.TryGetBuiltin("marketplace", out _, out var error).Should().BeFalse();
        error.Should().Be(PluginManifestRules.UnknownBuiltin);
    }
}
