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
/// Tenant-level email/SMTP settings (B-069 / ADR-020).
/// Non-secrets override IConfiguration; password may be env fallback or AES-GCM envelope.
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

    /// <summary>Encrypted SMTP password (ADR-020). Never expose plaintext via API.</summary>
    public EncryptedSecretEnvelope SmtpPassword { get; set; } = new();
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
