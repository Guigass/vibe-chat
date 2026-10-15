using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Search;

public enum SemanticSourceKind
{
    Message = 0,
    Page = 1
}

/// <summary>
/// Governed chunk projection (B-121). Embeddings are rebuildable; the row is not authorization.
/// </summary>
public sealed class SemanticChunk
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public SemanticSourceKind SourceKind { get; set; }
    public Guid SourceId { get; set; }
    public int ChunkIndex { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? CollectionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string Embedding { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class SemanticSettings
{
    public WorkspaceId WorkspaceId { get; set; }
    public TenantId TenantId { get; set; }
    public bool Enabled { get; set; }
    public int MonthlyTokenBudget { get; set; } = SemanticPolicies.DefaultMonthlyTokenBudget;
    public int TokensUsed { get; set; }
    public DateTimeOffset BudgetPeriodStart { get; set; }
}

public sealed class SemanticUsage
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public UserId UserId { get; set; }
    public int Tokens { get; set; }
    public int CitationCount { get; set; }
    public int LatencyMs { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SemanticSearchOptions
{
    public const string SectionName = "SemanticSearch";

    public bool Enabled { get; set; }
    public string QdrantUrl { get; set; } = string.Empty;
    public int MonthlyTokenBudget { get; set; } = SemanticPolicies.DefaultMonthlyTokenBudget;
}

public sealed record SemanticCitation(
    Guid ChunkId,
    string SourceKind,
    Guid SourceId,
    Guid? ChannelId,
    long? Sequence,
    string Excerpt,
    double Score);

public sealed record SemanticAnswer(string Evidence, string? Text, double Confidence);

public sealed record SemanticQueryRequest(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    UserId UserId,
    string Query);

public sealed record SemanticQueryOutcome(
    string Code,
    string Evidence,
    string? Answer,
    double Confidence,
    IReadOnlyList<SemanticCitation> Citations,
    int TokensCharged);

public sealed record SemanticRebuildResult(int Messages, int Pages, int Chunks);

public interface ISemanticIndex
{
    Task SyncMessageAsync(TenantId tenantId, MessageId messageId, CancellationToken cancellationToken);
    Task SyncPageAsync(TenantId tenantId, Guid pageId, CancellationToken cancellationToken);
    Task<SemanticRebuildResult> RebuildAsync(TenantId tenantId, WorkspaceId workspaceId, CancellationToken cancellationToken);
    Task DeleteWorkspaceProjectionAsync(TenantId tenantId, WorkspaceId workspaceId, CancellationToken cancellationToken);
}

public interface ISemanticQuery
{
    Task<SemanticQueryOutcome> QueryAsync(SemanticQueryRequest request, CancellationToken cancellationToken);
}

public static class SemanticPolicies
{
    public const int Dimensions = 64;
    public const int ChunkLength = 320;
    public const int ChunkOverlap = 40;
    public const int MaxChunksPerSource = 8;
    public const int MaxCandidates = 400;
    public const int MaxCitations = 4;
    public const int ExcerptLength = 240;
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 400;
    public const int DefaultMonthlyTokenBudget = 100_000;
    public const int MaxMonthlyTokenBudget = 5_000_000;
    public const int QueriesPerMinute = 30;
    public const int BudgetPeriodDays = 30;
    public const double MinScore = 0.34;
    public const string EmbeddingProfile = "hash-v1";
    public const string CollectionName = "vibechat-semantic";
    public const string EvidenceCited = "cited";
    public const string EvidenceNone = "none";
    public const string CodeOk = "ok";
    public const string CodeDisabled = "SemanticSearchDisabled";
    public const string CodeBudget = "SemanticBudgetExceeded";
    public const string CodeRate = "SemanticRateLimited";
    public const string CodeInvalid = "InvalidSemanticQuery";
    public const string CodeUnavailable = "SemanticSearchUnavailable";

    public static string SourceToken(SemanticSourceKind kind) => kind switch
    {
        SemanticSourceKind.Message => "message",
        SemanticSourceKind.Page => "page",
        _ => "unknown"
    };

    public static int EstimateTokens(string? text)
    {
        var length = text?.Length ?? 0;
        return Math.Max(1, (length + 3) / 4);
    }
}

public static class SemanticChunker
{
    public static IReadOnlyList<string> Split(string? text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        if (normalized.Length <= SemanticPolicies.ChunkLength)
        {
            return [normalized];
        }

        var chunks = new List<string>(SemanticPolicies.MaxChunksPerSource);
        var start = 0;
        while (start < normalized.Length && chunks.Count < SemanticPolicies.MaxChunksPerSource)
        {
            var length = Math.Min(SemanticPolicies.ChunkLength, normalized.Length - start);
            var slice = normalized.Substring(start, length).Trim();
            if (slice.Length > 0)
            {
                chunks.Add(slice);
            }

            if (start + length >= normalized.Length)
            {
                break;
            }

            start += SemanticPolicies.ChunkLength - SemanticPolicies.ChunkOverlap;
        }

        return chunks;
    }
}

public static class SemanticEmbedding
{
    public static float[] Embed(string? text)
    {
        var vector = new float[SemanticPolicies.Dimensions];
        foreach (var word in Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length < 3)
            {
                vector[Bucket(word)] += 1;
                continue;
            }

            for (var i = 0; i <= word.Length - 3; i++)
            {
                vector[Bucket(word.Substring(i, 3))] += 1;
            }
        }

        return L2(vector);
    }

    public static double Cosine(float[] left, float[] right)
    {
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        var length = Math.Min(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    public static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ToBase64(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    public static float[] FromBase64(string? encoded)
    {
        var vector = new float[SemanticPolicies.Dimensions];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return vector;
        }

        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length != vector.Length * sizeof(float))
            {
                return vector;
            }

            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            return vector;
        }
        catch (FormatException)
        {
            return vector;
        }
    }

    public static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= SemanticPolicies.ExcerptLength
            ? trimmed
            : trimmed[..SemanticPolicies.ExcerptLength].Trim();
    }

    private static string Normalize(string? text)
    {
        var decomposed = (text ?? string.Empty).Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var buffer = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(ch);
            }
            else if (buffer.Length > 0 && buffer[^1] != ' ')
            {
                buffer.Append(' ');
            }
        }

        return buffer.ToString().Trim();
    }

    private static int Bucket(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return hash[0] & (SemanticPolicies.Dimensions - 1);
    }

    private static float[] L2(float[] vector)
    {
        double sum = 0;
        for (var i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }

        if (sum == 0)
        {
            return vector;
        }

        var norm = (float)Math.Sqrt(sum);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }
}

/// <summary>
/// Every sentence of a grounded answer is a citation excerpt. No preamble.
/// </summary>
public static class SemanticAnswerComposer
{
    public static SemanticAnswer Compose(IReadOnlyList<SemanticCitation> citations)
    {
        if (citations.Count == 0)
        {
            return new SemanticAnswer(SemanticPolicies.EvidenceNone, null, 0);
        }

        var lines = new string[citations.Count];
        var confidence = 0d;
        for (var i = 0; i < citations.Count; i++)
        {
            lines[i] = $"[{i + 1}] {citations[i].Excerpt}";
            if (citations[i].Score > confidence)
            {
                confidence = citations[i].Score;
            }
        }

        return new SemanticAnswer(SemanticPolicies.EvidenceCited, string.Join("\n", lines), confidence);
    }
}
