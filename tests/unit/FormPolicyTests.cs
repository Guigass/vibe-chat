using System.Text.Json;
using FluentAssertions;
using VibeChat.BuildingBlocks;
using VibeChat.Messaging;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class FormPolicyTests
{
    [Fact]
    public void Production_forms_flag_defaults_off()
    {
        var json = File.ReadAllText(FindRepoFile(Path.Combine("apps", "api", "appsettings.json")));
        var forms = json.IndexOf("\"Forms\"", StringComparison.Ordinal);
        var next = json.IndexOf("\"Notifications\"", forms, StringComparison.Ordinal);
        var slice = json[forms..next];
        slice.Should().Contain("\"Enabled\": false");
        slice.Should().NotContain("\"Enabled\": true");
    }

    [Fact]
    public void Schema_rejects_unknown_properties_hidden_required_and_bad_types()
    {
        Parse(ValidSchema()).error.Should().BeEmpty();
        Parse(ValidSchema(extra: "\"note\":\"x\"")).error.Should().Be("InvalidFormSchema");
        Parse(ValidSchema(fieldExtra: "\"hidden\":true,\"required\":true")).error.Should().Be("InvalidFormSchema");
        Parse(ValidSchema(type: "script")).error.Should().Be("InvalidFormSchema");
    }

    [Fact]
    public void Hidden_and_unknown_answers_are_rejected_and_visible_answers_round_trip()
    {
        var schema = Read(ValidSchema());
        var hidden = JsonDocument.Parse("""{"internal":"secret","reason":"ok"}""").RootElement;
        FormPolicies.TryBindAnswers(schema, hidden, out _, out var hiddenError).Should().BeFalse();
        hiddenError.Should().Be("HiddenField");

        var unknown = JsonDocument.Parse("""{"reason":"ok","extra":1}""").RootElement;
        FormPolicies.TryBindAnswers(schema, unknown, out _, out var unknownError).Should().BeFalse();
        unknownError.Should().Be("UnknownField");

        var visible = schema.WithoutHiddenFields();
        FormPolicies.TryBindAnswers(visible, JsonDocument.Parse("""{"reason":"  ok  "}""").RootElement, out var canonical, out var error)
            .Should().BeTrue();
        error.Should().BeEmpty();
        canonical.Should().Be("""{"reason":"ok"}""");
        visible.Fields.Should().NotContain(field => field.Hidden);
    }

    [Fact]
    public void Published_schema_is_stable_and_quorum_does_not_close_early()
    {
        var schema = Read(ValidSchema());
        var again = Read(schema.ToJson());
        again.ToJson().Should().Be(schema.ToJson());

        var single = FormPolicies.NextState(schema, [(Guid.NewGuid(), FormPolicies.DecisionApprove)]);
        single.Should().Be(FormSubmissionState.Approved);

        var quorum = schema with
        {
            ApprovalMode = FormPolicies.ModeQuorum,
            RequiredApprovals = 2,
            ReviewerUserIds = [Guid.NewGuid(), Guid.NewGuid()],
        };
        FormPolicies.NextState(quorum, [(Guid.NewGuid(), FormPolicies.DecisionApprove)])
            .Should().Be(FormSubmissionState.Pending);
        FormPolicies.NextState(quorum, [(Guid.NewGuid(), FormPolicies.DecisionApprove), (Guid.NewGuid(), FormPolicies.DecisionApprove)])
            .Should().Be(FormSubmissionState.Approved);
        FormPolicies.NextState(quorum, [(Guid.NewGuid(), FormPolicies.DecisionReject), (Guid.NewGuid(), FormPolicies.DecisionReject)])
            .Should().Be(FormSubmissionState.Rejected);
    }

    [Fact]
    public void Member_can_create_and_guest_cannot()
    {
        RolePermissionCatalog.For(Role.Member).Should().Contain(Permissions.Forms.Submit);
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Forms.Create);
        RolePermissionCatalog.For(Role.Auditor).Should().Contain(Permissions.Forms.Review);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Forms.Publish);
    }

    private static (FormSchema schema, string error) Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        FormPolicies.TryParseSchema(document.RootElement, out var schema, out var error);
        return (schema, error);
    }

    private static FormSchema Read(string json)
    {
        var parsed = Parse(json);
        parsed.error.Should().BeEmpty();
        return parsed.schema;
    }

    private static string ValidSchema(string extra = "", string fieldExtra = "", string type = "text")
    {
        var fieldTail = string.IsNullOrEmpty(fieldExtra) ? string.Empty : "," + fieldExtra;
        var topTail = string.IsNullOrEmpty(extra) ? string.Empty : "," + extra;
        return $$"""
            {
              "contractVersion": "form.schema.v1",
              "fields": [{ "key": "reason", "type": "{{type}}", "label": "Motivo" {{fieldTail}} }, { "key": "internal", "type": "text", "label": "Interno", "hidden": true }],
              "approval": { "mode": "single", "required": 1, "reviewerUserIds": ["55555555-5555-5555-5555-555555555555"] },
              "onApproved": "none"
              {{topTail}}
            }
            """;
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

        throw new FileNotFoundException(relativePath);
    }
}
