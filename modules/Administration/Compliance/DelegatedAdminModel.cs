using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>B-134. Off by default. Scopes are a fixed catalog, not custom roles.</summary>
public sealed class DelegatedAdminOptions
{
    public const string SectionName = "Features:DelegatedAdmin";

    public bool Enabled { get; set; }
}

public static class DelegatedScopes
{
    public const string Tenant = "tenant";
    public const string Workspace = "workspace";
    public const string Space = "space";
    public const string Compliance = "compliance";
    public const string Security = "security";
    public const string BillingRead = "billing-read";

    public static readonly string[] All = [Tenant, Workspace, Space, Compliance, Security, BillingRead];

    public static bool IsKnown(string? scope) =>
        scope is not null && All.Contains(scope, StringComparer.Ordinal);
}

public static class DelegatedResources
{
    public const string Users = "users";
    public const string Channels = "channels";
    public const string Storage = "storage";
    public const string Messages = "messages";
    public const string Automations = "automations";
    public const string AiBudget = "ai-budget";

    public static readonly string[] All = [Users, Channels, Storage, Messages, Automations, AiBudget];

    public static bool IsKnown(string? resource) =>
        resource is not null && All.Contains(resource, StringComparer.Ordinal);

    public static string Unit(string resource) =>
        resource == Storage ? "bytes" : "count";
}

public static class DelegatedAdminAlerts
{
    public const string Soft = "soft";
    public const string Hard = "hard";
    public const string BreakGlass = "break-glass";
    public const string BreakGlassExpired = "break-glass-expired";
}

public static class DelegatedAdminErrors
{
    public const string Disabled = "DelegatedAdminDisabled";
    public const string SelfGrant = "DelegationSelfGrant";
    public const string ScopeExceeded = "DelegationScopeExceeded";
    public const string AlreadyAssigned = "DelegationAlreadyAssigned";
    public const string ReasonRequired = "DelegationReasonRequired";
    public const string ExpiryInvalid = "DelegationExpiryInvalid";
    public const string ScopeUnknown = "DelegationScopeUnknown";
    public const string SpaceInvalid = "DelegationSpaceInvalid";
    public const string MemberRequired = "DelegationMemberRequired";
    public const string NotFound = "DelegationNotFound";
    public const string IdempotencyRequired = "DelegationIdempotencyRequired";
    public const string IdempotencyConflict = "DelegationIdempotencyConflict";
    public const string QuotaResourceUnknown = "QuotaResourceUnknown";
    public const string QuotaInvalidLimit = "QuotaInvalidLimit";
    public const string QuotaHardLimit = "QuotaHardLimit";

    public static int Status(string error) => error switch
    {
        Disabled or NotFound => 404,
        SelfGrant or ScopeExceeded => 403,
        IdempotencyConflict or AlreadyAssigned or QuotaHardLimit => 409,
        _ => 400
    };
}

public static class DelegatedAdminLimits
{
    public const int MaxReasonLength = 500;
    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxBreakGlassHours = 4;
    public const int MaxExpiryDays = 366;
}

public readonly record struct HeldScope(string Scope, Guid SpaceKey);

public sealed class ScopedRoleAssignment
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public UserId GranteeId { get; set; }
    public string Scope { get; set; } = string.Empty;
    public Guid SpaceKey { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public UserId GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public bool BreakGlass { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = "{}";
}

public sealed class ResourceQuota
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Resource { get; set; } = string.Empty;
    public long SoftLimit { get; set; }
    public long HardLimit { get; set; }
    public UserId UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class QuotaCounter
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Resource { get; set; } = string.Empty;
    public long Used { get; set; }
    public long PreviousUsed { get; set; }
}

public sealed class QuotaAlert
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Resource { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public Guid? AssignmentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record QuotaDecision(bool Allowed, bool Consumed, string? Error, long Used)
{
    public static QuotaDecision Allow(bool consumed = false, long used = 0) => new(true, consumed, null, used);

    public static QuotaDecision Deny(string error) => new(false, false, error, 0);
}

public sealed record DelegatedAdminCommandResult(int Status, object? Body, string? Error)
{
    public static DelegatedAdminCommandResult Ok(object body, int status = 200) => new(status, body, null);

    public static DelegatedAdminCommandResult Fail(string error) => new(DelegatedAdminErrors.Status(error), null, error);
}

/// <summary>Fixed scope lattice. A grantor cannot widen past the scopes they already hold.</summary>
public static class DelegatedAdminRules
{
    public static bool TryReason(string? value, out string reason)
    {
        reason = (value ?? string.Empty).Trim();
        return reason.Length is > 0 and <= DelegatedAdminLimits.MaxReasonLength;
    }

    public static bool TryScope(string? scope, Guid? spaceId, out string canonical, out Guid spaceKey)
    {
        canonical = scope ?? string.Empty;
        spaceKey = Guid.Empty;
        if (!DelegatedScopes.IsKnown(canonical))
        {
            return false;
        }

        if (canonical == DelegatedScopes.Space)
        {
            if (spaceId is not Guid id || id == Guid.Empty)
            {
                return false;
            }

            spaceKey = id;
            return true;
        }

        return spaceId is null || spaceId == Guid.Empty;
    }

    public static bool TryExpiry(DateTimeOffset now, DateTimeOffset? expiresAt, bool breakGlass)
    {
        if (breakGlass)
        {
            return expiresAt is DateTimeOffset expiry
                && expiry > now
                && expiry <= now.AddHours(DelegatedAdminLimits.MaxBreakGlassHours);
        }

        return expiresAt is null
            || (expiresAt > now && expiresAt <= now.AddDays(DelegatedAdminLimits.MaxExpiryDays));
    }

    public static bool TryLimits(long softLimit, long hardLimit) =>
        softLimit >= 0 && hardLimit >= 1 && softLimit <= hardLimit;

    public static string Hash(Guid grantee, string scope, Guid spaceKey, string reason, DateTimeOffset? expiresAt, bool breakGlass)
    {
        var raw = string.Join(
            '\n',
            grantee.ToString("N"),
            scope,
            spaceKey.ToString("N"),
            reason,
            expiresAt?.ToUniversalTime().ToString("O") ?? string.Empty,
            breakGlass ? "1" : "0");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    public static bool CanGrant(bool root, IReadOnlyList<HeldScope> held, string scope, Guid spaceKey, bool self)
    {
        if (self || !DelegatedScopes.IsKnown(scope))
        {
            return false;
        }

        if (scope == DelegatedScopes.Space && spaceKey == Guid.Empty)
        {
            return false;
        }

        if (scope != DelegatedScopes.Space && spaceKey != Guid.Empty)
        {
            return false;
        }

        if (root)
        {
            return true;
        }

        return scope switch
        {
            DelegatedScopes.Tenant => Holds(held, DelegatedScopes.Tenant),
            DelegatedScopes.Compliance => Holds(held, DelegatedScopes.Tenant) || Holds(held, DelegatedScopes.Compliance),
            DelegatedScopes.Security => Holds(held, DelegatedScopes.Tenant) || Holds(held, DelegatedScopes.Security),
            DelegatedScopes.Workspace or DelegatedScopes.BillingRead =>
                Holds(held, DelegatedScopes.Tenant) || Holds(held, DelegatedScopes.Workspace),
            DelegatedScopes.Space => Holds(held, DelegatedScopes.Tenant)
                || Holds(held, DelegatedScopes.Workspace)
                || held.Any(x => x.Scope == DelegatedScopes.Space && x.SpaceKey == spaceKey),
            _ => false
        };
    }

    public static bool CanManageSpace(bool root, IReadOnlyList<HeldScope> held, Guid spaceId) =>
        root
        || Holds(held, DelegatedScopes.Tenant)
        || Holds(held, DelegatedScopes.Workspace)
        || held.Any(x => x.Scope == DelegatedScopes.Space && x.SpaceKey == spaceId);

    public static bool CanEditQuotas(bool root, IReadOnlyList<HeldScope> held) =>
        root || Holds(held, DelegatedScopes.Tenant) || Holds(held, DelegatedScopes.Workspace);

    public static bool CanView(bool root, IReadOnlyList<HeldScope> held) => root || held.Count > 0;

    public static bool CanGrantSomething(bool root, IReadOnlyList<HeldScope> held) =>
        root || held.Any(x => x.Scope != DelegatedScopes.BillingRead);

    public static IReadOnlyList<string> Areas(bool root, IReadOnlyList<HeldScope> held)
    {
        if (root)
        {
            return DelegatedScopes.All;
        }

        return DelegatedScopes.All.Where(scope => Holds(held, scope)).ToArray();
    }

    public static bool Allows(IReadOnlyList<HeldScope> held, string permission, Guid? spaceId)
    {
        if (permission is Permissions.Workspace.Admin or Permissions.Workspace.Manage or Permissions.Admin.Dashboard)
        {
            return Holds(held, DelegatedScopes.Tenant) || Holds(held, DelegatedScopes.Workspace);
        }

        if (permission is Permissions.Compliance.Hold
            or Permissions.Compliance.Discovery
            or Permissions.Compliance.Dlp
            or Permissions.Compliance.Policy)
        {
            return Holds(held, DelegatedScopes.Tenant) || Holds(held, DelegatedScopes.Compliance);
        }

        if (permission == Permissions.Security.AuditExport)
        {
            return Holds(held, DelegatedScopes.Tenant) || Holds(held, DelegatedScopes.Security);
        }

        if (permission == Permissions.Billing.Read)
        {
            return Holds(held, DelegatedScopes.Tenant)
                || Holds(held, DelegatedScopes.Workspace)
                || Holds(held, DelegatedScopes.BillingRead);
        }

        if (permission is Permissions.Channel.Manage or Permissions.Channel.Create)
        {
            return Holds(held, DelegatedScopes.Tenant)
                || Holds(held, DelegatedScopes.Workspace)
                || (spaceId is Guid id && held.Any(x => x.Scope == DelegatedScopes.Space && x.SpaceKey == id));
        }

        return false;
    }

    public static string Trend(long used, long previous) =>
        used > previous ? "up" : used < previous ? "down" : "flat";

    public static string Action(bool configured, long used, long soft, long hard)
    {
        if (!configured)
        {
            return "none";
        }

        if (used >= hard)
        {
            return "blocked";
        }

        return used >= soft ? "warning" : "none";
    }

    private static bool Holds(IReadOnlyList<HeldScope> held, string scope) =>
        held.Any(x => x.Scope == scope);
}
