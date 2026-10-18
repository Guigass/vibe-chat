using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Identity;

namespace VibeChat.UnitTests;

public sealed class SsoLinkingTests
{
    [Fact]
    public void Email_match_does_not_attach_a_new_broker_identity_to_an_existing_user()
    {
        var admin = Guid.NewGuid();
        var decision = SsoLinkPolicy.Decide(new SsoLinkFacts(
            ProfileByKeycloakSubject: null,
            ProfileIsPending: false,
            PendingByEmail: null,
            LinkedUserId: null));

        decision.Kind.Should().Be(SsoLinkKind.CreateUnprivileged);
        decision.UserId.Should().BeNull();
        decision.UserId.Should().NotBe(admin);
    }

    [Fact]
    public void Pending_invite_is_the_explicit_link()
    {
        var pending = Guid.NewGuid();
        var decision = SsoLinkPolicy.Decide(new SsoLinkFacts(null, false, pending, null));

        decision.Kind.Should().Be(SsoLinkKind.ClaimPending);
        decision.UserId.Should().Be(pending);
        decision.Reason.Should().Be(SsoLinkReasons.PendingInvite);
    }

    [Fact]
    public void Existing_keycloak_subject_links_without_using_email()
    {
        var user = Guid.NewGuid();
        var decision = SsoLinkPolicy.Decide(new SsoLinkFacts(user, false, Guid.NewGuid(), null));

        decision.Kind.Should().Be(SsoLinkKind.UseExisting);
        decision.UserId.Should().Be(user);
        decision.Reason.Should().Be(SsoLinkReasons.ExistingSubject);
    }

    [Fact]
    public void Stable_pair_is_not_rebound_to_another_keycloak_subject()
    {
        var linked = Guid.NewGuid();
        var other = Guid.NewGuid();
        var decision = SsoLinkPolicy.Decide(new SsoLinkFacts(other, false, null, linked));

        decision.Kind.Should().Be(SsoLinkKind.Conflict);
        decision.UserId.Should().BeNull();
    }

    [Fact]
    public void Same_subject_on_another_issuer_is_not_the_same_link()
    {
        var first = SsoBrokerClaimsReader.TryRead(Claims("https://idp.example/a/", "user-1"), out var left);
        var second = SsoBrokerClaimsReader.TryRead(Claims("https://idp.example/b", "user-1"), out var right);

        first.Should().BeTrue();
        second.Should().BeTrue();
        left.Issuer.Should().NotBe(right.Issuer);
        left.Subject.Should().Be(right.Subject);
        left.Issuer.Should().Be("https://idp.example/a");
    }

    [Fact]
    public void External_issuer_is_rejected_and_keycloak_issuer_matches_with_or_without_slash()
    {
        KeycloakIssuerPolicy.Matches(
            "https://login.microsoftonline.com/tenant/v2.0",
            "http://localhost:8080/realms/vibechat").Should().BeFalse();
        KeycloakIssuerPolicy.Matches(
            "http://localhost:8080/realms/vibechat/",
            "http://localhost:8080/realms/vibechat").Should().BeTrue();
    }

    [Fact]
    public void Directory_uses_display_name_hides_disabled_providers_and_drops_secrets()
    {
        const string json = """
            [
              {"alias":"hub-oidc","displayName":"Logar com HUB","providerId":"oidc","enabled":true,"hideOnLogin":false,"config":{"clientSecret":"change_me","domains":"hub.example, bad domain"}},
              {"alias":"hidden","displayName":"Oculto","enabled":true,"hideOnLogin":true},
              {"alias":"off","displayName":"Desligado","enabled":false,"hideOnLogin":false},
              {"alias":"blank","displayName":"  ","enabled":true,"config":{"domains":"blank.example"}}
            ]
            """;

        var providers = SsoProviderParser.Parse(json);

        providers.Should().HaveCount(2);
        providers[0].DisplayName.Should().Be("Logar com HUB");
        providers[0].Domains.Should().Equal("hub.example");
        providers[1].DisplayName.Should().BeEmpty();
        JsonSerializer.Serialize(providers).Should().NotContain("change_me");
    }

    [Fact]
    public void Directory_circuit_opens_on_the_third_failure()
    {
        var circuit = new SsoDirectoryCircuit();
        var now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");
        circuit.RecordFailure(now);
        circuit.RecordFailure(now);
        circuit.IsOpen(now).Should().BeFalse();
        circuit.RecordFailure(now);
        circuit.IsOpen(now.AddSeconds(59)).Should().BeTrue();
        circuit.IsOpen(now.AddSeconds(61)).Should().BeFalse();
        circuit.RecordSuccess();
        circuit.RecordFailure(now);
        circuit.IsOpen(now).Should().BeFalse();
    }

    [Fact]
    public void Realm_examples_ship_oidc_and_saml_brokers_with_a_custom_label()
    {
        var json = File.ReadAllText(FindRepoFile(Path.Combine("infra", "keycloak", "realm-vibechat.json")));
        using var document = JsonDocument.Parse(json);
        var providers = document.RootElement.GetProperty("identityProviders").EnumerateArray().ToArray();
        var oidc = providers.Single(item => item.GetProperty("alias").GetString() == "hub-oidc");
        var saml = providers.Single(item => item.GetProperty("alias").GetString() == "hub-saml");

        oidc.GetProperty("providerId").GetString().Should().Be("oidc");
        oidc.GetProperty("displayName").GetString().Should().Be("Logar com HUB");
        oidc.GetProperty("enabled").GetBoolean().Should().BeFalse();
        oidc.GetProperty("config").GetProperty("clientSecret").GetString().Should().Be("change_me");
        saml.GetProperty("providerId").GetString().Should().Be("saml");
        saml.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        saml.GetProperty("config").GetProperty("signingCertificate").GetString().Should().Be("CHANGE_ME");

        var scope = document.RootElement.GetProperty("clientScopes").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "vibechat-broker");
        var claims = scope.GetProperty("protocolMappers").EnumerateArray()
            .Select(item => item.GetProperty("config").GetProperty("claim.name").GetString())
            .ToArray();
        claims.Should().BeEquivalentTo("broker_alias", "broker_subject", "broker_issuer");
    }

    private static IEnumerable<Claim> Claims(string issuer, string subject) =>
    [
        new(SsoClaimNames.Issuer, issuer),
        new(SsoClaimNames.Subject, subject)
    ];

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
