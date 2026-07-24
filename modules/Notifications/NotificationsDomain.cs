using VibeChat.SharedKernel;

namespace VibeChat.Notifications;

public sealed class NotificationPreference
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public bool EmailEnabled { get; set; } = true;
    public bool PushEnabled { get; set; } = true;
}
