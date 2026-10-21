using System.Text.Json;
using FluentAssertions;
using VibeChat.BuildingBlocks;
using VibeChat.Integrations;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class DeveloperPortalCatalogTests
{
    [Fact]
    public void OpenApi_paths_match_the_runtime_integration_surface_and_omit_secrets()
    {
        var document = DeveloperPortalCatalog.OpenApiDocument();
        var paths = document["paths"]!.AsObject().Select(item => item.Key).ToArray();
        paths.Should().Equal(DeveloperPortalCatalog.RuntimePaths);
        var json = document.ToJsonString();
        DeveloperPortalCatalog.ContainsSecret(json).Should().BeFalse();
        json.Should().Contain(DeveloperPortalCatalog.TokenHeader);
        json.Should().NotContain("?token");
    }

    [Fact]
    public void Contract_kit_carries_event_schemas_without_a_credential()
    {
        var kit = DeveloperPortalCatalog.ContractKit(liveSendEnabled: false);
        var json = kit.ToJsonString();
        json.Should().Contain(PublicEventCatalog.MessageCreated);
        json.Should().Contain("\"schema\"");
        json.Should().Contain("deprecated");
        json.Should().Contain(PublicEventCatalog.MessageCreatedV1RetiresOn.ToString("yyyy-MM-dd"));
        DeveloperPortalCatalog.ContainsSecret(json).Should().BeFalse();
        kit["liveSendEnabled"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Explorer_stays_inside_channel_and_dm_scopes()
    {
        var channel = Guid.NewGuid();
        var other = Guid.NewGuid();

        DeveloperPortalCatalog.Decide(true, false, false, [channel], DeveloperPortalCatalog.SendOperation, channel)
            .Allowed.Should().BeTrue();
        var denied = DeveloperPortalCatalog.Decide(true, false, false, [channel], DeveloperPortalCatalog.SendOperation, other);
        denied.Allowed.Should().BeFalse();
        denied.Error.Should().Be(DeveloperPortalErrors.ScopeDenied);

        DeveloperPortalCatalog.Decide(true, false, false, [channel], DeveloperPortalCatalog.DirectOperation, null)
            .Error.Should().Be(DeveloperPortalErrors.ScopeDenied);
        DeveloperPortalCatalog.Decide(true, false, true, [channel], DeveloperPortalCatalog.DirectOperation, null)
            .Allowed.Should().BeTrue();

        DeveloperPortalCatalog.Decide(true, true, true, [channel], DeveloperPortalCatalog.SendOperation, channel)
            .Error.Should().Be(DeveloperPortalErrors.Revoked);
        DeveloperPortalCatalog.Decide(false, false, true, [channel], DeveloperPortalCatalog.SendOperation, channel)
            .StatusCode.Should().Be(401);
    }

    [Fact]
    public void Synthetic_calls_are_marked_and_admin_holds_both_permissions()
    {
        var call = DeveloperPortalCatalog.SyntheticCall(Guid.NewGuid(), DeveloperPortalCatalog.SendOperation);
        call["synthetic"]!.GetValue<bool>().Should().BeTrue();
        call["delivered"]!.GetValue<bool>().Should().BeFalse();
        call["sandbox"]!.GetValue<bool>().Should().BeTrue();
        DeveloperPortalCatalog.ContainsSecret(call.ToJsonString()).Should().BeFalse();

        var webhook = DeveloperPortalCatalog.SyntheticWebhook();
        webhook["delivered"]!.GetValue<bool>().Should().BeFalse();
        webhook["eventType"]!.GetValue<string>().Should().Be(PublicEventCatalog.MessageCreated);

        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Integration.Read);
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Integration.Manage);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Integration.Read);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Integration.Manage);
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Integration.Read);
        RolePermissionCatalog.For(Role.Bot).Should().NotContain(Permissions.Integration.Manage);

        DeveloperPortalCatalog.Catalog(true)["rateLimit"]!["sendPerMinute"]!.GetValue<int>()
            .Should().Be(RateLimitPolicies.DefaultSendPerMinute);
    }
}
