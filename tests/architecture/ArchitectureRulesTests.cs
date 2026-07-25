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
        }

        sql.Should().Contain("""USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)""");
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
}
