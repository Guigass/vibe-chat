using System.Text.RegularExpressions;
using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>
/// Server-authoritative knowledge page (B-120). Markdown is restricted;
/// collaboration stays request/response until B-152.
/// </summary>
public sealed class KnowledgeCollection
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public KnowledgeScopeKind ScopeKind { get; set; }
    public Guid ScopeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public UserId CreatedBy { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class KnowledgePage
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid CollectionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int Version { get; set; }
    public UserId CreatedBy { get; set; }
    public UserId UpdatedBy { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class KnowledgePageVersion
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid PageId { get; set; }
    public int Version { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public UserId ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int? RestoredFromVersion { get; set; }
    public DateTimeOffset At { get; set; }
}

public enum KnowledgeScopeKind
{
    Workspace = 0,
    Space = 1,
    Channel = 2,
}

public sealed class KnowledgePageOptions
{
    public const string SectionName = "Knowledge:Pages";

    public bool Enabled { get; set; }
}

public readonly record struct KnowledgeReferenceToken(string Kind, Guid TargetId);

public static class KnowledgePolicies
{
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 16_000;
    public const int MaxSlugLength = 80;
    public const int MaxIdempotencyKeyLength = 80;
    public const int MaxQueryLength = 80;
    public const int MinQueryLength = 2;
    public const int MaxCollectionsPerWorkspace = 100;
    public const int MaxPagesPerCollection = 200;
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 50;
    public const string ChangedEvent = "knowledge.page.changed";

    public const string ActionCreated = "created";
    public const string ActionUpdated = "updated";
    public const string ActionRestored = "restored";
    public const string ActionDeleted = "deleted";

    public const string RefMessage = "message";
    public const string RefDecision = "decision";
    public const string RefTask = "task";
    public const string RefAttachment = "attachment";
    public const string RefVisible = "visible";
    public const string RefRedacted = "redacted";

    private static readonly Regex HtmlTag = new(@"<\s*/?\s*[A-Za-z]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnsafeScheme = new(@"\b(?:javascript|data|vbscript)\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ReferenceToken = new(
        @"\[\[(message|decision|task|attachment):([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\]\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    public static bool IsValidTitle(string title) =>
        title.Length is >= 1 and <= MaxTitleLength && title.All(ch => !char.IsControl(ch));

    public static bool IsValidIdempotencyKey(string key) =>
        key.Length is >= 1 and <= MaxIdempotencyKeyLength && key.All(ch => !char.IsControl(ch));

    public static bool IsSafeBody(string body) =>
        body.Length <= MaxBodyLength
        && body.All(ch => ch is not '\0')
        && !HtmlTag.IsMatch(body)
        && !UnsafeScheme.IsMatch(body);

    public static bool TryParseScope(string? token, out KnowledgeScopeKind scope)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "workspace":
                scope = KnowledgeScopeKind.Workspace;
                return true;
            case "space":
                scope = KnowledgeScopeKind.Space;
                return true;
            case "channel":
                scope = KnowledgeScopeKind.Channel;
                return true;
            default:
                scope = default;
                return false;
        }
    }

    public static string ScopeToken(KnowledgeScopeKind scope) => scope switch
    {
        KnowledgeScopeKind.Workspace => "workspace",
        KnowledgeScopeKind.Space => "space",
        KnowledgeScopeKind.Channel => "channel",
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    public static string Slugify(string title)
    {
        var chars = new List<char>(Math.Min(title.Length, MaxSlugLength));
        var pendingHyphen = false;
        foreach (var ch in title.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingHyphen && chars.Count > 0)
                {
                    chars.Add('-');
                }

                pendingHyphen = false;
                chars.Add(ch);
            }
            else
            {
                pendingHyphen = true;
            }

            if (chars.Count >= MaxSlugLength)
            {
                break;
            }
        }

        var slug = new string(chars.ToArray()).Trim('-');
        return slug.Length == 0 ? "page" : slug;
    }

    public static IReadOnlyList<KnowledgeReferenceToken> ParseReferences(string body)
    {
        var seen = new HashSet<(string Kind, Guid Id)>();
        var tokens = new List<KnowledgeReferenceToken>();
        foreach (Match match in ReferenceToken.Matches(body))
        {
            var kind = match.Groups[1].Value.ToLowerInvariant();
            var id = Guid.Parse(match.Groups[2].Value);
            if (seen.Add((kind, id)))
            {
                tokens.Add(new KnowledgeReferenceToken(kind, id));
            }
        }

        return tokens;
    }

    public static bool IsKnowledgeEvent(string? type) =>
        string.Equals(type, ChangedEvent, StringComparison.Ordinal);
}
