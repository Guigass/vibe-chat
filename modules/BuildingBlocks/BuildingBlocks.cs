using System.Security.Claims;
using VibeChat.SharedKernel;

namespace VibeChat.BuildingBlocks;

public interface IModuleMarker;

public interface ITenantContext
{
    TenantId TenantId { get; }
    bool HasTenant { get; }
    void SetTenant(TenantId tenantId);

    /// <summary>Authenticated user for RLS bootstrap (membership discovery before tenant is known).</summary>
    UserId UserId { get; }
    bool HasUser { get; }
    void SetUser(UserId userId);

    /// <summary>Worker-only GUC: <c>outbox</c> or <c>retention</c>; never a bypass kill-switch.</summary>
    string? JobRole { get; }
    void SetJobRole(string? jobRole);
}

public sealed class TenantContext : ITenantContext
{
    public TenantId TenantId { get; private set; } = TenantId.Empty;
    public bool HasTenant => TenantId.Value != Guid.Empty;
    public void SetTenant(TenantId tenantId) => TenantId = tenantId;

    public UserId UserId { get; private set; } = UserId.Empty;
    public bool HasUser => UserId.Value != Guid.Empty;
    public void SetUser(UserId userId) => UserId = userId;

    public string? JobRole { get; private set; }
    public void SetJobRole(string? jobRole) =>
        JobRole = string.IsNullOrWhiteSpace(jobRole) ? null : jobRole.Trim();
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    UserId UserId { get; }
    string Subject { get; }
    string Email { get; }
    string DisplayName { get; }
    IReadOnlyCollection<Role> Roles { get; }
    ClaimsPrincipal Principal { get; }
}

public sealed class CurrentUser : ICurrentUser
{
    private readonly ClaimsPrincipal _principal;

    public CurrentUser(ClaimsPrincipal principal)
    {
        _principal = principal;
    }

    public bool IsAuthenticated => _principal.Identity?.IsAuthenticated == true;
    public ClaimsPrincipal Principal => _principal;
    public string Subject => ClaimValue(ClaimTypes.NameIdentifier) ?? ClaimValue("sub") ?? string.Empty;
    public string Email => ClaimValue(ClaimTypes.Email) ?? ClaimValue("email") ?? string.Empty;
    public string DisplayName => ClaimValue("name") ?? _principal.Identity?.Name ?? Email;

    public UserId UserId
    {
        get
        {
            var value = ClaimValue("vibechat_user_id") ?? ClaimValue("user_id");
            return Guid.TryParse(value, out var id) ? new UserId(id) : UserId.Empty;
        }
    }

    public IReadOnlyCollection<Role> Roles => _principal.FindAll(ClaimTypes.Role)
        .Select(c => Enum.TryParse<Role>(c.Value, true, out var role) ? role : Role.Member)
        .Distinct()
        .ToArray();

    private string? ClaimValue(string type) => _principal.FindFirst(type)?.Value;
}

public abstract record IntegrationEvent
{
    protected IntegrationEvent(TenantId tenantId)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; init; }
    public TenantId TenantId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
}

public interface IOutboxWriter
{
    void Add(OutboxMessage message);
}

public sealed record IdempotencyRecord(
    TenantId TenantId,
    string Key,
    string RequestHash,
    string ResultJson,
    DateTimeOffset CreatedAt);

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(TenantId tenantId, string key, CancellationToken cancellationToken);
    Task StoreAsync(IdempotencyRecord record, CancellationToken cancellationToken);
}

public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(TenantId tenantId, UserId userId, string permission, CancellationToken cancellationToken);
}

public static class RolePermissionCatalog
{
    public static IReadOnlySet<string> For(Role role) => role switch
    {
        Role.PlatformOwner or Role.WorkspaceOwner or Role.Admin => AdminPermissions,
        Role.Moderator => ModeratorPermissions,
        Role.Auditor => AuditorPermissions,
        Role.Member => MemberPermissions,
        Role.Bot => BotPermissions,
        _ => GuestPermissions
    };

    private static readonly HashSet<string> AdminPermissions =
    [
        Permissions.Workspace.Read, Permissions.Workspace.Manage, Permissions.Workspace.Admin,
        Permissions.Channel.Read, Permissions.Channel.Create, Permissions.Channel.Manage, Permissions.Channel.MentionAll,
        Permissions.Message.Read, Permissions.Message.Send, Permissions.Message.React, Permissions.Message.EditOwn, Permissions.Message.DeleteOwn, Permissions.Message.DeleteAny,
        Permissions.Files.Upload, Permissions.Files.Download,
        Permissions.Search.Messages,
        Permissions.Admin.Dashboard, Permissions.Ai.Summarize, Permissions.Ai.SuggestReply, Permissions.Ai.Transcribe
    ];

    private static readonly HashSet<string> ModeratorPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Channel.Create, Permissions.Channel.MentionAll,
        Permissions.Message.Read, Permissions.Message.Send, Permissions.Message.React, Permissions.Message.EditOwn, Permissions.Message.DeleteOwn, Permissions.Message.DeleteAny,
        Permissions.Files.Upload, Permissions.Files.Download,
        Permissions.Search.Messages,
        Permissions.Ai.Summarize, Permissions.Ai.SuggestReply, Permissions.Ai.Transcribe
    ];

    private static readonly HashSet<string> AuditorPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Message.Read, Permissions.Files.Download,
        Permissions.Search.Messages, Permissions.Admin.Dashboard
    ];

    private static readonly HashSet<string> MemberPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Channel.Create, Permissions.Channel.MentionAll,
        Permissions.Message.Read, Permissions.Message.Send, Permissions.Message.React, Permissions.Message.EditOwn, Permissions.Message.DeleteOwn,
        Permissions.Files.Upload, Permissions.Files.Download,
        Permissions.Search.Messages,
        Permissions.Ai.Summarize, Permissions.Ai.SuggestReply, Permissions.Ai.Transcribe
    ];

    private static readonly HashSet<string> GuestPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Message.Read, Permissions.Files.Download
    ];

    private static readonly HashSet<string> BotPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Channel.MentionAll,
        Permissions.Message.Read, Permissions.Message.Send, Permissions.Message.React,
        Permissions.Files.Upload, Permissions.Files.Download,
        Permissions.Search.Messages,
        Permissions.Ai.Summarize, Permissions.Ai.SuggestReply, Permissions.Ai.Transcribe
    ];
}

/// <summary>
/// Membership role assignment rules (B-041). Guests are out of MVP (D-07).
/// </summary>
public static class WorkspaceRolePolicies
{
    public static readonly Role[] AssignableRoles =
    [
        Role.Member,
        Role.Moderator,
        Role.Auditor,
        Role.Admin
    ];

    public static bool IsAssignable(Role role) => AssignableRoles.Contains(role);

    public static bool CanManageRoles(Role actorRole) =>
        RolePermissionCatalog.For(actorRole).Contains(Permissions.Workspace.Admin);

    /// <summary>Invite/provision membership (B-068). Same gate as role management.</summary>
    public static bool CanInviteMembers(Role actorRole) => CanManageRoles(actorRole);

    public static bool CanChangeMemberRole(Role actorRole, Role targetCurrentRole, Role targetNewRole, bool isSelf)
    {
        if (isSelf)
        {
            return false;
        }

        if (!CanManageRoles(actorRole))
        {
            return false;
        }

        if (!IsAssignable(targetNewRole))
        {
            return false;
        }

        // Protect ownership / platform roles — reassignment is out of this admin surface.
        if (targetCurrentRole is Role.PlatformOwner or Role.WorkspaceOwner)
        {
            return false;
        }

        // Guest/Bot membership is not managed here (D-07: membership obrigatória).
        if (targetCurrentRole is Role.Guest or Role.Bot)
        {
            return false;
        }

        return true;
    }

    public static bool CanAssignInviteRole(Role actorRole, Role inviteRole) =>
        CanInviteMembers(actorRole) && IsAssignable(inviteRole);

    public static bool TryParseRole(string? value, out Role role) =>
        Enum.TryParse(value, ignoreCase: true, out role);

    public static string PendingSubjectForEmail(string normalizedEmail) =>
        $"pending:{normalizedEmail}";

    public static bool IsPendingSubject(string subject) =>
        subject.StartsWith("pending:", StringComparison.OrdinalIgnoreCase);
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

public interface IRateLimiter
{
    Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken cancellationToken);
}

public static class RateLimitKeys
{
    // GAP-redis-keys — tenant-first prefix (docs/security/multi-tenant.md)
    public static string SendMessage(TenantId tenantId, UserId userId) =>
        $"t:{tenantId.Value}:rl:send:{userId.Value}";

    public static string Hub(TenantId tenantId, UserId userId) =>
        $"t:{tenantId.Value}:rl:hub:{userId.Value}";
}

public static class RateLimitPolicies
{
    public const int DefaultSendPerMinute = 60;
    public const int DefaultHubPerMinute = 120;
    public const int MinPerMinute = 1;
    public const int MaxPerMinute = 10_000;
}

/// <summary>Tenant-level rate limits (ADR-020). Effective = min(DB, env ceiling).</summary>
public sealed class TenantRateLimitSettings
{
    public TenantId TenantId { get; set; }
    public int SendPerMinute { get; set; } = RateLimitPolicies.DefaultSendPerMinute;
    public int HubPerMinute { get; set; } = RateLimitPolicies.DefaultHubPerMinute;
    public DateTimeOffset UpdatedAt { get; set; }
}
