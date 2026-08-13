using FluentAssertions;
using NetArchTest.Rules;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Messaging;
using VibeChat.Search;
using VibeChat.Tenancy;

namespace VibeChat.ArchitectureTests;

public sealed class ArchitectureRulesTests
{
    [Fact]
    public void Messaging_module_does_not_reference_administration_internals()
    {
        var result = Types.InAssembly(typeof(Message).Assembly)
            .ShouldNot()
            .HaveDependencyOn("VibeChat.Administration")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Core_domain_modules_do_not_reference_infrastructure_or_api()
    {
        var assemblies = new[]
        {
            typeof(Message).Assembly,
            typeof(Channel).Assembly,
            typeof(Workspace).Assembly,
            typeof(UserProfile).Assembly,
            typeof(ISearchQuery).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("VibeChat.Infrastructure", "VibeChat.Api")
                .GetResult();

            result.IsSuccessful.Should().BeTrue();
        }
    }

    [Fact]
    public void Rls_catalog_covers_all_tenant_scoped_business_tables()
    {
        var sqlPath = FindRepoFile(Path.Combine("infra", "compose", "postgres", "03-rls.sql"));
        var sql = File.ReadAllText(sqlPath);

        // Keep in sync with VibeChatDbContext tables that carry TenantId (identity.user_profiles excluded).
        string[] tenantTables =
        [
            "tenancy.workspaces",
            "tenancy.workspace_members",
            "directory.spaces",
            "conversations.channels",
            "conversations.channel_members",
            "messaging.messages",
            "messaging.threads",
            "files.attachments",
            "messaging.reactions",
            "messaging.message_mentions",
            "messaging.read_cursors",
            "messaging.conversation_sequences",
            "messaging.idempotency",
            "messaging.message_retention_settings",
            "messaging.link_previews",
            "messaging.message_link_previews",
            "messaging.link_preview_settings",
            "building_blocks.outbox_messages",
            "audit.audit_events",
            "ai.usage_records",
            "ai.settings",
            "notifications.preferences",
            "notifications.email_settings",
            "integrations.webhook_endpoints",
            "files.settings",
            "building_blocks.rate_limit_settings"
        ];

        foreach (var table in tenantTables)
        {
            sql.Should().Contain(table, because: $"RLS catalog must enable policy coverage for {table}");
            var policyNeedle = $"ON {table}";
            sql.Should().Contain(policyNeedle, because: $"RLS catalog must define a policy on {table}");
            sql.Should().Contain($"ALTER TABLE IF EXISTS {table} FORCE ROW LEVEL SECURITY",
                because: $"SEC-RLS-RUNTIME requires FORCE RLS on {table}");
        }

        sql.Should().Contain("WITH CHECK (\"TenantId\" = app.current_tenant_id())",
            because: "SEC-RLS-RUNTIME requires WITH CHECK on tenant writes");
        sql.Should().Contain("app.current_tenant_id()",
            because: "RLS policies must read app.tenant_id via fail-closed helper");
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

        throw new FileNotFoundException($"Could not locate '{relativePath}' from test base directory.");
    }

    [Fact]
    public void Mutable_api_v1_maps_declare_RequirePermission_or_exempt()
    {
        // B-174: offline gate — between each v1.Map(Post|Put|Patch|Delete) and the next
        // v1.Map* / app.Map* / app.Run, RequirePermission or AllowPermissionGateExempt must appear.
        var programPath = FindRepoFile(Path.Combine("apps", "api", "Program.cs"));
        var source = File.ReadAllText(programPath);
        var mapPattern = new System.Text.RegularExpressions.Regex(
            @"\bv1\.Map(?<verb>Post|Put|Patch|Delete)\s*\(\s*""(?<path>[^""]+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        var matches = mapPattern.Matches(source).Cast<System.Text.RegularExpressions.Match>().ToArray();
        var boundaryPattern = new System.Text.RegularExpressions.Regex(
            @"\b(?:v1\.Map|app\.Map|app\.Run)\b",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var violations = new List<string>();
        for (var i = 0; i < matches.Length; i++)
        {
            var map = matches[i];
            var regionStart = map.Index;
            var regionEnd = source.Length;
            var nextBoundary = boundaryPattern.Match(source, map.Index + map.Length);
            if (nextBoundary.Success)
            {
                regionEnd = nextBoundary.Index;
            }

            var region = source[regionStart..regionEnd];
            if (region.Contains(".RequirePermission(", StringComparison.Ordinal)
                || region.Contains(".AllowPermissionGateExempt(", StringComparison.Ordinal))
            {
                continue;
            }

            violations.Add($"{map.Groups["verb"].Value} {map.Groups["path"].Value}");
        }

        violations.Should().BeEmpty(
            "B-174: mutable Map* must chain RequirePermission or AllowPermissionGateExempt. Missing:\n"
            + string.Join('\n', violations));
    }

    [Fact]
    public void Api_v1_maps_are_listed_in_authz_matriz()
    {
        // B-175: every Minimal API path under /api/v1 must appear in the authZ matrix.
        var programPath = FindRepoFile(Path.Combine("apps", "api", "Program.cs"));
        var matrizPath = FindRepoFile(Path.Combine("docs", "security", "authz-matriz.md"));
        var source = File.ReadAllText(programPath);
        var matriz = File.ReadAllText(matrizPath);
        var mapPattern = new System.Text.RegularExpressions.Regex(
            @"\bv1\.Map(?:Get|Post|Put|Patch|Delete)\s*\(\s*""(?<path>[^""]+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var missing = mapPattern.Matches(source)
            .Select(m => m.Groups["path"].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(path => !matriz.Contains($"`{path}`", StringComparison.Ordinal))
            .ToArray();

        missing.Should().BeEmpty(
            "B-175: each v1.Map* path must be listed in docs/security/authz-matriz.md. Missing:\n"
            + string.Join('\n', missing));
    }

    [Fact]
    public void Nginx_configs_include_documented_csp()
    {
        var headersPath = FindRepoFile(Path.Combine("infra", "nginx", "security-headers.conf"));
        var headers = File.ReadAllText(headersPath);

        headers.Should().Contain("Content-Security-Policy", because: "B-077 requires CSP on the official web/proxy path");
        headers.Should().Contain("default-src 'self'", because: "CSP baseline must restrict default origins to self");
        headers.Should().Contain("http://localhost:5080", because: "lab self-host web calls API/SignalR on :5080 (connect-src)");
        headers.Should().Contain("ws://localhost:5080", because: "SignalR WebSocket lab origin must be in connect-src");
        headers.Should().NotContain("unsafe-eval", because: "B-077 forbids masking XSS with unsafe-eval");
        headers.Should().NotContain("*", because: "B-077 forbids wildcard CSP sources in production reference");

        foreach (var relativePath in new[] { Path.Combine("infra", "proxy", "nginx.conf"), Path.Combine("apps", "web", "nginx.conf") })
        {
            var nginx = File.ReadAllText(FindRepoFile(relativePath));
            nginx.Should().Contain("security-headers.conf",
                because: $"{relativePath} must include the shared B-077 header snippet");
        }
    }
}
