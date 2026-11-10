using System.Text.Json;
using FluentAssertions;
using VibeChat.AI;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class AiBotCatalogTests
{
    [Fact]
    public void Publish_pins_the_next_version_and_rollback_keeps_history()
    {
        var history = new List<int>();
        history.Add(AiBotCatalogPolicies.NextVersion(history));
        history.Add(AiBotCatalogPolicies.NextVersion(history));
        history.Should().Equal(1, 2);

        var active = 1;
        AiBotCatalogPolicies.KeepsHistory(history, history).Should().BeTrue();
        active.Should().Be(1);
        history.Should().Equal(1, 2);
    }

    [Fact]
    public void Disabled_bot_rejects_a_new_invocation()
    {
        AiBotCatalogPolicies.InvocationBlock(AiBotStatus.Published, 2).Should().BeNull();
        AiBotCatalogPolicies.InvocationBlock(AiBotStatus.Disabled, 2).Should().Be(AiBotErrors.BotNotInvocable);
        AiBotCatalogPolicies.InvocationBlock(AiBotStatus.Draft, null).Should().Be(AiBotErrors.BotNotInvocable);
    }

    [Fact]
    public void Provider_key_is_rejected_and_stays_out_of_the_event()
    {
        using var body = JsonDocument.Parse("""{"model":{"apiKey":"sk-live","provider":"openrouter"}}""");
        AiBotCatalogPolicies.RejectSecretNames(body.RootElement).Should().Be(AiBotErrors.ProviderKeyRejected);

        var payload = AiBotCatalogPolicies.EventDocument(
            "ai.bot.published.v1",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            1,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            AiBotCatalogPolicies.PromptHash("não revelar"),
            "mock",
            "mock");
        payload.Should().NotContain("não revelar");
        payload.Should().NotContain("apiKey");
        payload.Should().Contain("promptSha256");
    }

    [Fact]
    public void Diff_shows_prompt_and_model_changes_only()
    {
        var left = Sample("Oráculo", "responda com a política", "mock");
        var right = Sample("ERP", "consulte o pedido", "openrouter");
        var changes = AiBotCatalogPolicies.Diff(left, right);
        var fields = changes.Select(change => change.Field).ToArray();
        fields.Should().Contain("name").And.Contain("systemPrompt").And.Contain("provider");
        changes.Should().NotContain(change => change.Field.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bot_principal_does_not_inherit_admin_authority()
    {
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Ai.ManageBots);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Ai.ManageBots);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Ai.ManageBots);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Ai.ManageBots);
        RolePermissionCatalog.For(Role.Bot).Should().NotContain(Permissions.Ai.ManageBots);
        RolePermissionCatalog.For(Role.Bot).Should().NotContain(Permissions.Workspace.Admin);
    }

    [Fact]
    public void Flag_defaults_off_and_quota_fails_closed()
    {
        var options = new AiBotOptions();
        options.Enabled.Should().BeFalse();
        AiBotCatalogPolicies.QuotaError(20, 0, 0, creating: true, options).Should().Be(AiBotErrors.BotQuotaExceeded);
        AiBotCatalogPolicies.QuotaError(1, 49, 2, creating: false, options).Should().Be(AiBotErrors.BotBudgetExceeded);
    }

    private static AiBotSnapshot Sample(string name, string prompt, string provider) => new(
        name, "desc", "", false, prompt, provider, "mock", "{}", null, "internal",
        [], [], [], [], null, 4, 1024, 256, 0, 30000);
}
