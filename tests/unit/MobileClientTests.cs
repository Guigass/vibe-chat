using System.Text.Json;
using FluentAssertions;
using VibeChat.Identity;

namespace VibeChat.UnitTests;

public sealed class MobileClientTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid ChannelId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string[] Hosts = ["chat.example"];

    [Fact]
    public void Shell_matches_the_web_contract_and_sideloads_without_a_store_account()
    {
        var shell = MobileRules.Describe(new MobileOptions { Enabled = true });
        var body = JsonSerializer.Serialize(shell, Json);

        shell.UiFork.Should().BeFalse();
        shell.ApiBase.Should().Be("/api/v1");
        shell.Wrapper.Should().Be("capacitor");
        shell.BiometricLockDefault.Should().BeFalse();
        shell.Distribution.Should().Be("sideload");
        shell.Store.Should().Be("off");
        shell.PushContent.Should().Be("ids-only");
        shell.SideloadReady.Should().BeTrue();
        shell.BrowserFallback.Should().BeTrue();
        shell.Targets.Should().Equal("android-arm64", "ios-arm64");
        body.Should().NotContain("access_token");
    }

    [Theory]
    [InlineData("operator/mobile-store", "store", "ready")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nabc", "sideload", "rejected")]
    [InlineData("operator/secret-store", "sideload", "rejected")]
    public void Store_ref_never_replaces_the_sideload_artifact_with_a_secret(string credentialRef, string distribution, string store)
    {
        var options = new MobileOptions { StoreCredentialRef = credentialRef };
        var shell = MobileRules.Describe(options);
        var body = JsonSerializer.Serialize(shell, Json);

        shell.Distribution.Should().Be(distribution);
        shell.Store.Should().Be(store);
        shell.SideloadReady.Should().BeTrue();
        body.Should().NotContain(credentialRef);
    }

    [Fact]
    public void Deep_link_and_share_stay_on_the_allowlisted_host_and_do_not_send()
    {
        var linkJson = JsonSerializer.Serialize(new { url = $"vibechat://chat.example/v1/channel/{ChannelId:D}" });
        MobileRules.TryParseDeepLink(linkJson, Hosts, out var link, out var linkError).Should().BeTrue();
        link!.Host.Should().Be("chat.example");
        link.Kind.Should().Be("channel");
        linkError.Should().BeNull();

        MobileRules.TryParseDeepLink(
            JsonSerializer.Serialize(new { url = "vibechat://evil.example/v1/channel/" + ChannelId.ToString("D") }),
            Hosts,
            out _,
            out var evil).Should().BeFalse();
        evil.Should().Be(MobileErrors.DeepLinkRejected);

        var share = JsonSerializer.Serialize(new { host = "chat.example", kind = "channel", id = ChannelId.ToString("D"), confirm = true });
        MobileRules.TryAcceptShare(share, Hosts, out var accepted, out var shareError).Should().BeTrue();
        accepted!.SendsMessage.Should().BeFalse();
        accepted.Id.Should().Be(ChannelId);
        shareError.Should().BeNull();

        MobileRules.TryAcceptShare(
            JsonSerializer.Serialize(new { host = "evil.example", kind = "channel", id = ChannelId.ToString("D"), confirm = true }),
            Hosts,
            out _,
            out var wrongHost).Should().BeFalse();
        wrongHost.Should().Be(MobileErrors.ShareRejected);

        MobileRules.TryAcceptShare(
            JsonSerializer.Serialize(new { host = "chat.example", kind = "channel", id = ChannelId.ToString("D"), confirm = false }),
            Hosts,
            out _,
            out _).Should().BeFalse();

        MobileRules.TryAcceptShare(
            """{"host":"chat.example","kind":"channel","id":"22222222-2222-2222-2222-222222222222","confirm":true,"tenantId":"11111111-1111-1111-1111-111111111111"}""",
            Hosts,
            out _,
            out var tenant).Should().BeFalse();
        tenant.Should().Be(MobileErrors.TokenRejected);

        MobileRules.TryAcceptShare(
            """{"host":"chat.example","kind":"channel","id":"22222222-2222-2222-2222-222222222222","confirm":true,"text":"secret"}""",
            Hosts,
            out _,
            out var content).Should().BeFalse();
        content.Should().Be(MobileErrors.TokenRejected);
    }

    [Fact]
    public void Push_preview_keeps_ids_only()
    {
        var ok = JsonSerializer.Serialize(new { kind = "mention", conversationId = ChannelId.ToString("D") });
        MobileRules.TryAcceptPush(ok, out var view, out var error).Should().BeTrue();
        view!.ContainsContent.Should().BeFalse();
        view.Kind.Should().Be("mention");
        view.ConversationId.Should().Be(ChannelId);
        error.Should().BeNull();

        MobileRules.TryAcceptPush(
            """{"kind":"message","conversationId":"22222222-2222-2222-2222-222222222222","text":"hello"}""",
            out _,
            out var text).Should().BeFalse();
        text.Should().Be(MobileErrors.TokenRejected);

        MobileRules.TryAcceptPush(
            """{"kind":"message","conversationId":"22222222-2222-2222-2222-222222222222","fcmToken":"abc"}""",
            out _,
            out var token).Should().BeFalse();
        token.Should().Be(MobileErrors.TokenRejected);

        MobileRules.TryAcceptPush("""{"kind":"body","conversationId":"22222222-2222-2222-2222-222222222222"}""", out _, out var kind)
            .Should().BeFalse();
        kind.Should().Be(MobileErrors.PushRejected);
    }

    [Fact]
    public void Token_fields_and_device_storage_do_not_carry_secrets()
    {
        MobileRules.ContainsSecret("""{"platform":"ios-arm64","access_token":"abc"}""").Should().BeTrue();
        MobileRules.ContainsSecret("""{"platform":"android-arm64","appVersion":"1.0.0"}""").Should().BeFalse();
        MobileRules.IsRegisterBody("""{"platform":"ios-arm64","appVersion":"1.0.0"}""").Should().BeTrue();
        MobileRules.IsRegisterBody("""{"platform":"ios-arm64","appVersion":"1.0.0","note":"x"}""").Should().BeFalse();

        typeof(MobileDevice).GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        typeof(MobileDeviceView).GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Package_hashes_match_the_committed_lock_for_ios_and_android()
    {
        var lockPath = FindRepoFile(Path.Combine("apps", "mobile", "shell.lock.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
        var targets = document.RootElement.GetProperty("targets");
        var hashes = MobilePackaging.Hashes();

        hashes.Keys.Should().Equal("android-arm64", "ios-arm64");
        foreach (var pair in hashes)
        {
            targets.GetProperty(pair.Key).GetString().Should().Be(pair.Value);
            MobilePackaging.Recipe(pair.Key).Should().NotContain("token").And.NotContain("PRIVATE");
        }

        document.RootElement.GetProperty("distribution").GetString().Should().Be("sideload");

        var sideload = JsonDocument.Parse(File.ReadAllText(FindRepoFile(Path.Combine("apps", "mobile", "sideload.json"))));
        sideload.RootElement.GetProperty("storeAccount").GetBoolean().Should().BeFalse();
        sideload.RootElement.GetProperty("targets").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("android-arm64", "ios-arm64");

        var config = JsonDocument.Parse(File.ReadAllText(FindRepoFile(Path.Combine("apps", "mobile", "capacitor.config.json"))));
        config.RootElement.GetProperty("webDir").GetString().Should().Contain("web");
        config.RootElement.GetProperty("appId").GetString().Should().Be("app.vibechat.mobile");

        var sbom = File.ReadAllText(FindRepoFile(Path.Combine("apps", "mobile", "sbom.json")));
        sbom.Should().Contain("Apache-2.0").And.Contain("vibechat-web");
        sbom.Should().NotContain("PRIVATE").And.NotContain("access_token");
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
