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

    [Fact]
    public void Slash_note_builtin_declares_commands_without_a_callback()
    {
        PluginManifestRules.TryGetBuiltin(PluginManifestRules.BuiltinSlashNoteId, out var manifest, out var error)
            .Should().BeTrue(error);
        manifest!.Capabilities.Should().Equal(
            PluginManifestRules.CapabilityMessagesSend,
            PluginManifestRules.CapabilityCommandsSlash);
        manifest.Commands.Select(command => command.Name).Should().Equal(PluginManifestRules.SlashNoteCommand);
        manifest.Callback.Should().BeNull();
        PluginManifestRules.RequiresPlatform(manifest).Should().BeTrue();
        PluginManifestRules.IsProductSlash(manifest, PluginManifestRules.SlashNoteCommand).Should().BeTrue();
        PluginManifestRules.Builtins(platformEnabled: false).Select(item => item.Id)
            .Should().Equal(PluginManifestRules.BuiltinIncomingMessagesId);
        PluginManifestRules.Builtins(platformEnabled: true).Select(item => item.Id)
            .Should().Equal(PluginManifestRules.BuiltinIncomingMessagesId, PluginManifestRules.BuiltinSlashNoteId);
    }

    [Fact]
    public void Callback_urls_block_metadata_and_private_addresses()
    {
        PluginManifestRules.TryAcceptCallbackUrl("https://169.254.169.254/latest", out _, out var metadata)
            .Should().BeFalse();
        metadata.Should().Be(PluginPlatformErrors.EgressBlocked);
        PluginManifestRules.TryAcceptCallbackUrl("https://10.1.2.3/hook", out _, out var priv)
            .Should().BeFalse();
        priv.Should().Be(PluginPlatformErrors.EgressBlocked);
        PluginManifestRules.TryAcceptCallbackUrl("http://hooks.example.test/hook", out _, out var plain)
            .Should().BeFalse();
        plain.Should().Be(PluginPlatformErrors.InvalidCallbackUrl);
        PluginManifestRules.TryAcceptCallbackUrl("http://127.0.0.1:9/hook", out var lab, out var labError)
            .Should().BeTrue(labError);
        lab.Should().StartWith("http://127.0.0.1:9/");

        const string hostile = """
            {"schema":"vibechat.plugin.manifest.v1","id":"pager","name":"Pager","version":"1.0.0","capabilities":["ui.hooks"],"hooks":["<script>"]}
            """;
        PluginManifestRules.TryParse(hostile, out _, out var hookError).Should().BeFalse();
        hookError.Should().Be(PluginPlatformErrors.UnknownHook);
    }

    [Fact]
    public void Custom_slash_requires_callback_and_known_hooks_are_config_only()
    {
        const string missing = """
            {"schema":"vibechat.plugin.manifest.v1","id":"pager","name":"Pager","version":"1.0.0","capabilities":["commands.slash"],"commands":[{"name":"ping","description":"Ping"}]}
            """;
        PluginManifestRules.TryParse(missing, out _, out var missingError).Should().BeFalse();
        missingError.Should().Be(PluginPlatformErrors.InvalidCallbackUrl);

        const string ok = """
            {"schema":"vibechat.plugin.manifest.v1","id":"pager","name":"Pager","version":"1.0.0","capabilities":["events.subscribe","ui.hooks"],"callback":{"url":"https://hooks.example.test/in","events":["MessageCreated"]},"hooks":["message.badge.bot"]}
            """;
        PluginManifestRules.TryParse(ok, out var manifest, out var error).Should().BeTrue(error);
        manifest!.Hooks.Should().Equal(PluginManifestRules.HookMessageBadgeBot);
        manifest.Callback!.Events.Should().Equal(WebhookEventTypes.MessageCreated);
        manifest.CanonicalJson.Should().NotContain("plugin.js").And.NotContain("dll");
    }
}
