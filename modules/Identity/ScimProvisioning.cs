using System.Security.Cryptography;
using System.Text;
using VibeChat.SharedKernel;

namespace VibeChat.Identity;

/// <summary>SCIM 2.0 provisioning (B-128). Off by default. The Keycloak adapter is optional.</summary>
public sealed class ScimOptions
{
    public const string SectionName = "Identity:Scim";

    public bool Enabled { get; set; }

    /// <summary>
    /// When false, deprovision still revokes local sessions and memberships.
    /// A missing IdP adapter must not block SCIM.
    /// </summary>
    public bool KeycloakAdapter { get; set; }
}

public static class ScimPolicies
{
    public const string TokenPrefix = "vc_scim_";
    public const int TokenBytes = 32;
    public const int MaxTokensPerTenant = 20;
    public const int MaxNameLength = 80;
    public const int MaxUserNameLength = 256;
    public const int MaxExternalIdLength = 256;
    public const int MaxDisplayNameLength = 128;
    public const int PageMax = 100;
    public const int BulkMax = 20;
    public const string Scopes = "users groups";

    public static readonly Role[] MappedRoles = [Role.Member, Role.Moderator, Role.Auditor, Role.Guest];

    public static bool IsMappedRole(Role role) => MappedRoles.Contains(role);

    public static string? NormalizeName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is 0 or > MaxNameLength || HasControl(value))
        {
            return null;
        }

        return value;
    }

    public static string? NormalizeUserName(string? userName)
    {
        var value = (userName ?? string.Empty).Trim();
        if (value.Contains('@'))
        {
            value = value.ToLowerInvariant();
        }

        if (value.Length is 0 or > MaxUserNameLength || HasControl(value))
        {
            return null;
        }

        return value;
    }

    public static string? NormalizeExternalId(string? externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        var value = externalId.Trim();
        if (value.Length > MaxExternalIdLength || HasControl(value))
        {
            return null;
        }

        return value;
    }

    public static bool ExternalIdRejected(string? externalId) =>
        !string.IsNullOrWhiteSpace(externalId) && NormalizeExternalId(externalId) is null;

    public static string NormalizeDisplayName(string? displayName, string userName)
    {
        var value = (displayName ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            value = userName;
        }

        if (value.Length > MaxDisplayNameLength)
        {
            value = value[..MaxDisplayNameLength];
        }

        return HasControl(value) ? userName : value;
    }

    public static (int StartIndex, int Count) NormalizePage(int? startIndex, int? count)
    {
        var start = startIndex is null or < 1 ? 1 : startIndex.Value;
        var size = count is null or < 1 ? PageMax : count.Value;
        if (size > PageMax)
        {
            size = PageMax;
        }

        return (start, size);
    }

    private static bool HasControl(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                return true;
            }
        }

        return false;
    }
}

public static class ScimTokenCodec
{
    public static string CreateRaw()
    {
        Span<byte> bytes = stackalloc byte[ScimPolicies.TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        return ScimPolicies.TokenPrefix + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Last4(string raw)
    {
        var value = raw.Trim();
        return value.Length <= 4 ? value : value[^4..];
    }
}

public static class ScimSchemas
{
    public const string User = "urn:ietf:params:scim:schemas:core:2.0:User";
    public const string Group = "urn:ietf:params:scim:schemas:core:2.0:Group";
    public const string List = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    public const string Error = "urn:ietf:params:scim:api:messages:2.0:Error";
    public const string BulkRequest = "urn:ietf:params:scim:api:messages:2.0:BulkRequest";
    public const string BulkResponse = "urn:ietf:params:scim:api:messages:2.0:BulkResponse";
    public const string Patch = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
    public const string ServiceProviderConfig = "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig";
}

public enum ScimFilterField
{
    UserName,
    ExternalId,
    DisplayName
}

public static class ScimFilter
{
    public static bool TryParse(string? filter, out ScimFilterField field, out string value, out string error)
    {
        field = default;
        value = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var text = filter.Trim();
        var marker = " eq \"";
        var split = text.IndexOf(marker, StringComparison.Ordinal);
        if (split <= 0 || !text.EndsWith('"') || text.Length < split + marker.Length + 1)
        {
            error = "invalidFilter";
            return false;
        }

        var name = text[..split];
        field = name switch
        {
            "userName" => ScimFilterField.UserName,
            "externalId" => ScimFilterField.ExternalId,
            "displayName" => ScimFilterField.DisplayName,
            _ => default
        };
        if (name is not ("userName" or "externalId" or "displayName"))
        {
            error = "invalidFilter";
            return false;
        }

        value = text[(split + marker.Length)..^1];
        if (value.Contains('"') || ScimPolicies.ExternalIdRejected(value) && name == "externalId")
        {
            error = "invalidFilter";
            return false;
        }

        return true;
    }
}

public sealed class ScimToken : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Scopes { get; set; } = ScimPolicies.Scopes;
    public UserId CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class ScimPrincipal : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public string? ExternalId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Active { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ScimGroup : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string? ExternalId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ScimGroupMember : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid GroupId { get; set; }
    public Guid PrincipalId { get; set; }
}

public sealed class ScimGroupMapping : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid GroupId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Role Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Membership created by a group mapping. Manual memberships have no grant.</summary>
public sealed class ScimMembershipGrant : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid GroupId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public UserId UserId { get; set; }
    public Role Role { get; set; }
}

/// <summary>Local session kill for a deprovisioned principal. Independent of Keycloak.</summary>
public sealed class AccessRevocation : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public UserId UserId { get; set; }
    public DateTimeOffset RevokedAt { get; set; }
    public string Reason { get; set; } = "scim";
}

public sealed record ProvisionUserCommand(
    TenantId TenantId,
    Guid? PrincipalId,
    string? ExternalId,
    string? UserName,
    string? DisplayName,
    bool Active,
    string Source,
    UserId? ActorUserId);

public sealed record ProvisionUserResult(
    bool Ok,
    string? Error,
    Guid PrincipalId,
    UserId UserId,
    bool Created,
    bool Deactivated,
    string UserName,
    string DisplayName,
    string? ExternalId,
    bool Active);

public sealed record ProvisionGroupCommand(
    TenantId TenantId,
    Guid? GroupId,
    string? ExternalId,
    string? DisplayName,
    IReadOnlyList<Guid>? MemberPrincipalIds,
    bool Remove,
    string Source,
    UserId? ActorUserId);

public sealed record ProvisionGroupResult(
    bool Ok,
    string? Error,
    Guid GroupId,
    bool Created,
    bool Removed,
    string DisplayName,
    string? ExternalId,
    IReadOnlyList<Guid> MemberIds);

public sealed record MappingEffect(Guid UserId, string CurrentRole, string MappedRole);

public sealed record MappingPreview(
    bool Ok,
    string? Error,
    Guid? MappingId,
    IReadOnlyList<MappingEffect> Adds,
    IReadOnlyList<MappingEffect> Conflicts);

/// <summary>
/// Canonical provisioning port. SCIM and the admin command both call this.
/// Adapters must not write membership tables themselves.
/// </summary>
public interface IProvisioningPort
{
    Task<ProvisionUserResult> ApplyUserAsync(ProvisionUserCommand command, CancellationToken cancellationToken);

    Task<ProvisionGroupResult> ApplyGroupAsync(ProvisionGroupCommand command, CancellationToken cancellationToken);

    Task<MappingPreview> PreviewMappingAsync(
        TenantId tenantId,
        Guid groupId,
        WorkspaceId workspaceId,
        Role role,
        CancellationToken cancellationToken);

    Task<MappingPreview> ApplyMappingAsync(
        TenantId tenantId,
        Guid groupId,
        WorkspaceId workspaceId,
        Role role,
        UserId actorUserId,
        CancellationToken cancellationToken);

    Task<MappingPreview> RemoveMappingAsync(
        TenantId tenantId,
        Guid mappingId,
        WorkspaceId workspaceId,
        UserId actorUserId,
        CancellationToken cancellationToken);
}

/// <summary>Optional IdP session logout. The default does nothing and never fails the command.</summary>
public interface IExternalSessionRevoker
{
    Task RevokeAsync(UserId userId, CancellationToken cancellationToken);
}
