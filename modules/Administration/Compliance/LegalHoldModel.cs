using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>B-129. Off by default; Development and the test host opt in.</summary>
public sealed class LegalHoldOptions
{
    public const string SectionName = "Features:LegalHold";

    public bool Enabled { get; set; }
}

public static class LegalHoldLimits
{
    public const int MinReasonLength = 8;
    public const int MaxReasonLength = 500;
    public const int MaxTitleLength = 120;
    public const int MaxTextLength = 200;
    public const int MaxItems = 200;
    public const int MaxIdempotencyKeyLength = 200;
    public const string ExportFormat = "vibechat.ediscovery.export.v1";
}

public static class LegalHoldStatus
{
    public const string Requested = "requested";
    public const string Active = "active";
    public const string ReleaseRequested = "release_requested";
    public const string Released = "released";
}

public static class LegalHoldScope
{
    public const string Workspace = "workspace";
    public const string User = "user";
    public const string Channel = "channel";
    public const string Conversation = "conversation";
    public const string Interval = "interval";

    public static bool Known(string? scope) => scope is Workspace or User or Channel or Conversation or Interval;
}

public static class ComplianceCapabilities
{
    public const string Hold = "hold";
    public const string Discovery = "discovery";
    public const string Dlp = "dlp";
    public const string AuditExport = "audit.export";
    public const string Policy = "policy";

    public static bool Known(string? capability) => capability is Hold or Discovery or Dlp or AuditExport or Policy;
}

public static class LegalHoldErrors
{
    public const string Disabled = "LegalHoldDisabled";
    public const string SelfGrant = "ComplianceSelfGrant";
    public const string Segregation = "ComplianceSegregation";
    public const string ConfirmationRequired = "ComplianceConfirmationRequired";
    public const string ReasonRequired = "ComplianceReasonRequired";
    public const string ScopeInvalid = "ComplianceScopeInvalid";
    public const string NotFound = "ComplianceNotFound";
    public const string State = "ComplianceState";
    public const string IdempotencyRequired = "ComplianceIdempotencyRequired";
    public const string IdempotencyConflict = "ComplianceIdempotencyConflict";
    public const string ResultTooLarge = "DiscoveryResultTooLarge";
    public const string MemberRequired = "ComplianceMemberRequired";
    public const string ExpiryInvalid = "ComplianceExpiryInvalid";
    public const string CapabilityInvalid = "ComplianceCapabilityInvalid";
}

public static class CustodyActions
{
    public const string Grant = "compliance.grant";
    public const string Revoke = "compliance.revoke";
    public const string HoldRequest = "compliance.hold.request";
    public const string HoldApprove = "compliance.hold.approve";
    public const string ReleaseRequest = "compliance.hold.release.request";
    public const string ReleaseApprove = "compliance.hold.release.approve";
    public const string CaseCreate = "compliance.case.create";
    public const string Export = "compliance.discovery.export";
}

/// <summary>Message coordinates used to decide whether an active hold blocks purge.</summary>
public readonly record struct HoldSubject(
    Guid WorkspaceId,
    Guid ChannelId,
    Guid ConversationId,
    Guid AuthorId,
    DateTimeOffset CreatedAt);

public static class LegalHoldRules
{
    public static bool Confirmed(bool confirmed, bool acknowledged) => confirmed && acknowledged;

    public static bool ReasonOk(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        return trimmed.Length >= LegalHoldLimits.MinReasonLength
            && trimmed.Length <= LegalHoldLimits.MaxReasonLength;
    }

    public static bool CanApprove(Guid requestedBy, Guid actor) => requestedBy != actor;

    public static bool IsCovering(
        string status,
        DateTimeOffset? expiresAt,
        Guid holdWorkspaceId,
        string scopeKind,
        Guid? scopeId,
        DateTimeOffset? intervalStart,
        DateTimeOffset? intervalEnd,
        HoldSubject subject,
        DateTimeOffset now)
    {
        if (status != LegalHoldStatus.Active || holdWorkspaceId != subject.WorkspaceId)
        {
            return false;
        }

        if (expiresAt is not null && expiresAt <= now)
        {
            return false;
        }

        return scopeKind switch
        {
            LegalHoldScope.Workspace => true,
            LegalHoldScope.User => scopeId == subject.AuthorId,
            LegalHoldScope.Channel => scopeId == subject.ChannelId,
            LegalHoldScope.Conversation => scopeId == subject.ConversationId,
            LegalHoldScope.Interval => intervalStart is not null
                && intervalEnd is not null
                && subject.CreatedAt >= intervalStart
                && subject.CreatedAt <= intervalEnd,
            _ => true
        };
    }
}

public static class CustodyChain
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Payload(
        string action,
        Guid actor,
        Guid? holdId,
        Guid? caseId,
        Guid? exportId,
        string reason,
        DateTimeOffset occurredAt) =>
        JsonSerializer.Serialize(new
        {
            action,
            actor,
            holdId,
            caseId,
            exportId,
            reason,
            occurredAt = occurredAt.ToUniversalTime().ToString("O")
        }, Json);

    public static string Hash(string previousHash, string payload) =>
        Sha256Hex(previousHash + "\n" + payload);
}

public sealed record DiscoveryQuery(
    Guid[] Custodians,
    Guid[] ChannelIds,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Text);

public sealed record DiscoveryManifestItem(
    Guid MessageId,
    Guid AuthorId,
    Guid ConversationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt,
    string BodySha256,
    string Body);

public sealed record DiscoveryManifest(
    string Format,
    Guid CaseId,
    DateTimeOffset ExportedAt,
    DiscoveryQuery Query,
    IReadOnlyList<DiscoveryManifestItem> Items);

public sealed class ComplianceGrant
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public UserId UserId { get; set; }
    public string Capability { get; set; } = string.Empty;
    public UserId GrantedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class LegalHold
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string ScopeKind { get; set; } = string.Empty;
    public Guid? ScopeId { get; set; }
    public DateTimeOffset? IntervalStart { get; set; }
    public DateTimeOffset? IntervalEnd { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = LegalHoldStatus.Requested;
    public UserId RequestedBy { get; set; }
    public UserId? ApprovedBy { get; set; }
    public UserId? ReleaseRequestedBy { get; set; }
    public UserId? ReleaseApprovedBy { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class DiscoveryCase
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public string QueryJson { get; set; } = "{}";
    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class DiscoveryExportRecord
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid CaseId { get; set; }
    public string Status { get; set; } = "ready";
    public int Progress { get; set; } = 100;
    public string ManifestJson { get; set; } = "{}";
    public string Checksum { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class CustodyEvent
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid? HoldId { get; set; }
    public Guid? CaseId { get; set; }
    public Guid? ExportId { get; set; }
    public string Action { get; set; } = string.Empty;
    public UserId ActorUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public string PreviousHash { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ComplianceResult(int Status, string? Error, object? Body)
{
    public static ComplianceResult Ok(object body, int status = 200) => new(status, null, body);
    public static ComplianceResult Fail(int status, string error) => new(status, error, null);
}
