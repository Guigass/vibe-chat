using System.Text.Json.Nodes;
using FluentAssertions;
using VibeChat.Integrations;
using VibeChat.Sdk;

namespace VibeChat.UnitTests;

public sealed class PluginSdkTests
{
    private const string Body = "{\"synthetic\":true}";
    private const string Golden = "sha256=4eb5148d02412ccd84b203e568a9c296ac3e36c993333c9b918c2eb67f05ba9b";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Verifier_accepts_the_golden_vector_and_rejects_replay_stale_and_bad_signature()
    {
        VibeChatPluginSdk.ComputeSignature(PluginSdkCatalog.SandboxSecret, Body).Should().Be(Golden);

        var fresh = VibeChatPluginSdk.Verify(
            PluginSdkCatalog.SandboxSecret, Body, Golden, "delivery-1", Now, Now, []);
        fresh.Accepted.Should().BeTrue();
        fresh.Reason.Should().Be(PluginSdkErrors.Accepted);

        VibeChatPluginSdk.Verify(
            PluginSdkCatalog.SandboxSecret, Body, Golden.ToUpperInvariant(), "delivery-1", Now, Now, [])
            .Reason.Should().Be(PluginSdkErrors.InvalidSignature);

        VibeChatPluginSdk.Verify(
            PluginSdkCatalog.SandboxSecret, Body, Golden, "delivery-1", Now, Now, ["delivery-1"])
            .Reason.Should().Be(PluginSdkErrors.ReplayDetected);

        VibeChatPluginSdk.Verify(
            PluginSdkCatalog.SandboxSecret, Body, Golden, "delivery-1", Now.AddMinutes(-6), Now, [])
            .Reason.Should().Be(PluginSdkErrors.StaleDelivery);

        VibeChatPluginSdk.Verify(
            PluginSdkCatalog.SandboxSecret, Body, Golden, "delivery-1", Now.AddMinutes(2), Now, [])
            .Reason.Should().Be(PluginSdkErrors.StaleDelivery);

        VibeChatPluginSdk.Verify(
            PluginSdkCatalog.SandboxSecret, Body, Golden, "  ", Now, Now, [])
            .Reason.Should().Be(PluginSdkErrors.DeliveryIdRequired);
    }

    [Fact]
    public void Matrix_keeps_the_support_window_and_catalog_omits_secrets()
    {
        PluginSdkMatrix.IsCompatible(PluginSdkCatalog.Version, PluginSdkCatalog.ApiContract, PublicEventCatalog.MessageCreated, 1, Now)
            .Should().BeTrue();
        PluginSdkMatrix.IsCompatible(PluginSdkCatalog.Version, PluginSdkCatalog.ApiContract, PublicEventCatalog.MessageCreated, 2, Now)
            .Should().BeTrue();
        PluginSdkMatrix.IsCompatible("0.9.0", PluginSdkCatalog.ApiContract, PublicEventCatalog.MessageCreated, 1, Now)
            .Should().BeFalse();
        PluginSdkMatrix.IsCompatible(PluginSdkCatalog.Version, "2", PublicEventCatalog.MessageCreated, 2, Now)
            .Should().BeFalse();
        PluginSdkMatrix.IsCompatible(
                PluginSdkCatalog.Version,
                PluginSdkCatalog.ApiContract,
                PublicEventCatalog.MessageCreated,
                1,
                new DateTimeOffset(2027, 4, 19, 0, 0, 0, TimeSpan.Zero))
            .Should().BeFalse();

        var first = PluginSdkCatalog.Digest();
        PluginSdkCatalog.Digest().Should().Be(first);

        var document = PluginSdkCatalog.Document(Now).ToJsonString();
        document.Should().Contain("\"sdkVersion\":\"1.0.0\"").And.Contain("Apache-2.0");
        document.Should().Contain("bot").And.Contain("slash").And.Contain("event-consumer").And.Contain("connector");
        document.Should().Contain("sandbox.invalid").And.NotContain("vc_int_").And.NotContain("vc_cb_");
        document.Should().NotContain(PluginSdkCatalog.SandboxSecret);
    }

    [Fact]
    public void Contract_runner_passes_without_leaking_the_token()
    {
        var report = PluginSdkContractRunner.Report(Now, "foreign-workspace-marker");
        ((JsonValue)report["passed"]!).GetValue<bool>().Should().BeTrue();
        var json = report.ToJsonString();
        json.Should().NotContain("vc_int_").And.NotContain("foreign-workspace-marker");
        json.Should().Contain("unauthorized").And.Contain("forbidden").And.Contain("cross-tenant");
    }

    [Fact]
    public void Scrub_removes_tokens_and_packages_declare_license()
    {
        VibeChatPluginSdk.Scrub("seen vc_int_abc123 and vc_cb_def456 Bearer raw-token tail")
            .Should().Be("seen [redacted] and [redacted] Bearer [redacted] tail");

        var request = VibeChatPluginSdk.SandboxSend(Guid.NewGuid(), Guid.NewGuid(), "hello", "quickstart-key");
        request.Headers.Authorization.Should().BeNull();
        request.Headers.Contains(DeveloperPortalCatalog.TokenHeader).Should().BeFalse();
        PluginSdkIdempotency.TryNormalize("quickstart-key", out _).Should().BeTrue();

        File.ReadAllText(FindRepoFile(Path.Combine("sdks", "dotnet", "VibeChat.Sdk", "VibeChat.Sdk.csproj")))
            .Should().Contain("<Version>1.0.0</Version>").And.Contain("Apache-2.0");
        var package = File.ReadAllText(FindRepoFile(Path.Combine("sdks", "typescript", "package.json")));
        package.Should().Contain("\"version\": \"1.0.0\"").And.Contain("Apache-2.0");
        var provenance = File.ReadAllText(FindRepoFile(Path.Combine("sdks", "typescript", "provenance.json")));
        provenance.Should().Contain("\"published\": false").And.Contain("Apache-2.0").And.NotContain("vc_int_");
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
