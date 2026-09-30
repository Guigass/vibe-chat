using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// Workspace bot identity for external send (B-109). The clear token is never stored.
/// </summary>
public sealed class IntegrationBot
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public UserId UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool AllowDms { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Hashed API token. At most one row per bot has <see cref="RevokedAt"/> null.</summary>
public sealed class IntegrationBotToken
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid BotId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Explicit channel grant. Empty set means the bot cannot post to channels.</summary>
public sealed class IntegrationBotChannelScope
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid BotId { get; set; }
    public ChannelId ChannelId { get; set; }
}

public static class BotIntegrationPolicies
{
    public const int MaxNameLength = 80;
    public const int TokenBytes = 32;
    public const string TokenPrefix = "vc_int_";

    public static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = (name ?? string.Empty).Trim();
        return normalized.Length is >= 1 and <= MaxNameLength;
    }
}

public sealed class BotIntegrationOptions
{
    public const string SectionName = "Integrations:Bots";

    /// <summary>Off by default (R3). Create/list/send return 404 until an operator turns it on.</summary>
    public bool Enabled { get; set; }
}

public static class IntegrationToken
{
    public static string CreateRaw()
    {
        Span<byte> bytes = stackalloc byte[BotIntegrationPolicies.TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        return BotIntegrationPolicies.TokenPrefix + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Last4(string raw)
    {
        var value = raw.Trim();
        return value.Length <= 4 ? value : value[^4..];
    }
}
