using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VibeChat.SharedKernel;

namespace VibeChat.AI;

public enum BotKnowledgeKind
{
    File,
    Page,
    Url
}

public enum BotKnowledgeStatus
{
    Queued,
    Scanning,
    Parsing,
    Embedding,
    Ready,
    Failed,
    Stale,
    Deleting,
    Paused
}

/// <summary>Governed knowledge source for a workspace bot (B-157). PostgreSQL is the source of truth.</summary>
public sealed class BotKnowledgeSource
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public BotKnowledgeKind Kind { get; set; }
    public BotKnowledgeStatus Status { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Classification { get; set; } = "internal";
    public int Version { get; set; } = 1;
    public int ChunkCount { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public string EmbeddingProfile { get; set; } = BotKnowledgePolicies.EmbeddingProfile;
    public string ProjectorVersion { get; set; } = BotKnowledgePolicies.ProjectorVersion;
    public string? LastError { get; set; }
    public string Locator { get; set; } = string.Empty;
    public Guid? FileId { get; set; }
    public Guid? PageId { get; set; }
    public UserId OwnerUserId { get; set; }
    public string? PurgeEvidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class BotKnowledgeSourceVersion
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid SourceId { get; set; }
    public int VersionNumber { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string EmbeddingProfile { get; set; } = BotKnowledgePolicies.EmbeddingProfile;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BotKnowledgeChunk
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid SourceId { get; set; }
    public int VersionNumber { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string Embedding { get; set; } = string.Empty;
    public string EmbeddingProfile { get; set; } = BotKnowledgePolicies.EmbeddingProfile;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class BotKnowledgeIngestionJob
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid SourceId { get; set; }
    public int VersionNumber { get; set; }
    public BotKnowledgeStatus Status { get; set; }
    public int Attempt { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class BotKnowledgeGrant
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid BotId { get; set; }
    public Guid SourceId { get; set; }
    public UserId GrantedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class BotKnowledgeOptions
{
    public const string SectionName = "Features:BotKnowledge";

    public bool Enabled { get; set; }
    public int MaxSourcesPerWorkspace { get; set; } = BotKnowledgePolicies.MaxSourcesPerWorkspace;
    public string UrlAllowlist { get; set; } = string.Empty;
    public int MaxUrlBytes { get; set; } = BotKnowledgePolicies.MaxUrlBytes;
    public int FetchTimeoutSeconds { get; set; } = BotKnowledgePolicies.FetchTimeoutSeconds;
}

public static class BotKnowledgeErrors
{
    public const string Disabled = "BotKnowledgeDisabled";
    public const string NotFound = "KnowledgeSourceNotFound";
    public const string PageNotFound = "KnowledgePageNotFound";
    public const string FileNotFound = "KnowledgeFileNotFound";
    public const string BotNotFound = "KnowledgeBotNotFound";
    public const string GrantNotFound = "KnowledgeGrantNotFound";
    public const string InvalidSource = "InvalidKnowledgeSource";
    public const string InvalidQuery = "InvalidKnowledgeQuery";
    public const string QuotaExceeded = "KnowledgeQuotaExceeded";
    public const string IdempotencyRequired = "IdempotencyKeyRequired";
    public const string IdempotencyConflict = "IdempotencyConflict";
    public const string E2eeNotIndexed = "E2eeNotIndexed";
    public const string MalwareQuarantined = "MalwareQuarantined";
    public const string EgressBlocked = "EgressBlocked";
    public const string DnsRebinding = "DnsRebinding";
    public const string FetchFailed = "KnowledgeFetchFailed";
    public const string SourceTooLarge = "KnowledgeSourceTooLarge";
    public const string UnsupportedType = "UnsupportedKnowledgeType";
    public const string QdrantDeferred = "QdrantDeferred";
    public const string EvidenceCited = "cited";
    public const string EvidenceInsufficient = "insufficient";
}

public static class BotKnowledgePolicies
{
    public const string EmbeddingProfile = "hash-v1";
    public const string ProjectorVersion = "1";
    public const string CollectionName = "vibechat-bot-knowledge";
    public const string E2eeContentType = "application/vnd.vibechat.e2ee";
    public const int MaxSourcesPerWorkspace = 100;
    public const int MaxNameLength = 120;
    public const int MaxTextLength = 32_000;
    public const int MaxUrlLength = 2000;
    public const int MaxUrlBytes = 512_000;
    public const int FetchTimeoutSeconds = 5;
    public const int MaxRedirects = 3;
    public const int MaxCitations = 4;
    public const int ExcerptLength = 240;
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 400;
    public const double MinScore = 0.34;

    public static readonly string[] Classifications = ["public", "internal", "confidential", "restricted"];

    public static bool TryAccept(string? name, string? classification, string? kind, out BotKnowledgeKind parsedKind, out string? error)
    {
        parsedKind = BotKnowledgeKind.File;
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < 1 or > MaxNameLength)
        {
            error = BotKnowledgeErrors.InvalidSource;
            return false;
        }

        if (!Classifications.Contains((classification ?? string.Empty).Trim(), StringComparer.Ordinal))
        {
            error = BotKnowledgeErrors.InvalidSource;
            return false;
        }

        if (!Enum.TryParse<BotKnowledgeKind>((kind ?? string.Empty).Trim(), ignoreCase: true, out parsedKind))
        {
            error = BotKnowledgeErrors.InvalidSource;
            return false;
        }

        error = null;
        return true;
    }

    public static string? RejectContentType(string? contentType)
    {
        var value = (contentType ?? string.Empty).Trim();
        var separator = value.IndexOf(';');
        var media = (separator < 0 ? value : value[..separator]).Trim();
        return media.Equals(E2eeContentType, StringComparison.OrdinalIgnoreCase)
            ? BotKnowledgeErrors.E2eeNotIndexed
            : null;
    }

    public static bool Readable(BotKnowledgeSource source) =>
        source.DeletedAt is null
        && source.Status == BotKnowledgeStatus.Ready
        && source.EmbeddingProfile == EmbeddingProfile
        && source.ProjectorVersion == ProjectorVersion;

    public static string Checksum(IReadOnlyList<string> chunkHashes)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', chunkHashes)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Sha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Excerpt(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length <= ExcerptLength ? value : value[..ExcerptLength];
    }

    public static IReadOnlyList<T> Citations<T>(IEnumerable<(T Item, double Score, bool Allowed)> rows)
    {
        return rows
            .Where(row => row.Allowed && row.Score >= MinScore)
            .OrderByDescending(row => row.Score)
            .Take(MaxCitations)
            .Select(row => row.Item)
            .ToArray();
    }

    public static string EventDocument(
        Guid sourceId,
        Guid workspaceId,
        Guid tenantId,
        int version,
        Guid actorId,
        string contentSha256,
        string kind,
        string status)
    {
        return new JsonObject
        {
            ["sourceId"] = sourceId.ToString("D"),
            ["workspaceId"] = workspaceId.ToString("D"),
            ["tenantId"] = tenantId.ToString("D"),
            ["version"] = version,
            ["actorId"] = actorId.ToString("D"),
            ["contentSha256"] = contentSha256,
            ["kind"] = kind,
            ["status"] = status
        }.ToJsonString();
    }

    public static IReadOnlyList<string> Allowlist(string? configured) =>
        (configured ?? string.Empty)
            .Split([',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.TrimEnd('.').ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
