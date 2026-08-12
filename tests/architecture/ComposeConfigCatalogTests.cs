using System.Text.RegularExpressions;
using System.Text.Json;
using FluentAssertions;

namespace VibeChat.ArchitectureTests;

/// <summary>B-105 — self-host config catalog: compose injects promised env bindings.</summary>
public sealed class ComposeConfigCatalogTests
{
    private static readonly string[] ApiEmailBindings =
    [
        "Email__Enabled:",
        "Email__Smtp__Host:",
        "Email__Smtp__Port:",
        "Email__Smtp__Username:",
        "Email__Smtp__Password:",
        "Email__Smtp__From:",
        "Email__Smtp__UseStartTls:"
    ];

    private static readonly string[] WorkerRetentionBindings =
    [
        "MessageRetention__Enabled:",
        "MessageRetention__DefaultRetentionDays:",
        "MessageRetention__BatchSize:",
        "MessageRetention__IntervalMinutes:"
    ];

    private static readonly string[] ApiAiBindings =
    [
        "Ai__Enabled:",
        "Ai__Provider:",
        "Ai__OpenRouter__ApiKey:",
        "Ai__OpenRouter__BaseUrl:"
    ];

    [Fact]
    public void Apps_profile_api_injects_email_and_ai_bindings_from_env()
    {
        var compose = ReadRepoFile("compose.yaml");
        var apiBlock = ExtractServiceEnvironmentBlock(compose, "api");

        foreach (var binding in ApiEmailBindings)
        {
            apiBlock.Should().Contain(binding, because: $"B-105 requires {binding.TrimEnd(':')} on api container");
        }

        foreach (var binding in ApiAiBindings)
        {
            apiBlock.Should().Contain(binding, because: $"B-105 requires {binding.TrimEnd(':')} on api container");
        }

        apiBlock.Should().Contain("Ai__OpenRouter__ApiKey: ${OPENROUTER_API_KEY",
            because: "canonical OpenRouter secret uses OPENROUTER_API_KEY in .env.example");
    }

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
    public void Staging_bootstrap_exposes_keycloak_schema_and_initial_owner_bindings()
    {
        var compose = ReadRepoFile("compose.yaml");
        var keycloakBlock = ExtractServiceEnvironmentBlock(compose, "keycloak");
        var apiBlock = ExtractServiceEnvironmentBlock(compose, "api");

        keycloakBlock.Should().Contain("KC_DB_SCHEMA: ${KEYCLOAK_DB_SCHEMA:-public}");
        keycloakBlock.Should().Contain("VIBECHAT_INITIAL_ADMIN_PASSWORD: ${VIBECHAT_INITIAL_ADMIN_PASSWORD:-}");
        apiBlock.Should().Contain("Seed__InitialAdminEmail: ${SEED_INITIAL_ADMIN_EMAIL:-}");
    }

    [Fact]
    public void Keycloak_waits_for_idempotent_schema_initializer()
    {
        var compose = ReadRepoFile("compose.yaml");
        var keycloakBlock = ExtractServiceBlock(compose, "keycloak");
        var initializerBlock = ExtractServiceBlock(compose, "keycloak-schema");
        var initializer = ReadRepoFile(Path.Combine("infra", "compose", "postgres", "04-keycloak-schema.sh"));

        keycloakBlock.Should().Contain("keycloak-schema:");
        keycloakBlock.Should().Contain("condition: service_completed_successfully");
        initializerBlock.Should().Contain("04-keycloak-schema.sh");
        initializer.Should().Contain("CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION %I");
        initializer.Should().Contain("*[!a-zA-Z0-9_]*");
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
    public void Apps_profile_worker_injects_message_retention_bindings_from_env()
    {
        var compose = ReadRepoFile("compose.yaml");
        var workerBlock = ExtractServiceEnvironmentBlock(compose, "worker");

        foreach (var binding in WorkerRetentionBindings)
        {
            workerBlock.Should().Contain(binding,
                because: $"B-105 requires {binding.TrimEnd(':')} on worker container");
        }
    }

    [Fact]
    public void Env_example_declares_substitutions_used_by_apps_profile()
    {
        var compose = ReadRepoFile("compose.yaml");
        var envExample = ReadRepoFile(".env.example");
        var appsSection = ExtractAppsProfileSection(compose);

        var referenced = Regex.Matches(appsSection, @"\$\{([A-Za-z0-9_]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        referenced.Should().NotBeEmpty(because: "apps profile must reference env substitutions");

        foreach (var key in referenced)
        {
            envExample.Should().MatchRegex($"(?m)^{Regex.Escape(key)}=",
                because: $".env.example must declare {key} used by profile apps");
        }
    }

    [Fact]
    public void Env_example_documents_canonical_email_and_openrouter_keys()
    {
        var envExample = ReadRepoFile(".env.example");

        envExample.Should().Contain("EMAIL__Enabled=");
        envExample.Should().Contain("OPENROUTER_API_KEY=");
        envExample.Should().NotContain("AI__OpenRouter__ApiKey=",
            because: "duplicate OpenRouter alias removed; compose maps OPENROUTER_* only");
    }

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
