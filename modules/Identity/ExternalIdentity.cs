using VibeChat.SharedKernel;

namespace VibeChat.Identity;

/// <summary>
/// Stable link from an external IdP <c>(issuer, subject)</c> to a local user (B-164).
/// Global like <see cref="UserProfile"/>: login happens before a tenant is chosen.
/// </summary>
public sealed class ExternalIdentity
{
    public Guid Id { get; set; }
    public UserId UserId { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? ProviderAlias { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
