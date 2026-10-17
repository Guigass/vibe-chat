using System.Text.Json;
using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>
/// Private incident room and its playbook (B-126). Timeline rows reference
/// evidence; they do not copy message bodies.
/// </summary>
public sealed class IncidentRoom
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public ChannelId ChannelId { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = IncidentPolicies.Declared;
    public UserId CommanderUserId { get; set; }
    public string? CorrelationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public UserId CreatedBy { get; set; }
    public DateTimeOffset DeclaredAt { get; set; }
    public DateTimeOffset? MitigatingAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? PostmortemJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IncidentResponder
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid IncidentId { get; set; }
    public UserId UserId { get; set; }
    public string Role { get; set; } = IncidentPolicies.ResponderRole;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IncidentPlaybookStep
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid IncidentId { get; set; }
    public int StepIndex { get; set; }
    public string Code { get; set; } = string.Empty;
    public UserId OwnerUserId { get; set; }
    public string Status { get; set; } = IncidentPolicies.StepPending;
    public string? EvidenceKind { get; set; }
    public Guid? EvidenceId { get; set; }
    public Guid? AutomationId { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class IncidentTimelineEntry
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid IncidentId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IncidentOptions
{
    public const string SectionName = "Incidents";

    public bool Enabled { get; set; }
}

public static class IncidentPolicies
{
    public const string ContractVersion = "incident.playbook.v1";
    public const string DefaultTemplate = "default";
    public const string Declared = "declared";
    public const string Mitigating = "mitigating";
    public const string Resolved = "resolved";
    public const string CommanderRole = "commander";
    public const string ResponderRole = "responder";
    public const string StepPending = "pending";
    public const string StepDone = "done";
    public const string StepFailed = "failed";
    public const string KindMessage = "message";
    public const string KindDecision = "decision";
    public const string KindTask = "task";
    public const string KindAudit = "audit";
    public const string KindAnnouncement = "announcement";
    public const string StepAcknowledge = "acknowledge";
    public const string StepMitigate = "mitigate";
    public const string StepCommunicate = "communicate";
    public const string StepPostmortem = "postmortem";
    public const string EventDeclared = "incident.declared";
    public const string EventTransitioned = "incident.transitioned";
    public const string EventResolved = "incident.resolved";
    public const string EventAutomationFailed = "incident.automation_failed";
    public const int MaxOpenPerWorkspace = 30;
    public const int MaxResponders = 8;
    public const int MaxTimeline = 40;
    public const int MaxTitleLength = 80;
    public const int MaxNoteLength = 280;
    public const int MaxIdempotencyKeyLength = 80;
    public const int MaxCorrelationLength = 80;

    public static readonly string[] Severities = ["sev1", "sev2", "sev3", "sev4"];

    public static readonly string[] EvidenceKinds = [KindMessage, KindDecision, KindTask, KindAudit];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Normalize(string? value) => (value ?? string.Empty).Trim();

    public static bool IsValidIdempotencyKey(string key) =>
        key.Length is >= 8 and <= MaxIdempotencyKeyLength
        && key.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static bool IsValidTitle(string title) =>
        title.Length is >= 1 and <= MaxTitleLength && !title.Contains('<') && !title.Contains('>');

    public static bool IsValidSeverity(string severity) =>
        Severities.Contains(severity, StringComparer.Ordinal);

    public static bool IsValidTemplate(string template) =>
        string.Equals(template, DefaultTemplate, StringComparison.Ordinal);

    public static bool IsValidCorrelation(string? correlation) =>
        correlation is null
        || (correlation.Length is >= 1 and <= MaxCorrelationLength
            && correlation.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':'));

    public static bool IsValidNote(string note) =>
        note.Length is >= 1 and <= MaxNoteLength
        && !note.Contains('<')
        && !note.Contains('>')
        && note.All(static c => !char.IsControl(c) || c is '\n' or '\t');

    public static bool IsValidEvidenceKind(string kind) =>
        EvidenceKinds.Contains(kind, StringComparer.Ordinal);

    public static bool TryTransition(string from, string to) => (from, to) switch
    {
        (Declared, Mitigating) => true,
        (Mitigating, Resolved) => true,
        _ => false,
    };

    public static string[] TemplateSteps(string template) =>
        IsValidTemplate(template)
            ? [StepAcknowledge, StepMitigate, StepCommunicate, StepPostmortem]
            : [];

    public static string ChannelName(string title, Guid incidentId)
    {
        var slug = new string(title.ToLowerInvariant().Select(static c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        if (slug.Length > 40)
        {
            slug = slug[..40].Trim('-');
        }

        if (slug.Length < 2)
        {
            slug = "incidente";
        }

        return $"{slug}-{incidentId.ToString("N")[..8]}";
    }

    public static string BuildPostmortem(
        IncidentRoom room,
        IReadOnlyList<IncidentPlaybookStep> steps,
        IReadOnlyList<IncidentTimelineEntry> timeline)
    {
        return JsonSerializer.Serialize(new
        {
            contractVersion = ContractVersion,
            incidentId = room.Id,
            channelId = room.ChannelId.Value,
            title = room.Title,
            severity = room.Severity,
            status = room.Status,
            correlationId = room.CorrelationId,
            declaredAt = room.DeclaredAt,
            mitigatingAt = room.MitigatingAt,
            resolvedAt = room.ResolvedAt,
            steps = steps
                .OrderBy(step => step.StepIndex)
                .Select(step => new
                {
                    index = step.StepIndex,
                    code = step.Code,
                    status = step.Status,
                    evidenceKind = step.EvidenceKind,
                    evidenceId = step.EvidenceId,
                    errorCode = step.ErrorCode,
                }),
            timeline = timeline
                .OrderBy(entry => entry.CreatedAt)
                .Select(entry => new
                {
                    kind = entry.Kind,
                    sourceId = entry.SourceId,
                }),
        }, JsonOptions);
    }
}
