using VibeChat.SharedKernel;

namespace VibeChat.Directory;

/// <summary>
/// How a contact group is shared. Department is workspace-wide; personal is owner-only.
/// A contact group never grants channel, DM, or workspace permissions (B-166).
/// </summary>
public enum ContactGroupKind
{
    Department,
    Personal
}

/// <summary>
/// Grouping of people in the workspace contact list. Not a Space and not an ACL.
/// </summary>
public sealed class ContactGroup : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public ContactGroupKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public UserId? OwnerUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Assignment of a workspace member to a contact group. Unique per group and user.</summary>
public sealed class ContactGroupMember : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid GroupId { get; set; }
    public UserId UserId { get; set; }
}

public static class ContactGroupPolicies
{
    public const int MaxNameLength = 80;
    public const int MaxMemberCount = 500;
    public const int MaxOrder = 10_000;
    public const string DepartmentWire = "department";
    public const string PersonalWire = "personal";

    public static bool TryParseKind(string? value, out ContactGroupKind kind)
    {
        if (string.Equals(value, DepartmentWire, StringComparison.OrdinalIgnoreCase))
        {
            kind = ContactGroupKind.Department;
            return true;
        }

        if (string.Equals(value, PersonalWire, StringComparison.OrdinalIgnoreCase))
        {
            kind = ContactGroupKind.Personal;
            return true;
        }

        kind = default;
        return false;
    }

    public static string ToWire(ContactGroupKind kind) =>
        kind == ContactGroupKind.Personal ? PersonalWire : DepartmentWire;

    public static ContactGroupKind ParseWire(string value) =>
        TryParseKind(value, out var kind)
            ? kind
            : throw new InvalidOperationException($"Unknown contact group kind '{value}'.");

    /// <summary>Trimmed name, or null when empty or longer than <see cref="MaxNameLength"/>.</summary>
    public static string? NormalizeName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0 || value.Length > MaxNameLength)
        {
            return null;
        }

        return value;
    }

    public static bool NamesConflict(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    public static UserId? EffectiveOwner(ContactGroupKind kind, UserId caller) =>
        kind == ContactGroupKind.Personal ? caller : null;

    public static bool CanRead(ContactGroupKind kind, Guid? ownerUserId, Guid callerUserId) =>
        kind == ContactGroupKind.Department || ownerUserId == callerUserId;

    public static bool CanWrite(ContactGroupKind kind, Guid? ownerUserId, Guid callerUserId, bool isWorkspaceAdmin) =>
        kind == ContactGroupKind.Department
            ? isWorkspaceAdmin
            : ownerUserId == callerUserId;

    public static bool OrderInRange(int order) => order >= 0 && order <= MaxOrder;
}
