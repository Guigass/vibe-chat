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
            "building_blocks.outbox_messages",
            "audit.audit_events",
            "ai.usage_records",
            "ai.settings",
            "notifications.preferences",
            "notifications.email_settings",
            "integrations.webhook_endpoints"
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
    public void Nginx_configs_include_documented_csp()
    {
        var headersPath = FindRepoFile(Path.Combine("infra", "nginx", "security-headers.conf"));
        var headers = File.ReadAllText(headersPath);

        headers.Should().Contain("Content-Security-Policy", because: "B-077 requires CSP on the official web/proxy path");
        headers.Should().Contain("default-src 'self'", because: "CSP baseline must restrict default origins to self");
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
