using VibeChat.SharedKernel;

namespace VibeChat.Search;

public sealed record MessageIndexed(
    MessageId MessageId,
    TenantId TenantId,
    ChannelId ChannelId,
    string Body,
    bool IsDeleted,
    DateTimeOffset UpdatedAt);

public sealed record SearchMessagesQuery(
    TenantId TenantId,
    UserId UserId,
    WorkspaceId WorkspaceId,
    string Term,
    ChannelId? ChannelId = null,
    int Limit = 20);

public sealed record SearchMessageHit(
    Guid MessageId,
    Guid ChannelId,
    string ChannelName,
    string ChannelType,
    long Sequence,
    Guid AuthorUserId,
    string AuthorDisplayName,
    string BodyPreview,
    DateTimeOffset CreatedAt,
    double Rank);

public sealed record SearchResultPage(
    string Query,
    IReadOnlyList<SearchMessageHit> Items,
    int Limit);

public interface ISearchIndexer
{
    Task IndexMessageAsync(MessageIndexed doc, CancellationToken cancellationToken);
    Task RemoveMessageAsync(TenantId tenantId, MessageId messageId, CancellationToken cancellationToken);
}

public interface ISearchQuery
{
    Task<SearchResultPage> SearchMessagesAsync(SearchMessagesQuery query, CancellationToken cancellationToken);
}

public static class SearchPolicies
{
    public const int MinTermLength = 2;
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const int PreviewLength = 160;
    public const string TextConfig = "portuguese";

    public static int NormalizeLimit(int? limit)
    {
        var value = limit ?? DefaultLimit;
        if (value < 1)
        {
            return 1;
        }

        return value > MaxLimit ? MaxLimit : value;
    }

    public static string NormalizeTerm(string? term) => (term ?? string.Empty).Trim();

    public static string BuildPreview(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length <= PreviewLength)
        {
            return trimmed;
        }

        return trimmed[..PreviewLength].TrimEnd() + "…";
    }
}
