using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Identity;

namespace VibeChat.UnitTests;

public sealed class DesktopClientTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Shell_matches_the_web_contract_and_hides_secrets()
    {
        var options = new DesktopOptions
        {
            Enabled = true,
            UpdateFeedUrl = "https://updates.example/feed.json",
            UpdatePublicKeyPem = "PUBLICKEYMATERIAL",
            SigningCredentialRef = "operator/desktop-signing"
        };

        var shell = DesktopRules.Describe(options);
        var body = JsonSerializer.Serialize(shell, Json);

        shell.UiFork.Should().BeFalse();
        shell.ApiBase.Should().Be("/api/v1");
        shell.Wrapper.Should().Be("tauri");
        shell.LaunchAtLoginDefault.Should().BeFalse();
        shell.Updates.Should().Be("feed");
        shell.SignedBuilds.Should().BeTrue();
        shell.Signing.Should().Be("ready");
        shell.BrowserFallback.Should().BeTrue();
        shell.Targets.Should().Equal("windows-x64", "macos-arm64", "linux-x64");
        body.Should().NotContain("PUBLICKEYMATERIAL");
        body.Should().NotContain("operator/desktop-signing");
        body.Should().NotContain("access_token");
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://updates.example/feed.json")]
    [InlineData("https://updates.example:8443/feed.json")]
    public void Missing_or_cleartext_feed_turns_updates_off(string feed)
    {
        var options = new DesktopOptions { UpdateFeedUrl = feed };
        DesktopRules.Describe(options).Updates.Should().Be("off");
        DesktopRules.TryVerifyUpdate("{}", options, out _, out var error).Should().BeFalse();
        error.Should().Be(DesktopErrors.UpdateOff);
    }

    [Theory]
    [InlineData("-----BEGIN PRIVATE KEY-----\nabc")]
    [InlineData("operator/secret-key")]
    [InlineData("token-store")]
    public void Unsafe_signing_ref_is_rejected(string credentialRef)
    {
        DesktopRules.SigningState(credentialRef).Should().Be("rejected");
        DesktopRules.Describe(new DesktopOptions { SigningCredentialRef = credentialRef }).SignedBuilds.Should().BeFalse();
    }

    [Fact]
    public void Deep_link_accepts_only_allowlisted_v1_destinations()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var hosts = new[] { "chat.example" };
        DesktopRules.TryParseDeepLink($"vibechat://chat.example/v1/channel/{id:D}", hosts, out var view, out var error).Should().BeTrue();
        view!.Host.Should().Be("chat.example");
        view.Kind.Should().Be("channel");
        view.Id.Should().Be(id);
        error.Should().BeNull();

        string[] attacks =
        [
            "https://chat.example/v1/channel/" + id.ToString("D"),
            "vibechat://user@chat.example/v1/channel/" + id.ToString("D"),
            "vibechat://evil.example/v1/channel/" + id.ToString("D"),
            "vibechat://chat.example/v2/channel/" + id.ToString("D"),
            "vibechat://chat.example/v1/javascript/" + id.ToString("D"),
            "vibechat://chat.example/v1/channel/not-a-guid",
            "vibechat://chat.example/v1/channel/" + id.ToString("D") + "?token=abc",
            "vibechat://chat.example/v1/../channel/" + id.ToString("D"),
            "file:///etc/passwd"
        ];
        foreach (var attack in attacks)
        {
            DesktopRules.TryParseDeepLink(attack, hosts, out _, out var rejected).Should().BeFalse(attack);
            rejected.Should().Be(DesktopErrors.DeepLinkRejected);
        }
    }

    [Fact]
    public void Invalid_update_signature_is_rejected_and_a_valid_one_is_accepted()
    {
        using var rsa = RSA.Create(2048);
        var options = new DesktopOptions
        {
            UpdateFeedUrl = "https://updates.example/feed.json",
            UpdatePublicKeyPem = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())
        };
        var sha = new string('a', 64);
        var canonical = DesktopRules.CanonicalUpdate("1.2.3", "updates.example", ["linux-x64"], sha);
        var signature = Convert.ToBase64String(rsa.SignData(
            Encoding.UTF8.GetBytes(canonical),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss));

        var accepted = UpdateJson("updates.example", signature, sha);
        DesktopRules.TryVerifyUpdate(accepted, options, out var view, out var error).Should().BeTrue(error);
        view!.ProductVersion.Should().Be("1.2.3");
        view.FeedHost.Should().Be("updates.example");

        DesktopRules.TryVerifyUpdate(UpdateJson("updates.example", signature + "aa", sha), options, out _, out var bad)
            .Should().BeFalse();
        bad.Should().Be(DesktopErrors.UpdateRejected);

        DesktopRules.TryVerifyUpdate(UpdateJson("evil.example", signature, sha), options, out _, out var host)
            .Should().BeFalse();
        host.Should().Be(DesktopErrors.UpdateRejected);
    }

    [Fact]
    public void Token_fields_and_device_storage_do_not_carry_secrets()
    {
        DesktopRules.ContainsSecret("""{"platform":"linux-x64","access_token":"abc"}""").Should().BeTrue();
        DesktopRules.ContainsSecret("""{"note":"Bearer abc"}""").Should().BeTrue();
        DesktopRules.ContainsSecret("""{"platform":"linux-x64","appVersion":"1.0.0"}""").Should().BeFalse();

        typeof(DesktopDevice).GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        typeof(DesktopDeviceView).GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Package_hashes_match_the_committed_lock_for_every_os()
    {
        var lockPath = FindRepoFile(Path.Combine("apps", "desktop", "shell.lock.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
        var targets = document.RootElement.GetProperty("targets");
        var hashes = DesktopPackaging.Hashes();

        hashes.Keys.Should().Equal("windows-x64", "macos-arm64", "linux-x64");
        foreach (var pair in hashes)
        {
            targets.GetProperty(pair.Key).GetString().Should().Be(pair.Value);
            DesktopPackaging.Recipe(pair.Key).Should().NotContain("token").And.NotContain("PRIVATE");
        }

        var tauri = JsonDocument.Parse(File.ReadAllText(FindRepoFile(Path.Combine("apps", "desktop", "tauri.conf.json"))));
        tauri.RootElement.GetProperty("plugins").GetProperty("updater").GetProperty("active").GetBoolean().Should().BeFalse();
        tauri.RootElement.GetProperty("plugins").GetProperty("deep-link").GetProperty("desktop").GetProperty("schemes")[0].GetString()
            .Should().Be("vibechat");
        tauri.RootElement.GetProperty("build").GetProperty("frontendDist").GetString().Should().Contain("web");

        var sbom = File.ReadAllText(FindRepoFile(Path.Combine("apps", "desktop", "sbom.json")));
        sbom.Should().Contain("tauri").And.Contain("vibechat-web");
        sbom.Should().NotContain("PRIVATE").And.NotContain("access_token");
    }

    private static string UpdateJson(string feedHost, string signature, string sha) =>
        JsonSerializer.Serialize(new
        {
            channel = "stable",
            productVersion = "1.2.3",
            feedHost,
            targets = new[] { "linux-x64" },
            sha256 = sha,
            signature
        });

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
