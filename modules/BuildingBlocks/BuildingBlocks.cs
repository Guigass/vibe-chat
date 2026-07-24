using System.Security.Claims;
using VibeChat.SharedKernel;

namespace VibeChat.BuildingBlocks;

public interface IModuleMarker;

public interface ITenantContext
{
    TenantId TenantId { get; }
    bool HasTenant { get; }
    void SetTenant(TenantId tenantId);
}

public sealed class TenantContext : ITenantContext
{
    public TenantId TenantId { get; private set; } = TenantId.Empty;
    public bool HasTenant => TenantId.Value != Guid.Empty;
    public void SetTenant(TenantId tenantId) => TenantId = tenantId;
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
        Permissions.Channel.Read, Permissions.Channel.Create, Permissions.Channel.Manage,
        Permissions.Message.Read, Permissions.Message.Send, Permissions.Message.EditOwn, Permissions.Message.DeleteOwn, Permissions.Message.DeleteAny,
        Permissions.Files.Upload, Permissions.Files.Download,
        Permissions.Search.Messages,
        Permissions.Admin.Dashboard, Permissions.Ai.Summarize
    ];

    private static readonly HashSet<string> ModeratorPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Channel.Create,
        Permissions.Message.Read, Permissions.Message.Send, Permissions.Message.EditOwn, Permissions.Message.DeleteOwn, Permissions.Message.DeleteAny,
        Permissions.Files.Upload, Permissions.Files.Download,
        Permissions.Search.Messages,
        Permissions.Ai.Summarize
    ];

    private static readonly HashSet<string> AuditorPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Message.Read, Permissions.Files.Download,
        Permissions.Search.Messages, Permissions.Admin.Dashboard
    ];

    private static readonly HashSet<string> MemberPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Message.Read, Permissions.Message.Send, Permissions.Message.EditOwn, Permissions.Message.DeleteOwn,
        Permissions.Files.Upload, Permissions.Files.Download,
        Permissions.Search.Messages,
        Permissions.Ai.Summarize
    ];

    private static readonly HashSet<string> GuestPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Message.Read, Permissions.Files.Download
    ];

    private static readonly HashSet<string> BotPermissions =
    [
        Permissions.Workspace.Read, Permissions.Channel.Read, Permissions.Message.Read, Permissions.Message.Send,
        Permissions.Files.Upload, Permissions.Files.Download,
        Permissions.Search.Messages,
        Permissions.Ai.Summarize
    ];
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
    public static string SendMessage(TenantId tenantId, UserId userId) => $"rl:send:{tenantId.Value:N}:{userId.Value:N}";
    public static string Hub(TenantId tenantId, UserId userId) => $"rl:hub:{tenantId.Value:N}:{userId.Value:N}";
}

public static class RateLimitPolicies
{
    public const int DefaultSendPerMinute = 60;
    public const int DefaultHubPerMinute = 120;
}
