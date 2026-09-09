using System.Text.Json;
using VibeChat.SharedKernel;

namespace VibeChat.Notifications;

public enum NotificationLevel
{
    All = 0,
    MentionsAndDms = 1,
    None = 2
}

public sealed class NotificationPreference
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public bool EmailEnabled { get; set; } = true;
    public bool PushEnabled { get; set; } = true;

    /// <summary>Global notification level (B-097). Channel overrides take precedence when active.</summary>
    public NotificationLevel Level { get; set; } = NotificationLevel.MentionsAndDms;
    public bool HidePreview { get; set; }
    public bool DndEnabled { get; set; }
    public TimeOnly? DndStart { get; set; }
    public TimeOnly? DndEnd { get; set; }

    /// <summary>Bitmask, Sunday=1 .. Saturday=64. 0 or null means every day.</summary>
    public short DndDays { get; set; }

    /// <summary>IANA time zone id; DND is always computed live against it, never a fixed offset.</summary>
    public string? TimeZone { get; set; }

    /// <summary>Stored but not yet sent (B-097 follow-up) — see spec note.</summary>
    public bool DigestEnabled { get; set; }

    /// <summary>DM authors who bypass DND.</summary>
    public Guid[] PriorityContactUserIds { get; set; } = [];
}

/// <summary>
/// Per-channel notification override (B-097). Row presence means an override is active;
/// absence means "use the global default". <see cref="MutedUntil"/> is evaluated live at
/// read time — no cleanup job needed for temporary mutes to expire on their own.
/// </summary>
public sealed class ChannelNotificationPreference
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public ChannelId ChannelId { get; set; }
    public NotificationLevel Level { get; set; }
    public DateTimeOffset? MutedUntil { get; set; }
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

public sealed class PushSubscription
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
}

public enum PushSendStatus
{
    Delivered = 0,
    Gone = 1,
    Failed = 2
}

public sealed record PushSendResult(PushSendStatus Status);

public sealed record PushDeliveryRequest(string Endpoint, string P256dh, string Auth, string PayloadJson);

public interface IPushSender
{
    string Name { get; }
    bool IsEnabled { get; }
    Task<PushSendResult> SendAsync(PushDeliveryRequest request, CancellationToken cancellationToken);
}

/// <summary>Default when Push:Enabled=false (D-13 / B-095).</summary>
public sealed class NullPushSender : IPushSender
{
    public string Name => "Null";
    public bool IsEnabled => false;

    public Task<PushSendResult> SendAsync(PushDeliveryRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PushSendResult(PushSendStatus.Delivered));
}

public static class PushDispatchPolicies
{
    public const int PreviewMaxChars = 80;
    public const int MaxSubscriptionsPerMessage = 50;
    public const int SendConcurrency = 4;

    /// <summary>
    /// B-097: resolves the effective notification level for one candidate. A channel override whose
    /// <see cref="ChannelNotificationPreference.MutedUntil"/> has already passed is treated as
    /// expired and falls back to the global level — no cleanup job required.
    /// </summary>
    public static NotificationLevel ResolveEffectiveLevel(
        NotificationLevel globalLevel,
        (NotificationLevel Level, DateTimeOffset? MutedUntil)? channelOverride,
        DateTimeOffset nowUtc)
    {
        if (channelOverride is { } o && (o.MutedUntil is null || o.MutedUntil > nowUtc))
        {
            return o.Level;
        }

        return globalLevel;
    }

    public static bool ShouldNotifyForLevel(NotificationLevel level, bool isDirect, bool isMentioned, bool isAuthor)
    {
        if (isAuthor || level == NotificationLevel.None)
        {
            return false;
        }

        if (isDirect)
        {
            return true;
        }

        return level == NotificationLevel.All || isMentioned;
    }

    /// <summary>
    /// Computes DND live against the IANA time zone on every call — never a stored fixed offset,
    /// so DST transitions stay correct. Fails open (returns false) on an invalid/empty time zone.
    /// </summary>
    public static bool IsWithinDnd(
        bool dndEnabled,
        TimeOnly? start,
        TimeOnly? end,
        short dndDays,
        string? timeZoneId,
        DateTimeOffset nowUtc)
    {
        if (!dndEnabled || start is null || end is null || string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(nowUtc, tz);
        if (dndDays != 0)
        {
            var dayBit = (short)(1 << (int)local.DayOfWeek);
            if ((dndDays & dayBit) == 0)
            {
                return false;
            }
        }

        var timeOfDay = TimeOnly.FromDateTime(local.DateTime);
        var startValue = start.Value;
        var endValue = end.Value;
        return startValue <= endValue
            ? timeOfDay >= startValue && timeOfDay < endValue
            : timeOfDay >= startValue || timeOfDay < endValue;
    }

    /// <summary>Only a DM from a marked priority contact bypasses DND.</summary>
    public static bool IsPriorityBypass(bool isDirect, Guid authorId, IReadOnlyCollection<Guid> priorityContactUserIds) =>
        isDirect && priorityContactUserIds.Contains(authorId);

    public static bool IsSuppressedByCursor(long? lastReadSeq, long messageSeq) =>
        lastReadSeq is long read && read >= messageSeq;

    public static bool IsPushEnabled(bool? stored) => stored is not false;

    public static string TruncatePreview(string body)
    {
        var text = (body ?? string.Empty).Trim();
        if (text.Length <= PreviewMaxChars)
        {
            return text;
        }

        return text[..PreviewMaxChars].TrimEnd() + "…";
    }

    public static string DisplayAuthor(string? authorName)
    {
        var name = (authorName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name) || Guid.TryParse(name, out _))
        {
            return "Alguém";
        }

        return name;
    }

    public static string ChannelLabel(bool isDirect, string? channelName)
    {
        if (isDirect)
        {
            return "mensagem direta";
        }

        var name = (channelName ?? string.Empty).Trim().TrimStart('#');
        if (string.IsNullOrEmpty(name)
            || name.StartsWith("dm:", StringComparison.OrdinalIgnoreCase)
            || Guid.TryParse(name, out _))
        {
            return "#canal";
        }

        return "#" + name;
    }

    public static string NotificationTitle(bool isDirect, string? authorName, string? channelName)
    {
        var author = DisplayAuthor(authorName);
        if (isDirect)
        {
            return author == "Alguém" ? "Mensagem direta" : author;
        }

        return $"{author} · {ChannelLabel(false, channelName)}";
    }

    public static string BuildNgswPayload(
        string authorName,
        bool isDirect,
        string channelName,
        string preview,
        Guid channelId,
        Guid messageId,
        long sequence)
    {
        var title = NotificationTitle(isDirect, authorName, channelName);
        var url = $"/app?channel={channelId:D}&message={messageId:D}&seq={sequence}";
        return JsonSerializer.Serialize(new
        {
            notification = new
            {
                title,
                body = preview,
                icon = "/icons/icon-192x192.png",
                tag = messageId.ToString("D"),
                data = new
                {
                    channelId,
                    messageId,
                    seq = sequence,
                    onActionClick = new
                    {
                        @default = new
                        {
                            operation = "navigateLastFocusedOrOpen",
                            url
                        }
                    }
                }
            }
        });
    }
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

/// <summary>Invite/role emails follow the recipient locale (B-100). Pass <c>UserLocales.Resolve</c>.</summary>
public static class MemberEmailCopy
{
    public static (string Subject, string BodyText) Invite(
        string locale,
        string workspaceName,
        string displayName,
        string role) =>
        locale switch
        {
            "en" => (
                $"VibeChat: you were invited to {workspaceName}",
                $"Hello {displayName},\n\n" +
                $"You were added to the workspace \"{workspaceName}\" with the role {role}.\n" +
                "Sign in via SSO (Keycloak/OIDC) with this email to access.\n" +
                "There is no open self-signup — membership was already provisioned by an admin.\n"),
            "es" => (
                $"VibeChat: fuiste invitado a {workspaceName}",
                $"Hola {displayName},\n\n" +
                $"Te añadieron al workspace \"{workspaceName}\" con el rol {role}.\n" +
                "Inicia sesión por SSO (Keycloak/OIDC) con este correo para acceder.\n" +
                "No hay auto-registro abierto — un admin ya provisionó la membresía.\n"),
            "fr" => (
                $"VibeChat: vous avez été invité à {workspaceName}",
                $"Bonjour {displayName},\n\n" +
                $"Vous avez été ajouté à l'espace de travail « {workspaceName} » avec le rôle {role}.\n" +
                "Connectez-vous via SSO (Keycloak/OIDC) avec cet e-mail pour accéder.\n" +
                "Il n'y a pas d'inscription libre — un admin a déjà provisionné l'adhésion.\n"),
            "de" => (
                $"VibeChat: du wurdest zu {workspaceName} eingeladen",
                $"Hallo {displayName},\n\n" +
                $"Du wurdest dem Workspace \"{workspaceName}\" mit der Rolle {role} hinzugefügt.\n" +
                "Melde dich per SSO (Keycloak/OIDC) mit dieser E-Mail an.\n" +
                "Es gibt keine offene Selbstregistrierung — ein Admin hat die Mitgliedschaft bereits eingerichtet.\n"),
            "it" => (
                $"VibeChat: sei stato invitato a {workspaceName}",
                $"Ciao {displayName},\n\n" +
                $"Sei stato aggiunto al workspace \"{workspaceName}\" con il ruolo {role}.\n" +
                "Accedi via SSO (Keycloak/OIDC) con questa e-mail.\n" +
                "Non c'è auto-iscrizione aperta — un admin ha già creato la membership.\n"),
            "ja" => (
                $"VibeChat: {workspaceName} に招待されました",
                $"{displayName} さん\n\n" +
                $"ワークスペース「{workspaceName}」にロール {role} で追加されました。\n" +
                "このメールで SSO（Keycloak/OIDC）にサインインしてください。\n" +
                "公開の自己登録はありません。管理者によってメンバーシップは既に用意されています。\n"),
            "zh-CN" => (
                $"VibeChat: 你已被邀请加入 {workspaceName}",
                $"{displayName}，你好：\n\n" +
                $"你已被添加到工作区“{workspaceName}”，角色为 {role}。\n" +
                "请使用此邮箱通过 SSO（Keycloak/OIDC）登录。\n" +
                "没有开放的自助注册 — 管理员已开通成员资格。\n"),
            "ko" => (
                $"VibeChat: {workspaceName}에 초대되었습니다",
                $"{displayName}님, 안녕하세요.\n\n" +
                $"역할 {role}(으)로 워크스페이스 \"{workspaceName}\"에 추가되었습니다.\n" +
                "이 이메일로 SSO(Keycloak/OIDC)에 로그인하세요.\n" +
                "공개 자가 가입은 없습니다. 관리자가 멤버십을 이미 프로비저닝했습니다.\n"),
            "ru" => (
                $"VibeChat: вас пригласили в {workspaceName}",
                $"Здравствуйте, {displayName}!\n\n" +
                $"Вас добавили в рабочее пространство «{workspaceName}» с ролью {role}.\n" +
                "Войдите через SSO (Keycloak/OIDC) с этим адресом почты.\n" +
                "Открытой самостоятельной регистрации нет — администратор уже выдал членство.\n"),
            _ => (
                $"VibeChat: você foi convidado para {workspaceName}",
                $"Olá {displayName},\n\n" +
                $"Você foi adicionado ao workspace \"{workspaceName}\" com o papel {role}.\n" +
                "Autentique-se via SSO (Keycloak/OIDC) com este e-mail para acessar.\n" +
                "Não há self-signup aberto — a membership já foi provisionada pelo admin.\n"),
        };

    public static (string Subject, string BodyText) RoleChanged(
        string locale,
        string workspaceName,
        string displayName,
        string previousRole,
        string newRole) =>
        locale switch
        {
            "en" => (
                $"VibeChat: your role in {workspaceName} was updated",
                $"Hello {displayName},\n\nYour role in the workspace \"{workspaceName}\" changed from {previousRole} to {newRole}.\n"),
            "es" => (
                $"VibeChat: tu rol en {workspaceName} se actualizó",
                $"Hola {displayName},\n\nTu rol en el workspace \"{workspaceName}\" cambió de {previousRole} a {newRole}.\n"),
            "fr" => (
                $"VibeChat: votre rôle dans {workspaceName} a été mis à jour",
                $"Bonjour {displayName},\n\nVotre rôle dans l'espace de travail « {workspaceName} » est passé de {previousRole} à {newRole}.\n"),
            "de" => (
                $"VibeChat: deine Rolle in {workspaceName} wurde aktualisiert",
                $"Hallo {displayName},\n\nDeine Rolle im Workspace \"{workspaceName}\" wurde von {previousRole} zu {newRole} geändert.\n"),
            "it" => (
                $"VibeChat: il tuo ruolo in {workspaceName} è stato aggiornato",
                $"Ciao {displayName},\n\nIl tuo ruolo nel workspace \"{workspaceName}\" è cambiato da {previousRole} a {newRole}.\n"),
            "ja" => (
                $"VibeChat: {workspaceName} のロールが更新されました",
                $"{displayName} さん\n\nワークスペース「{workspaceName}」のロールが {previousRole} から {newRole} に変わりました。\n"),
            "zh-CN" => (
                $"VibeChat: 你在 {workspaceName} 的角色已更新",
                $"{displayName}，你好：\n\n你在工作区“{workspaceName}”的角色已从 {previousRole} 更改为 {newRole}。\n"),
            "ko" => (
                $"VibeChat: {workspaceName}의 역할이 변경되었습니다",
                $"{displayName}님, 안녕하세요.\n\n워크스페이스 \"{workspaceName}\"의 역할이 {previousRole}에서 {newRole}(으)로 변경되었습니다.\n"),
            "ru" => (
                $"VibeChat: ваша роль в {workspaceName} обновлена",
                $"Здравствуйте, {displayName}!\n\nВаша роль в рабочем пространстве «{workspaceName}» изменена с {previousRole} на {newRole}.\n"),
            _ => (
                $"VibeChat: seu papel em {workspaceName} foi atualizado",
                $"Olá {displayName},\n\nSeu papel no workspace \"{workspaceName}\" mudou de {previousRole} para {newRole}.\n"),
        };
}
