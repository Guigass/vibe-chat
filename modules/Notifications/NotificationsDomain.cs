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

/// <summary>
/// Tenant-level email/SMTP non-secret overrides (B-069). Password/API secrets stay in env.
/// When a row exists, Enabled/Host/Port/Username/From/UseStartTls override IConfiguration.
/// </summary>
public sealed class TenantEmailSettings
{
    public TenantId TenantId { get; set; }
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string Username { get; set; } = string.Empty;
    public string From { get; set; } = "noreply@localhost";
    public bool UseStartTls { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record EmailMessage(
    string To,
    string Subject,
    string BodyText,
    string? From = null,
    Guid? TenantId = null);

public interface IEmailSender
{
    string Name { get; }
    bool IsEnabled { get; }
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Default when Email:Enabled=false (D-10 / B-043).</summary>
public sealed class NullEmailSender : IEmailSender
{
    public string Name => "Null";
    public bool IsEnabled => false;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record MemberRoleChangedEmailEvent(
    Guid TenantId,
    Guid WorkspaceId,
    Guid TargetUserId,
    string To,
    string Subject,
    string BodyText);

/// <summary>Optional invite email after admin provisions membership (B-068 / D-10).</summary>
public sealed record MemberInvitedEmailEvent(
    Guid TenantId,
    Guid WorkspaceId,
    Guid TargetUserId,
    string To,
    string Subject,
    string BodyText);
