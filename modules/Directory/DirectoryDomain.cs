using System.Security.Cryptography;
using System.Text;
using VibeChat.Conversations;
using VibeChat.SharedKernel;

namespace VibeChat.Directory;

public sealed class Space : AggregateRoot
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Single-use channel invite for an external guest (B-040). Token is stored hashed only.</summary>
public sealed class ChannelInvite : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public ChannelId ChannelId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public UserId CreatedByUserId { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public UserId? AcceptedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public UserId? RevokedByUserId { get; set; }
}

public static class InvitePolicies
{
    public const int DefaultExpiryDays = 7;
    public const int DefaultMaxExpiryDays = 30;
    public const int TokenBytes = 32;
    public const int CreatePerMinute = 10;
    public const int AcceptPerMinute = 20;

    public static bool AllowsInvite(ChannelType type) =>
        type is ChannelType.Public or ChannelType.Private or ChannelType.Announcement or ChannelType.Group;

    public static int NormalizeExpiryDays(int? requested, int maxExpiryDays)
    {
        var days = requested ?? DefaultExpiryDays;
        if (days < 1)
        {
            days = 1;
        }

        var cap = maxExpiryDays < 1 ? DefaultMaxExpiryDays : maxExpiryDays;
        return days > cap ? cap : days;
    }

    public static string? NormalizeEmail(string? email)
    {
        var value = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Contains('@') && value.Length <= 256 ? value : null;
    }

    public static bool EmailMatches(string? inviteEmail, string profileEmail)
    {
        if (string.IsNullOrWhiteSpace(inviteEmail))
        {
            return true;
        }

        return string.Equals(inviteEmail, profileEmail.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    public static string Status(ChannelInvite invite, DateTimeOffset now)
    {
        if (invite.RevokedAt is not null)
        {
            return "Revoked";
        }

        if (invite.AcceptedAt is not null)
        {
            return "Accepted";
        }

        return invite.ExpiresAt <= now ? "Expired" : "Pending";
    }

    public static bool IsUnavailable(ChannelInvite? invite, DateTimeOffset now) =>
        invite is null
        || invite.RevokedAt is not null
        || invite.AcceptedAt is not null
        || invite.ExpiresAt <= now;
}

public static class InviteToken
{
    public static string CreateRaw()
    {
        Span<byte> bytes = stackalloc byte[InvitePolicies.TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed class InviteOptions
{
    public const string SectionName = "Directory:Invites";

    public bool Enabled { get; set; }
    public int MaxExpiryDays { get; set; } = InvitePolicies.DefaultMaxExpiryDays;
}
