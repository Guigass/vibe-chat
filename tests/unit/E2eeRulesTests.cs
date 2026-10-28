using FluentAssertions;
using VibeChat.Messaging;

namespace VibeChat.UnitTests;

public sealed class E2eeRulesTests
{
    private const string PublicJwk = """
        {"kty":"EC","crv":"P-256","x":"f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU","y":"x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0"}
        """;

    [Fact]
    public void Fingerprint_matches_the_canonical_vector()
    {
        E2eeRules.Fingerprint(PublicJwk).Should().Be("a8f3cb6cf809a56384e408f1476f354390efbd154ed0009ec9b5dfc6fe55512d");
    }

    [Fact]
    public void Public_jwk_rejects_a_private_scalar()
    {
        var withPrivate = PublicJwk.Replace("}", ",\"d\":\"secret\"}");
        E2eeRules.ValidatePublicJwk(withPrivate).Should().Be(E2eeErrors.DeviceInvalid);
        E2eeRules.ValidatePublicJwk(PublicJwk).Should().BeNull();
    }

    [Fact]
    public void Envelope_accepts_only_the_current_recipient_set()
    {
        var device = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var body = Envelope(2, device);

        E2eeRules.ValidateEnvelope(body, 2, [device]).Should().BeNull();
        E2eeRules.ValidateEnvelope("plain secret", 2, [device]).Should().Be(E2eeErrors.EnvelopeRequired);
        E2eeRules.ValidateEnvelope(body, 1, [device]).Should().Be(E2eeErrors.EpochStale);
        E2eeRules.ValidateEnvelope(body, 2, [device, other]).Should().Be(E2eeErrors.RecipientsMismatch);
        E2eeRules.ValidateEnvelope(body, 2, []).Should().Be(E2eeErrors.DeviceRequired);
    }

    [Fact]
    public void Backup_blob_must_be_a_sealed_package()
    {
        const string blob = """{"v":"1","salt":"c2FsdA","iv":"aXY2Nzg5","ct":"Y2lwaGVy"}""";
        E2eeRules.ValidateBackup(blob).Should().BeNull();
        E2eeRules.ValidateBackup("""{"v":"1","d":"private","salt":"c2FsdA","iv":"aXY2Nzg5","ct":"Y2lwaGVy"}""")
            .Should().Be(E2eeErrors.BackupInvalid);
    }

    private static string Envelope(int epoch, Guid device) =>
        $$"""{"v":1,"epoch":{{epoch}},"recipients":[{"kid":"{{device}}","enc":"AQIDBA","ct":"BAUGBw"}]}""";
}
