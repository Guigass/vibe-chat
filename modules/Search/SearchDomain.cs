using System.Globalization;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Search;

public sealed record MessageIndexed(
    MessageId MessageId,
    TenantId TenantId,
    ChannelId ChannelId,
    string Body,
    bool IsDeleted,
    DateTimeOffset UpdatedAt);

public enum SearchSort
{
    Relevance = 0,
    Date = 1
}

public sealed record SearchMessagesQuery(
    TenantId TenantId,
    UserId UserId,
    WorkspaceId WorkspaceId,
    string Term,
    ChannelId? ChannelId = null,
    int Limit = 20,
    UserId? AuthorId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool? HasAttachment = null,
    bool? HasLink = null,
    string? AttachmentKind = null,
    SearchSort Sort = SearchSort.Relevance,
    string? Cursor = null);

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
    int Limit,
    int Total = 0,
    string? Cursor = null);

public sealed record SearchPageCursor(
    SearchSort Sort,
    double Rank,
    DateTimeOffset CreatedAt,
    Guid MessageId);

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

    public static readonly HashSet<string> AttachmentKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "audio", "document"
    };

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

    public static bool HasStructuredFilter(SearchMessagesQuery query) =>
        query.ChannelId is not null
        || query.AuthorId is not null
        || query.From is not null
        || query.To is not null
        || query.HasAttachment is not null
        || query.HasLink is not null
        || !string.IsNullOrWhiteSpace(query.AttachmentKind);

    public static SearchSort ParseSort(string? sort) =>
        string.Equals(sort, "date", StringComparison.OrdinalIgnoreCase)
            ? SearchSort.Date
            : SearchSort.Relevance;

    public static bool TryParseAttachmentKind(string? raw, out string? kind)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            kind = null;
            return true;
        }

        kind = raw.Trim().ToLowerInvariant();
        return AttachmentKinds.Contains(kind);
    }

    public static bool TryParseInstant(string? raw, bool endOfDay, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var text = raw.Trim();
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            if (text.Length == 10 && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            {
                var day = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                value = endOfDay ? day.AddDays(1).AddTicks(-1) : day;
                return true;
            }

            value = parsed;
            return true;
        }

        return false;
    }

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

public static class SearchCursorCodec
{
    public static string Encode(SearchPageCursor cursor)
    {
        var sort = cursor.Sort == SearchSort.Date ? "date" : "relevance";
        var payload = string.Join('|',
            sort,
            cursor.Rank.ToString("R", CultureInfo.InvariantCulture),
            cursor.CreatedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            cursor.MessageId.ToString("D"));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string? raw, out SearchPageCursor cursor)
    {
        cursor = new SearchPageCursor(SearchSort.Relevance, 0, DateTimeOffset.MinValue, Guid.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var padded = raw.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var parts = payload.Split('|');
            if (parts.Length != 4)
            {
                return false;
            }

            var sort = SearchPolicies.ParseSort(parts[0]);
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rank)
                || !DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt)
                || !Guid.TryParse(parts[3], out var messageId))
            {
                return false;
            }

            cursor = new SearchPageCursor(sort, rank, createdAt, messageId);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
