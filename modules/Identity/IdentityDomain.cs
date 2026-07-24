using VibeChat.SharedKernel;

namespace VibeChat.Identity;

public sealed class UserProfile : AggregateRoot
{
    public UserId Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public interface IUserProfileReader
{
    Task<UserProfile?> FindBySubjectAsync(string subject, CancellationToken cancellationToken);
}
