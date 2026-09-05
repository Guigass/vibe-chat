using VibeChat.SharedKernel;

namespace VibeChat.Identity;

public sealed class UserProfile : AggregateRoot
{
    public UserId Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Personal UI locale (B-100). Null until the user or client persists one.</summary>
    public string? Locale { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class UserLocales
{
    public const string Default = "pt-BR";

    public static readonly IReadOnlyList<string> Supported = ["pt-BR", "en"];

    public static bool IsSupported(string? locale) =>
        locale is not null && Supported.Contains(locale, StringComparer.Ordinal);
}

public interface IUserProfileReader
{
    Task<UserProfile?> FindBySubjectAsync(string subject, CancellationToken cancellationToken);
}
