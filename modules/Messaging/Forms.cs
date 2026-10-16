using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>
/// Structured form, immutable published schema and approval (B-124).
/// Answers are allowlisted. Hidden fields are never stored.
/// </summary>
public sealed class FormDefinition
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public ChannelId ChannelId { get; set; }
    public string Title { get; set; } = string.Empty;
    public FormStatus Status { get; set; }
    public int PublishedVersion { get; set; }
    public string DraftSchemaJson { get; set; } = "{}";
    public UserId CreatedBy { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FormSchemaVersion
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid FormId { get; set; }
    public int Version { get; set; }
    public string SchemaJson { get; set; } = "{}";
    public UserId PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class FormSubmission
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid FormId { get; set; }
    public int SchemaVersion { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public bool AnswersPurged { get; set; }
    public FormSubmissionState State { get; set; }
    public UserId SubmittedBy { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string TitleSnapshot { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? RetainUntil { get; set; }
    public Guid? ResultMessageId { get; set; }
    public Guid? ResultTaskId { get; set; }
}

public sealed class FormDecision
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid SubmissionId { get; set; }
    public int Version { get; set; }
    public UserId ActorUserId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public enum FormStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

public enum FormSubmissionState
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

public sealed class FormOptions
{
    public const string SectionName = "Forms";

    public bool Enabled { get; set; }
}

public sealed record FormField(
    string Key,
    string Type,
    string Label,
    bool Required,
    bool Hidden,
    string[] Options);

public sealed record FormSchema(
    string ContractVersion,
    FormField[] Fields,
    string ApprovalMode,
    int RequiredApprovals,
    Guid[] ReviewerUserIds,
    string OnApproved)
{
    public string ToJson()
    {
        var fields = new JsonArray();
        foreach (var field in Fields)
        {
            var options = new JsonArray();
            foreach (var option in field.Options)
            {
                options.Add(option);
            }

            fields.Add(new JsonObject
            {
                ["key"] = field.Key,
                ["type"] = field.Type,
                ["label"] = field.Label,
                ["required"] = field.Required,
                ["hidden"] = field.Hidden,
                ["options"] = options,
            });
        }

        var reviewers = new JsonArray();
        foreach (var reviewer in ReviewerUserIds)
        {
            reviewers.Add(reviewer.ToString("D"));
        }

        return new JsonObject
        {
            ["contractVersion"] = ContractVersion,
            ["fields"] = fields,
            ["approval"] = new JsonObject
            {
                ["mode"] = ApprovalMode,
                ["required"] = RequiredApprovals,
                ["reviewerUserIds"] = reviewers,
            },
            ["onApproved"] = OnApproved,
        }.ToJsonString();
    }

    public FormSchema WithoutHiddenFields() =>
        this with { Fields = Fields.Where(field => !field.Hidden).ToArray() };

    public bool IsReviewer(Guid userId) => ReviewerUserIds.Contains(userId);
}

public static class FormPolicies
{
    public const string ContractVersion = "form.schema.v1";
    public const string Published = "form.published";
    public const string Submitted = "form.submitted";
    public const string Approved = "form.approved";
    public const string Rejected = "form.rejected";
    public const string AnswersPurged = "form.answers_purged";
    public const string Exported = "form.exported";
    public const string ModeSingle = "single";
    public const string ModeQuorum = "n_of_m";
    public const string DecisionApprove = "approve";
    public const string DecisionReject = "reject";
    public const string ResultNone = "none";
    public const string ResultMessage = "message";
    public const string ResultTask = "task";
    public const string ResultMethod = "form";
    public const int MaxTitleLength = 120;
    public const int MaxFields = 20;
    public const int MaxKeyLength = 40;
    public const int MaxLabelLength = 80;
    public const int MaxOptions = 12;
    public const int MaxOptionLength = 40;
    public const int MaxReviewers = 12;
    public const int MaxTextLength = 500;
    public const int MaxIdempotencyKeyLength = 80;
    public const int MaxFormsPerChannel = 30;
    public const int MaxPageSize = 50;
    public const int DefaultRetentionDays = 90;
    public const decimal MaxAbsNumber = 1_000_000_000_000m;

    private static readonly HashSet<string> TopLevel = ["contractVersion", "fields", "approval", "onApproved"];
    private static readonly HashSet<string> FieldNames = ["key", "type", "label", "required", "hidden", "options"];
    private static readonly HashSet<string> ApprovalNames = ["mode", "required", "reviewerUserIds"];
    private static readonly HashSet<string> FieldTypes = ["text", "number", "choice", "date", "user", "attachment"];

    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    public static bool IsValidTitle(string title) => title.Length is >= 1 and <= MaxTitleLength;

    public static bool IsValidIdempotencyKey(string key) => key.Length is >= 1 and <= MaxIdempotencyKeyLength;

    public static string StatusToken(FormStatus status) => status switch
    {
        FormStatus.Draft => "draft",
        FormStatus.Published => "published",
        FormStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string StateToken(FormSubmissionState state) => state switch
    {
        FormSubmissionState.Pending => "pending",
        FormSubmissionState.Approved => "approved",
        FormSubmissionState.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static bool TryParseDecision(string? token, out string decision)
    {
        decision = token?.Trim().ToLowerInvariant() ?? string.Empty;
        return decision is DecisionApprove or DecisionReject;
    }

    public static bool TryReadSchema(string json, out FormSchema schema)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryParseSchema(document.RootElement, out schema, out _);
        }
        catch (JsonException)
        {
            schema = new FormSchema(ContractVersion, [], ModeSingle, 1, [], ResultNone);
            return false;
        }
    }

    public static bool TryParseSchema(JsonElement element, out FormSchema schema, out string error)
    {
        schema = Empty();
        error = "InvalidFormSchema";
        if (element.ValueKind != JsonValueKind.Object || HasUnknown(element, TopLevel))
        {
            return false;
        }

        if (!TryString(element, "contractVersion", out var version) || version != ContractVersion)
        {
            return false;
        }

        if (!element.TryGetProperty("fields", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (fieldsElement.GetArrayLength() is < 1 or > MaxFields)
        {
            return false;
        }

        var fields = new List<FormField>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fieldsElement.EnumerateArray())
        {
            if (!TryField(field, out var parsed))
            {
                return false;
            }

            if (!keys.Add(parsed.Key))
            {
                return false;
            }

            fields.Add(parsed);
        }

        if (!element.TryGetProperty("approval", out var approval) || approval.ValueKind != JsonValueKind.Object || HasUnknown(approval, ApprovalNames))
        {
            return false;
        }

        if (!TryString(approval, "mode", out var mode) || mode is not (ModeSingle or ModeQuorum))
        {
            return false;
        }

        if (!approval.TryGetProperty("reviewerUserIds", out var reviewersElement) || reviewersElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var reviewers = new List<Guid>();
        foreach (var reviewer in reviewersElement.EnumerateArray())
        {
            if (reviewer.ValueKind != JsonValueKind.String || !Guid.TryParse(reviewer.GetString(), out var id) || id == Guid.Empty)
            {
                return false;
            }

            if (!reviewers.Contains(id))
            {
                reviewers.Add(id);
            }
        }

        reviewers.Sort();
        if (reviewers.Count is < 1 or > MaxReviewers)
        {
            return false;
        }

        var required = 1;
        if (approval.TryGetProperty("required", out var requiredElement))
        {
            if (requiredElement.ValueKind != JsonValueKind.Number || !requiredElement.TryGetInt32(out required))
            {
                return false;
            }
        }

        if (mode == ModeSingle)
        {
            required = 1;
        }
        else if (required < 1 || required > reviewers.Count)
        {
            return false;
        }

        if (!TryString(element, "onApproved", out var onApproved) || onApproved is not (ResultNone or ResultMessage or ResultTask))
        {
            return false;
        }

        schema = new FormSchema(ContractVersion, fields.ToArray(), mode, required, reviewers.ToArray(), onApproved);
        error = string.Empty;
        return true;
    }

    public static bool TryBindAnswers(FormSchema schema, JsonElement answers, out string canonical, out string error)
    {
        canonical = "{}";
        error = "InvalidAnswer";
        if (answers.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var visible = schema.Fields.Where(field => !field.Hidden).ToDictionary(field => field.Key, StringComparer.Ordinal);
        var hidden = schema.Fields.Where(field => field.Hidden).Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        var bound = new JsonObject();
        foreach (var property in answers.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (hidden.Contains(property.Name))
            {
                error = "HiddenField";
                return false;
            }

            if (!visible.TryGetValue(property.Name, out var field))
            {
                error = "UnknownField";
                return false;
            }

            if (!TryCanonicalValue(field, property.Value, out var node))
            {
                return false;
            }

            bound[property.Name] = node;
        }

        foreach (var field in visible.Values.Where(field => field.Required))
        {
            if (!bound.ContainsKey(field.Key))
            {
                return false;
            }
        }

        canonical = bound.ToJsonString();
        error = string.Empty;
        return true;
    }

    public static FormSubmissionState NextState(FormSchema schema, IReadOnlyList<(Guid Actor, string Decision)> decisions)
    {
        var approves = decisions.Count(item => item.Decision == DecisionApprove);
        var rejects = decisions.Count(item => item.Decision == DecisionReject);
        if (schema.ApprovalMode == ModeSingle)
        {
            if (approves > 0)
            {
                return FormSubmissionState.Approved;
            }

            return rejects > 0 ? FormSubmissionState.Rejected : FormSubmissionState.Pending;
        }

        if (approves >= schema.RequiredApprovals)
        {
            return FormSubmissionState.Approved;
        }

        var remaining = schema.ReviewerUserIds.Length - decisions.Count;
        return approves + remaining < schema.RequiredApprovals
            ? FormSubmissionState.Rejected
            : FormSubmissionState.Pending;
    }

    public static DateTimeOffset RetainUntil(DateTimeOffset submittedAt, int? retentionDays)
    {
        var days = retentionDays is null or <= 0 ? DefaultRetentionDays : retentionDays.Value;
        return submittedAt.AddDays(Math.Clamp(days, 1, 3650));
    }

    public static string MessageIdempotencyKey(Guid submissionId) => $"form-msg:{submissionId:N}";

    public static string TaskIdempotencyKey(Guid submissionId) => $"form-task:{submissionId:N}";

    private static FormSchema Empty() =>
        new(ContractVersion, [], ModeSingle, 1, [], ResultNone);

    private static bool TryField(JsonElement field, out FormField parsed)
    {
        parsed = new FormField(string.Empty, "text", string.Empty, false, false, []);
        if (field.ValueKind != JsonValueKind.Object || HasUnknown(field, FieldNames))
        {
            return false;
        }

        if (!TryString(field, "key", out var key) || !IsKey(key) || !TryString(field, "type", out var type) || !FieldTypes.Contains(type))
        {
            return false;
        }

        if (!TryString(field, "label", out var label) || label.Length is < 1 or > MaxLabelLength)
        {
            return false;
        }

        var required = false;
        if (field.TryGetProperty("required", out var requiredElement))
        {
            if (requiredElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            required = requiredElement.GetBoolean();
        }

        var hidden = false;
        if (field.TryGetProperty("hidden", out var hiddenElement))
        {
            if (hiddenElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            hidden = hiddenElement.GetBoolean();
        }

        if (hidden && required)
        {
            return false;
        }

        var options = Array.Empty<string>();
        if (field.TryGetProperty("options", out var optionsElement))
        {
            if (optionsElement.ValueKind != JsonValueKind.Array || !TryOptions(optionsElement, out options))
            {
                return false;
            }
        }

        if (type == "choice" && options.Length is < 1 or > MaxOptions)
        {
            return false;
        }

        if (type != "choice" && options.Length > 0)
        {
            return false;
        }

        parsed = new FormField(key, type, label, required, hidden, options);
        return true;
    }

    private static bool TryOptions(JsonElement element, out string[] options)
    {
        options = [];
        if (element.GetArrayLength() > MaxOptions)
        {
            return false;
        }

        var list = new List<string>();
        foreach (var option in element.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = Normalize(option.GetString());
            if (text.Length is < 1 or > MaxOptionLength || list.Contains(text, StringComparer.Ordinal))
            {
                return false;
            }

            list.Add(text);
        }

        options = list.ToArray();
        return true;
    }

    private static bool TryCanonicalValue(FormField field, JsonElement value, out JsonNode? node)
    {
        node = null;
        switch (field.Type)
        {
            case "text":
                if (value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var text = Normalize(value.GetString());
                if (text.Length is < 1 or > MaxTextLength)
                {
                    return false;
                }

                node = text;
                return true;
            case "number":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number) || Math.Abs(number) > MaxAbsNumber)
                {
                    return false;
                }

                node = JsonValue.Create(number);
                return true;
            case "choice":
                if (value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var choice = Normalize(value.GetString());
                if (!field.Options.Contains(choice, StringComparer.Ordinal))
                {
                    return false;
                }

                node = choice;
                return true;
            case "date":
                if (value.ValueKind != JsonValueKind.String
                    || !DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return false;
                }

                node = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            case "user":
            case "attachment":
                if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var id) || id == Guid.Empty)
                {
                    return false;
                }

                node = id.ToString("D");
                return true;
            default:
                return false;
        }
    }

    private static bool IsKey(string key) =>
        key.Length is >= 1 and <= MaxKeyLength
        && key[0] is >= 'a' and <= 'z'
        && key.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = Normalize(property.GetString());
        return true;
    }

    private static bool HasUnknown(JsonElement element, HashSet<string> allowed)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                return true;
            }
        }

        return false;
    }
}
