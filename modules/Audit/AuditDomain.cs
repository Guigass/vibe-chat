using VibeChat.SharedKernel;

namespace VibeChat.Audit;

public sealed class AuditEvent : AggregateRoot
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
}

public static class AuditActions
{
    public const string AdminLogin = "admin.login";
    public const string ChannelCreate = "channel.create";
    public const string SpaceCreate = "space.create";
    public const string MessageSend = "message.send";
    public const string MessageForward = "message.forward";
    public const string MessageDelete = "message.delete";
    public const string AttachmentUpload = "attachment.upload";
    public const string MemberRoleChange = "member.role.change";
    public const string MemberInvite = "member.invite";
    public const string SettingsChange = "settings.change";
    public const string SettingsCredentialRotate = "settings.credential.rotate";
    public const string SettingsEncryptionReencrypt = "settings.encryption.reencrypt";
    public const string SettingsLegacySecretMigrate = "settings.legacy-secret.migrate";
    public const string WorkspaceExport = "workspace.export";
    public const string MessagePurge = "message.purge";
    public const string AiTranscribe = "ai.transcribe";
}

public interface IAuditWriter
{
    void Add(AuditEvent auditEvent);
}
