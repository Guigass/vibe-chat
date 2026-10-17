using System.Net;
using FluentAssertions;
using VibeChat.BuildingBlocks;
using VibeChat.Integrations;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class ConnectorPolicyTests
{
    [Fact]
    public void Catalog_shows_minimum_scopes_and_signature_is_stable()
    {
        var github = ConnectorPolicies.Find(ConnectorPolicies.ProviderGitHub);
        github!.Scopes.Should().Equal("issues:read", "issues:write");
        ConnectorPolicies.Find(ConnectorPolicies.ProviderGitLab)!.Scopes.Should().Equal("read_api", "write_repository");
        ConnectorPolicies.Find(ConnectorPolicies.ProviderCalendar)!.Scopes.Should().Equal("calendar:read", "calendar:write");
        ConnectorPolicies.ScopesMatch(["issues:write", "issues:read"], github.Scopes).Should().BeTrue();
        ConnectorPolicies.ScopesMatch(["repo"], github.Scopes).Should().BeFalse();

        var body = """{"issue":{"id":7,"title":"Hello"}}""";
        var first = ConnectorPolicies.ComputeSignature("connector-token-test-0001", body);
        ConnectorPolicies.ComputeSignature("connector-token-test-0001", body).Should().Be(first);
        ConnectorPolicies.SignatureMatches("connector-token-test-0001", body, first.ToUpperInvariant()).Should().BeTrue();
        ConnectorPolicies.SignatureMatches("other-token-value-0002", body, first).Should().BeFalse();
    }

    [Fact]
    public void Private_metadata_and_rebinding_targets_are_blocked()
    {
        ConnectorPolicies.IsBlockedAddress(IPAddress.Parse("169.254.169.254")).Should().BeTrue();
        ConnectorPolicies.IsBlockedAddress(IPAddress.Parse("10.1.2.3")).Should().BeTrue();
        ConnectorPolicies.IsBlockedAddress(IPAddress.Parse("127.0.0.1")).Should().BeTrue();
        ConnectorPolicies.IsBlockedAddress(IPAddress.Parse("192.168.1.9")).Should().BeTrue();
        ConnectorPolicies.IsBlockedAddress(IPAddress.Parse("1.1.1.1")).Should().BeFalse();
        ConnectorPolicies.IsBlockedHost("metadata.google.internal").Should().BeTrue();

        var allow = new[] { "api.github.com", "github.com" };
        var blocked = ConnectorPolicies.Authorize(
            new Uri("https://169.254.169.254/latest"),
            [IPAddress.Parse("169.254.169.254")],
            [IPAddress.Parse("169.254.169.254")],
            ["169.254.169.254"]);
        blocked.Allowed.Should().BeFalse();
        blocked.ErrorCode.Should().Be("EgressBlocked");

        var rebinding = ConnectorPolicies.Authorize(
            new Uri("https://api.github.com/"),
            [IPAddress.Parse("1.1.1.1")],
            [IPAddress.Parse("169.254.169.254")],
            allow);
        rebinding.Allowed.Should().BeFalse();
        rebinding.ErrorCode.Should().Be("DnsRebinding");

        var redirect = ConnectorPolicies.Authorize(
            new Uri("https://evil.example/hook"),
            [IPAddress.Parse("1.1.1.1")],
            [IPAddress.Parse("1.1.1.1")],
            allow);
        redirect.Allowed.Should().BeFalse();

        var allowed = ConnectorPolicies.Authorize(
            new Uri("https://api.github.com/"),
            [IPAddress.Parse("1.1.1.1")],
            [IPAddress.Parse("1.1.1.1")],
            allow);
        allowed.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Revoke_and_circuit_stop_calls_without_touching_a_shared_outbox()
    {
        var revoked = new ConnectorInstallation { Status = ConnectorPolicies.StatusRevoked };
        ConnectorPolicies.MayCall(revoked, DateTimeOffset.UtcNow, out var revokedError).Should().BeFalse();
        revokedError.Should().Be("ConnectorRevoked");

        var open = new ConnectorInstallation
        {
            Status = ConnectorPolicies.StatusActive,
            Circuit = ConnectorPolicies.CircuitOpen,
            OpenUntil = DateTimeOffset.UtcNow.AddMinutes(1),
        };
        ConnectorPolicies.MayCall(open, DateTimeOffset.UtcNow, out var openError).Should().BeFalse();
        openError.Should().Be("CircuitOpen");

        var now = DateTimeOffset.Parse("2026-10-17T12:00:00Z");
        var first = ConnectorPolicies.RecordFailure(0, now);
        first.Circuit.Should().Be(ConnectorPolicies.CircuitClosed);
        first.NextAttemptAt.Should().BeAfter(now);
        var third = ConnectorPolicies.RecordFailure(2, now);
        third.Circuit.Should().Be(ConnectorPolicies.CircuitOpen);
        third.OpenUntil.Should().Be(now.Add(ConnectorPolicies.CircuitOpenFor));
        ConnectorPolicies.RecordSuccess().Failures.Should().Be(0);
    }

    [Fact]
    public void Inbound_parser_keeps_only_the_external_id_and_a_short_title()
    {
        ConnectorPolicies.TryReadEvent(
            ConnectorPolicies.ProviderGitHub,
            """{"tenantId":"other","issue":{"id":9,"title":"Hello\n<script>"}}""",
            out var id,
            out var title,
            out _).Should().BeTrue();
        id.Should().Be("9");
        title.Should().Be("Helloscript");
        ConnectorPolicies.Summary("github", id, title).Should().NotContain("tenantId").And.NotContain("<");

        ConnectorPolicies.TryReadEvent(
            ConnectorPolicies.ProviderGitLab,
            """{"object_attributes":{"id":3,"title":"Lab"}}""",
            out var gitlabId,
            out var gitlabTitle,
            out _).Should().BeTrue();
        gitlabId.Should().Be("3");
        gitlabTitle.Should().Be("Lab");

        ConnectorPolicies.TryReadEvent(
            ConnectorPolicies.ProviderCalendar,
            """{"uid":"evt-1","summary":"Standup"}""",
            out var calendarId,
            out var calendarTitle,
            out _).Should().BeTrue();
        calendarId.Should().Be("evt-1");
        calendarTitle.Should().Be("Standup");
    }

    [Fact]
    public void Connector_manage_is_admin_only()
    {
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Connector.Manage);
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Connector.Manage);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Connector.Manage);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Connector.Manage);
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Connector.Manage);
        RolePermissionCatalog.For(Role.Bot).Should().NotContain(Permissions.Connector.Manage);
    }
}
