using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>B-153. Off by default; Development and the test host opt in.</summary>
public sealed class ImportOptions
{
    public const string SectionName = "Features:Import";

    public bool Enabled { get; set; }
}

public static class ImportLimits
{
    public const int MaxDocumentBytes = 1_500_000;
    public const int MaxPrincipals = 1_000;
    public const int MaxChannels = 100;
    public const int MaxMessages = 1_000;
    public const int MaxAttachments = 200;
    public const int MaxBodyLength = 8_000;
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;
    public const string Format = "vibechat.import.v1";
}

public static class ImportStatus
{
    public const string Validated = "validated";
    public const string Planned = "planned";
    public const string Paused = "paused";
    public const string Staged = "staged";
    public const string Published = "published";
    public const string RolledBack = "rolled_back";
}

public static class ImportCommands
{
    public const string Plan = "plan";
    public const string Execute = "execute";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Publish = "publish";
    public const string Rollback = "rollback";

    public static bool Allowed(string status, string command) => command switch
    {
        Plan => status is ImportStatus.Validated or ImportStatus.Planned,
        Execute => status == ImportStatus.Planned,
        Pause => status is ImportStatus.Planned or ImportStatus.Staged,
        Resume => status == ImportStatus.Paused,
        Publish => status == ImportStatus.Staged,
        Rollback => status is ImportStatus.Planned or ImportStatus.Paused or ImportStatus.Staged or ImportStatus.Published,
        _ => false
    };
}

public static class ImportErrors
{
    public const string Disabled = "ImportDisabled";
    public const string RoleForbidden = "ImportRoleForbidden";
    public const string SchemaUnknown = "ImportSchemaUnknown";
    public const string DocumentInvalid = "ImportDocumentInvalid";
    public const string DuplicateExternalId = "ImportDuplicateExternalId";
    public const string MappingAmbiguous = "ImportMappingAmbiguous";
    public const string MappingUnknown = "ImportMappingUnknown";
    public const string Quota = "ImportQuota";
    public const string ZipBomb = "ImportZipBomb";
    public const string IdempotencyConflict = "ImportIdempotencyConflict";
    public const string NotFound = "ImportNotFound";
    public const string State = "ImportState";
    public const string Conflict = "ImportConflict";
    public const string ConfirmRequired = "ImportConfirmRequired";
    public const string IdempotencyRequired = "IdempotencyKeyRequired";
}

public static class ImportResourceTypes
{
    public const string Principal = "principal";
    public const string Space = "space";
    public const string Channel = "channel";
    public const string Thread = "thread";
    public const string Message = "message";
    public const string Attachment = "attachment";
}

public static class ImportDispositions
{
    public const string Staged = "staged";
    public const string Published = "published";
    public const string Reused = "reused";
    public const string Quarantined = "quarantined";
    public const string Ignored = "ignored";
    public const string Historical = "historical";
}

public sealed class CanonicalImport
{
    public string Format { get; set; } = ImportLimits.Format;
    public string SourceSystem { get; set; } = "vibechat";
    public string AdapterVersion { get; set; } = "1";
    public bool TenantClaimIgnored { get; set; }
    public List<CanonicalPrincipal> Principals { get; set; } = [];
    public List<CanonicalSpace> Spaces { get; set; } = [];
    public List<CanonicalChannel> Channels { get; set; } = [];
    public List<CanonicalThread> Threads { get; set; } = [];
    public List<CanonicalMessage> Messages { get; set; } = [];
    public List<CanonicalAttachment> Attachments { get; set; } = [];
}

public sealed class CanonicalPrincipal
{
    public string ExternalId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Role { get; set; }
    public Guid? MappedUserId { get; set; }
    public bool Historical { get; set; } = true;
}

public sealed class CanonicalSpace
{
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class CanonicalChannel
{
    public string ExternalId { get; set; } = string.Empty;
    public string? SpaceExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "public";
    public bool Ignored { get; set; }
    public string? IgnoreReason { get; set; }
}

public sealed class CanonicalThread
{
    public string ExternalId { get; set; } = string.Empty;
    public string ChannelExternalId { get; set; } = string.Empty;
    public string RootMessageExternalId { get; set; } = string.Empty;
}

public sealed class CanonicalMessage
{
    public string ExternalId { get; set; } = string.Empty;
    public string ChannelExternalId { get; set; } = string.Empty;
    public string? ThreadExternalId { get; set; }
    public string AuthorExternalId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool Ignored { get; set; }
    public string? IgnoreReason { get; set; }
    public bool BodyRedactedInReport { get; set; }
}

public sealed class CanonicalAttachment
{
    public string ExternalId { get; set; } = string.Empty;
    public string MessageExternalId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long ByteLength { get; set; }
    public string? Sha256 { get; set; }
    public string? PayloadBase64 { get; set; }
    public bool Quarantined { get; set; }
    public string? QuarantineReason { get; set; }
}

/// <summary>Operator-facing summary. Never includes message bodies, emails, payloads or secrets.</summary>
public sealed class ImportReport
{
    public int Principals { get; set; }
    public int Spaces { get; set; }
    public int Channels { get; set; }
    public int Threads { get; set; }
    public int Messages { get; set; }
    public int Attachments { get; set; }
    public int Quarantined { get; set; }
    public int Ignored { get; set; }
    public int Redacted { get; set; }
    public int Failed { get; set; }
    public bool Blocking { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<string> Conflicts { get; set; } = [];
}

public sealed class ImportJobRecord
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public UserId CreatedBy { get; set; }
    public string Adapter { get; set; } = string.Empty;
    public string Status { get; set; } = ImportStatus.Validated;
    public string? PauseFrom { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string DocumentHash { get; set; } = string.Empty;
    public string CanonicalJson { get; set; } = "{}";
    public string ReportJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ImportIdMapRecord
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid ImportJobId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public Guid? CanonicalId { get; set; }
    public string Disposition { get; set; } = ImportDispositions.Staged;
}

public sealed class ImportHistoricalPrincipalRecord
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid ImportJobId { get; set; }
    public UserId UserId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
}
