using System.Text.RegularExpressions;
using System.Text.Json;
using FluentAssertions;

namespace VibeChat.ArchitectureTests;

/// <summary>B-105 / B-187 — infra catalog in .env.example; product may stay as compose fallbacks.</summary>
public sealed class ComposeConfigCatalogTests
{
    private static readonly string[] ProductEnvPrefixes =
    [
        "EMAIL__",
        "SMTP_",
        "AI__",
        "OPENROUTER_",
        "MessageRetention__",
        "LinkPreview__",
        "Push__"
    ];

    [Fact]
    public void Apps_profile_api_exposes_safe_database_bootstrap_switch()
    {
        var compose = ReadRepoFile("compose.yaml");
        var apiBlock = ExtractServiceEnvironmentBlock(compose, "api");

        apiBlock.Should().Contain(
            "Database__BootstrapOnStartup: ${DATABASE_BOOTSTRAP_ON_STARTUP:-false}",
            because: "staging first boot needs an explicit migration switch without enabling demo seed data");
    }

    [Fact]
    public void Staging_bootstrap_exposes_initial_owner_without_enabling_demo_seed()
    {
        var compose = ReadRepoFile("compose.yaml");
        var keycloakBlock = ExtractServiceEnvironmentBlock(compose, "keycloak");
        var apiBlock = ExtractServiceEnvironmentBlock(compose, "api");

        keycloakBlock.Should().Contain("VIBECHAT_INITIAL_ADMIN_PASSWORD: ${VIBECHAT_INITIAL_ADMIN_PASSWORD:-}");
        apiBlock.Should().Contain("Bootstrap__Enabled: ${BOOTSTRAP_ENABLED:-false}");
        apiBlock.Should().Contain("Bootstrap__InitialAdminEmail: ${BOOTSTRAP_INITIAL_ADMIN_EMAIL:-}");
        apiBlock.Should().Contain("Bootstrap__WorkspaceName: ${BOOTSTRAP_WORKSPACE_NAME:-VibeChat Alpha}");
        apiBlock.Should().Contain("Bootstrap__WorkspaceSlug: ${BOOTSTRAP_WORKSPACE_SLUG:-vibechat-alpha}");
        apiBlock.Should().Contain("Seed__Enabled: ${SEED_ENABLED:-false}");
    }

    [Fact]
    public void Staging_realm_emits_subject_and_imports_temporary_admin_without_committed_password()
    {
        using var realm = JsonDocument.Parse(ReadRepoFile(Path.Combine("infra", "keycloak", "realm-vibechat.staging.json")));
        var root = realm.RootElement;
        var web = root.GetProperty("clients").EnumerateArray()
            .Single(x => x.GetProperty("clientId").GetString() == "vibechat-web");
        var subjectMapper = web.GetProperty("protocolMappers").EnumerateArray()
            .Single(x => x.GetProperty("protocolMapper").GetString() == "oidc-sub-mapper");

        subjectMapper.GetProperty("config").GetProperty("access.token.claim").GetString().Should().Be("true");
        subjectMapper.GetProperty("config").GetProperty("lightweight.claim").GetString().Should().Be("true");

        var admin = root.GetProperty("users").EnumerateArray()
            .Single(x => x.GetProperty("username").GetString() == "admin");
        admin.GetProperty("credentials")[0].GetProperty("value").GetString()
            .Should().Be("${VIBECHAT_INITIAL_ADMIN_PASSWORD}");
        admin.GetProperty("credentials")[0].GetProperty("temporary").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Env_example_declares_infra_substitutions_used_without_default()
    {
        var compose = ReadRepoFile("compose.yaml");
        var envExample = ReadRepoFile(".env.example");
        var appsSection = ExtractAppsProfileSection(compose);

        var required = Regex.Matches(appsSection, @"\$\{([A-Za-z0-9_]+)(:-([^}]*)?)?\}")
            .Select(m => new
            {
                Key = m.Groups[1].Value,
                HasDefaultClause = m.Groups[2].Success,
                DefaultValue = m.Groups[3].Value
            })
            .Where(x => !x.HasDefaultClause || string.IsNullOrEmpty(x.DefaultValue))
            .Select(x => x.Key)
            .Distinct()
            .Where(key => !IsProductEnvKey(key))
            .OrderBy(x => x)
            .ToArray();

        required.Should().NotBeEmpty(because: "apps profile must reference infra substitutions");

        foreach (var key in required)
        {
            envExample.Should().MatchRegex($"(?m)^{Regex.Escape(key)}=",
                because: $".env.example must declare infra {key} used without a compose default");
        }
    }

    [Fact]
    public void Env_example_is_infra_only_and_has_lab_keyring()
    {
        var envExample = ReadRepoFile(".env.example");

        foreach (var prefix in ProductEnvPrefixes)
        {
            envExample.Should().NotMatchRegex(
                $"(?m)^{Regex.Escape(prefix)}",
                because: $"B-187 removes product var {prefix}* from .env.example");
        }

        envExample.Should().Contain("RuntimeSettings__DatabaseOverridesEnabled=true");
        envExample.Should().Contain("RuntimeSettings__Encryption__Keys__1=");
        envExample.Should().NotContain("CHANGE_ME_base64_32_bytes");
        envExample.Should().Contain("VmliZUNoYXRMYWJLZXlyaW5nRGVtb09ubHkhISEhISE=");
    }

    private static bool IsProductEnvKey(string key) =>
        ProductEnvPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));

    private static string ExtractAppsProfileSection(string compose)
    {
        var apiIndex = compose.IndexOf("\n  api:", StringComparison.Ordinal);
        apiIndex.Should().BeGreaterThan(0);

        var proxyIndex = compose.IndexOf("\n  proxy:", StringComparison.Ordinal);
        var end = proxyIndex > apiIndex ? proxyIndex : compose.Length;
        return compose[apiIndex..end];
    }

    private static string ExtractServiceEnvironmentBlock(string compose, string serviceName)
    {
        var serviceBlock = ExtractServiceBlock(compose, serviceName);
        var envMarker = "environment:";
        var envStart = serviceBlock.IndexOf(envMarker, StringComparison.Ordinal);
        envStart.Should().BeGreaterThanOrEqualTo(0, because: $"{serviceName} must expose environment block");

        return serviceBlock[envStart..];
    }

    private static string ExtractServiceBlock(string compose, string serviceName)
    {
        var marker = $"\n  {serviceName}:";
        var start = compose.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, because: $"compose must define service {serviceName}");

        var nextService = Regex.Match(
            compose[(start + marker.Length)..],
            @"\n  [a-z][a-z0-9-]*:");

        var end = nextService.Success
            ? start + marker.Length + nextService.Index
            : compose.Length;

        return compose[start..end];
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' from test base directory.");
    }
}
