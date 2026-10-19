using System.Text.Json.Nodes;
using FluentAssertions;
using VibeChat.Integrations;

namespace VibeChat.UnitTests;

public sealed class PublicEventSchemaTests
{
    [Fact]
    public void Examples_match_their_schemas_and_v1_coexists_with_v2()
    {
        foreach (var version in PublicEventCatalog.Versions)
        {
            var decision = PublicEventCatalog.Validate(
                version.EventType,
                version.SchemaVersion,
                version.Example.ToJsonString());
            decision.Accepted.Should().BeTrue($"{version.EventType} v{version.SchemaVersion} example: {decision.Reason}");
        }

        var created = PublicEventCatalog.Versions
            .Where(version => version.EventType == PublicEventCatalog.MessageCreated)
            .Select(version => version.SchemaVersion)
            .ToArray();
        created.Should().Equal(1, 2);
        PublicEventCatalog.Versions.Single(version =>
                version.EventType == PublicEventCatalog.MessageCreated && version.SchemaVersion == 1)
            .DeprecatedUntil.Should().Be(PublicEventCatalog.MessageCreatedV1RetiresOn);
    }

    [Fact]
    public void Current_webhook_body_is_v1_and_projects_to_v2()
    {
        const string body = """
            {
              "tenantId": "11111111-1111-1111-1111-111111111111",
              "channelId": "22222222-2222-2222-2222-222222222222",
              "conversationId": "22222222-2222-2222-2222-222222222222",
              "threadId": null,
              "messageId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "clientMessageId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "announcement": null,
              "authorId": "33333333-3333-3333-3333-333333333333",
              "authorName": "Demo",
              "authorIsBot": false,
              "sequence": 4,
              "body": "hello",
              "createdAt": "2026-10-19T12:00:00.0000000+00:00",
              "mentionedUserIds": [],
              "attachments": []
            }
            """;

        var accepted = PublicEventCatalog.Accept(WebhookEventTypes.MessageCreated, body);
        accepted.Accepted.Should().BeTrue(accepted.Reason);
        accepted.SchemaVersion.Should().Be(1);
        accepted.EventType.Should().Be(PublicEventCatalog.MessageCreated);

        var projected = PublicEventCatalog.Project(
            PublicEventCatalog.MessageCreated,
            1,
            2,
            body,
            Guid.Parse("00000000-0000-0000-0000-000000000005"),
            Guid.Parse("00000000-0000-0000-0000-000000000006"),
            new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        projected.Should().NotBeNull();
        var v2 = PublicEventCatalog.Validate(PublicEventCatalog.MessageCreated, 2, projected);
        v2.Accepted.Should().BeTrue(v2.Reason);

        var catalogExample = PublicEventCatalog.Versions.Single(version =>
            version.EventType == PublicEventCatalog.MessageCreated && version.SchemaVersion == 2).Example;
        var fromExample = PublicEventCatalog.Project(
            PublicEventCatalog.MessageCreated,
            1,
            2,
            PublicEventCatalog.Versions.Single(version =>
                version.EventType == PublicEventCatalog.MessageCreated && version.SchemaVersion == 1).Example.ToJsonString(),
            Guid.Parse("00000000-0000-0000-0000-000000000005"),
            Guid.Parse("00000000-0000-0000-0000-000000000006"),
            new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        JsonNode.DeepEquals(JsonNode.Parse(fromExample!), catalogExample).Should().BeTrue();
    }

    [Fact]
    public void Unknown_version_and_type_fail_observably()
    {
        var unknownVersion = PublicEventCatalog.Accept(
            WebhookEventTypes.MessageCreated,
            """{"schemaVersion":99,"tenantId":"00000000-0000-0000-0000-000000000001"}""");
        unknownVersion.Accepted.Should().BeFalse();
        unknownVersion.Reason.Should().Be(EventSchemaReasons.UnknownSchemaVersion);
        unknownVersion.EventType.Should().Be(PublicEventCatalog.MessageCreated);

        var unknownType = PublicEventCatalog.Accept("NotAPublicEvent", "{}");
        unknownType.Reason.Should().Be(EventSchemaReasons.UnknownEventType);
        unknownType.EventType.Should().Be("unknown");

        var mismatch = PublicEventCatalog.Validate(PublicEventCatalog.MessageCreated, 1, """{"tenantId":"nope"}""");
        mismatch.Reason.Should().Be(EventSchemaReasons.SchemaMismatch);
    }

    [Fact]
    public void Baseline_has_no_breaking_change_and_mutations_are_detected()
    {
        var current = PublicEventCatalog.ExportJson();
        var baseline = File.ReadAllText(RepoFile("modules/Integrations/public-event-schema-baseline.json"));
        EventSchemaCompatibility.BreakingChanges(baseline, current, PublicEventCatalog.IntroducedOn)
            .Should().BeEmpty();

        var document = JsonNode.Parse(current)!.AsObject();
        var created = document["versions"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["schemaVersion"]!.GetValue<int>() == 1
                && item["eventType"]!.GetValue<string>() == PublicEventCatalog.MessageCreated);
        created["schema"]!["properties"]!.AsObject().Remove("tenantId");
        EventSchemaCompatibility.BreakingChanges(baseline, document.ToJsonString(), PublicEventCatalog.IntroducedOn)
            .Should().NotBeEmpty();

        var removed = JsonNode.Parse(current)!.AsObject();
        var versions = removed["versions"]!.AsArray();
        versions.RemoveAt(0);
        EventSchemaCompatibility.BreakingChanges(baseline, removed.ToJsonString(), PublicEventCatalog.IntroducedOn)
            .Should().Contain(item => item.Contains("removed", StringComparison.Ordinal));
        EventSchemaCompatibility.BreakingChanges(
                baseline,
                removed.ToJsonString(),
                PublicEventCatalog.MessageCreatedV1RetiresOn)
            .Should().NotContain(item => item.Contains("messaging.message.created@1", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_is_generated_from_the_catalog()
    {
        var committed = File.ReadAllText(RepoFile("docs/architecture/eventos-publicos.md"));
        committed.Should().Be(PublicEventCatalog.RenderMarkdown());
    }

    private static string RepoFile(string relativePath)
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
