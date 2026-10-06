using VibeChat.SharedKernel;

namespace VibeChat.Identity;

/// <summary>
/// Public member card inside one tenant (B-167). Distinct from temporary status (B-116)
/// and from the global identity row, which has no tenant and cannot take FORCE RLS.
/// </summary>
public sealed class MemberPublicProfile
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public string? JobTitle { get; set; }
    public string? About { get; set; }
    public string? HighlightMessage { get; set; }
    public string? AvatarObjectKey { get; set; }
    public string? AvatarContentType { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class MemberProfileRules
{
    public const int MaxDisplayNameLength = 80;
    public const int MaxJobTitleLength = 80;
    public const int MaxAboutLength = 500;
    public const int MaxHighlightLength = 160;
    public const int MaxAvatarBytes = 2 * 1024 * 1024;

    public const string DisplayNameRequired = "DisplayNameRequired";
    public const string DisplayNameTooLong = "DisplayNameTooLong";
    public const string DisplayNameInvalid = "DisplayNameInvalid";
    public const string JobTitleTooLong = "JobTitleTooLong";
    public const string AboutTooLong = "AboutTooLong";
    public const string HighlightTooLong = "HighlightTooLong";
    public const string ProfileTextInvalid = "ProfileTextInvalid";
    public const string AvatarEmpty = "AvatarEmpty";
    public const string AvatarTooLarge = "AvatarTooLarge";
    public const string AvatarTypeNotAllowed = "AvatarTypeNotAllowed";

    public static bool TryNormalize(
        string? displayName,
        string? jobTitle,
        string? about,
        string? highlight,
        out string normalizedName,
        out string? normalizedJob,
        out string? normalizedAbout,
        out string? normalizedHighlight,
        out string error)
    {
        normalizedName = string.Empty;
        normalizedJob = null;
        normalizedAbout = null;
        normalizedHighlight = null;
        error = string.Empty;

        if (!TryRequired(displayName, MaxDisplayNameLength, DisplayNameRequired, DisplayNameTooLong, out normalizedName, out error))
        {
            return false;
        }

        if (!TryOptional(jobTitle, MaxJobTitleLength, JobTitleTooLong, out normalizedJob, out error))
        {
            return false;
        }

        if (!TryOptional(about, MaxAboutLength, AboutTooLong, out normalizedAbout, out error))
        {
            return false;
        }

        if (!TryOptional(highlight, MaxHighlightLength, HighlightTooLong, out normalizedHighlight, out error))
        {
            return false;
        }

        return true;
    }

    public static bool TryReadImage(
        Stream input,
        string? declaredType,
        out byte[] bytes,
        out string contentType,
        out string error)
    {
        bytes = [];
        contentType = string.Empty;
        error = string.Empty;
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxAvatarBytes)
            {
                error = AvatarTooLarge;
                return false;
            }

            buffer.Write(chunk, 0, read);
        }

        bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            error = AvatarEmpty;
            return false;
        }

        var sniffed = Sniff(bytes);
        var declared = (declaredType ?? string.Empty).Trim().ToLowerInvariant();
        if (sniffed is null || declared.Length == 0 || !string.Equals(sniffed, declared, StringComparison.Ordinal))
        {
            error = AvatarTypeNotAllowed;
            bytes = [];
            return false;
        }

        contentType = sniffed;
        return true;
    }

    public static string AvatarPath(Guid workspaceId, Guid userId, DateTimeOffset updatedAt) =>
        $"/api/v1/workspaces/{workspaceId}/members/{userId}/profile/avatar?v={updatedAt.ToUnixTimeMilliseconds()}";

    public static string BuildStorageKey(TenantId tenantId, UserId userId) =>
        $"{tenantId.Value:N}/profiles/{userId.Value:N}/{Guid.NewGuid():N}";

    public static bool OwnsKey(TenantId tenantId, UserId userId, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var prefix = $"{tenantId.Value:N}/profiles/{userId.Value:N}/";
        return key.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool TryRequired(
        string? value,
        int max,
        string required,
        string tooLong,
        out string normalized,
        out string error)
    {
        normalized = (value ?? string.Empty).Trim();
        error = string.Empty;
        if (normalized.Length == 0)
        {
            error = required;
            return false;
        }

        if (HasControl(normalized))
        {
            error = DisplayNameInvalid;
            return false;
        }

        if (normalized.Length > max)
        {
            error = tooLong;
            return false;
        }

        return true;
    }

    private static bool TryOptional(string? value, int max, string tooLong, out string? normalized, out string error)
    {
        var text = (value ?? string.Empty).Trim();
        error = string.Empty;
        if (text.Length == 0)
        {
            normalized = null;
            return true;
        }

        if (HasControl(text))
        {
            normalized = null;
            error = ProfileTextInvalid;
            return false;
        }

        if (text.Length > max)
        {
            normalized = null;
            error = tooLong;
            return false;
        }

        normalized = text;
        return true;
    }

    private static bool HasControl(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Sniff(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (bytes.Length >= 6
            && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') && bytes[5] == (byte)'a')
        {
            return "image/gif";
        }

        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return "image/webp";
        }

        return null;
    }
}
