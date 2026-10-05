using VibeChat.SharedKernel;

namespace VibeChat.Messaging;

/// <summary>
/// Workspace (tenant) policy for who may edit or soft-delete a message (B-107).
/// A missing row means <see cref="MessageLifecyclePolicyRules.Default"/> — today's behavior.
/// </summary>
public sealed class MessageLifecyclePolicy
{
    public TenantId TenantId { get; set; }
    public bool EditEnabled { get; set; } = true;
    public int? EditWindowMinutes { get; set; }
    public string[] EditRoles { get; set; } = [];
    public bool EditRolesRestricted { get; set; }
    public bool EditAllowModeratorOverride { get; set; }
    public bool DeleteEnabled { get; set; } = true;
    public int? DeleteWindowMinutes { get; set; }
    public string[] DeleteRoles { get; set; } = [];
    public bool DeleteRolesRestricted { get; set; }
    public bool DeleteAllowModeratorOverride { get; set; } = true;
    /// <summary>Members with <c>message.history.read</c> may open the version list (B-114). Recording follows the process flag.</summary>
    public bool HistoryEnabled { get; set; } = true;
    /// <summary>Leave a neutral tombstone in the previous channel when a message is moved.</summary>
    public bool LeaveTombstone { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record MessagingPolicyDto(
    bool EditEnabled,
    int? EditWindowMinutes,
    bool EditRolesRestricted,
    string[] EditRoles,
    bool EditAllowModeratorOverride,
    bool DeleteEnabled,
    int? DeleteWindowMinutes,
    bool DeleteRolesRestricted,
    string[] DeleteRoles,
    bool DeleteAllowModeratorOverride,
    bool HistoryEnabled = true,
    bool LeaveTombstone = true);

public readonly record struct MessageLifecycleSnapshot(
    bool EditEnabled,
    int? EditWindowMinutes,
    bool EditRolesRestricted,
    IReadOnlyList<string> EditRoles,
    bool EditAllowModeratorOverride,
    bool DeleteEnabled,
    int? DeleteWindowMinutes,
    bool DeleteRolesRestricted,
    IReadOnlyList<string> DeleteRoles,
    bool DeleteAllowModeratorOverride,
    bool HistoryEnabled = true,
    bool LeaveTombstone = true);

public static class MessageLifecyclePolicyRules
{
    public const int MaxWindowMinutes = 525_600;
    public const string Invalid = "InvalidMessagingPolicy";
    public const string EditDisabled = "EditDisabled";
    public const string EditRoleDenied = "EditRoleDenied";
    public const string EditWindowExpired = "EditWindowExpired";
    public const string DeleteDisabled = "DeleteDisabled";
    public const string DeleteRoleDenied = "DeleteRoleDenied";
    public const string DeleteWindowExpired = "DeleteWindowExpired";
    public const string Forbidden = "Forbidden";

    public static readonly string[] ConfigurableRoles = ["Member", "Moderator", "Admin", "Auditor", "Guest"];
    public static readonly string[] FormDefaultRoles = ["Member", "Moderator", "Admin"];

    public static MessageLifecycleSnapshot Default { get; } = new(
        EditEnabled: true,
        EditWindowMinutes: null,
        EditRolesRestricted: false,
        EditRoles: [],
        EditAllowModeratorOverride: false,
        DeleteEnabled: true,
        DeleteWindowMinutes: null,
        DeleteRolesRestricted: false,
        DeleteRoles: [],
        DeleteAllowModeratorOverride: true,
        HistoryEnabled: true,
        LeaveTombstone: true);

    public static MessageLifecycleSnapshot SnapshotOf(MessageLifecyclePolicy? row) =>
        row is null
            ? Default
            : new MessageLifecycleSnapshot(
                row.EditEnabled,
                row.EditWindowMinutes,
                row.EditRolesRestricted,
                row.EditRoles,
                row.EditAllowModeratorOverride,
                row.DeleteEnabled,
                row.DeleteWindowMinutes,
                row.DeleteRolesRestricted,
                row.DeleteRoles,
                row.DeleteAllowModeratorOverride,
                row.HistoryEnabled,
                row.LeaveTombstone);

    public static MessagingPolicyDto ToDto(MessageLifecyclePolicy? row)
    {
        var snapshot = SnapshotOf(row);
        return new MessagingPolicyDto(
            snapshot.EditEnabled,
            snapshot.EditWindowMinutes,
            snapshot.EditRolesRestricted,
            snapshot.EditRolesRestricted ? snapshot.EditRoles.ToArray() : FormDefaultRoles,
            snapshot.EditAllowModeratorOverride,
            snapshot.DeleteEnabled,
            snapshot.DeleteWindowMinutes,
            snapshot.DeleteRolesRestricted,
            snapshot.DeleteRolesRestricted ? snapshot.DeleteRoles.ToArray() : FormDefaultRoles,
            snapshot.DeleteAllowModeratorOverride,
            snapshot.HistoryEnabled,
            snapshot.LeaveTombstone);
    }

    public static string? NormalizeWindow(int? minutes) =>
        minutes is null || minutes is >= 1 and <= MaxWindowMinutes ? null : Invalid;

    public static string? NormalizeRoles(IReadOnlyList<string>? roles, out string[] normalized)
    {
        normalized = [];
        if (roles is null)
        {
            return Invalid;
        }

        var list = new List<string>(roles.Count);
        foreach (var raw in roles)
        {
            var match = ConfigurableRoles.FirstOrDefault(role =>
                role.Equals(raw?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return Invalid;
            }

            if (!list.Contains(match, StringComparer.Ordinal))
            {
                list.Add(match);
            }
        }

        normalized = list.ToArray();
        return null;
    }

    /// <summary>
    /// Author path respects enabled, role allow-list and window.
    /// Moderator override (edit/delete any) applies only to someone else's message and skips the window.
    /// Returns null when allowed, <see cref="Forbidden"/> for the pre-existing 403, or a stable policy code.
    /// </summary>
    public static string? EvaluateEdit(
        MessageLifecycleSnapshot policy,
        bool isAuthor,
        bool hasOwn,
        bool hasAny,
        Role role,
        DateTimeOffset createdAt,
        DateTimeOffset now)
    {
        if (!isAuthor)
        {
            return policy.EditAllowModeratorOverride && hasAny ? null : Forbidden;
        }

        if (!policy.EditEnabled)
        {
            return EditDisabled;
        }

        if (!hasOwn)
        {
            return Forbidden;
        }

        if (!RoleAllowed(policy.EditRolesRestricted, policy.EditRoles, role))
        {
            return EditRoleDenied;
        }

        return WindowExpired(policy.EditWindowMinutes, createdAt, now) ? EditWindowExpired : null;
    }

    public static string? EvaluateDelete(
        MessageLifecycleSnapshot policy,
        bool isAuthor,
        bool hasOwn,
        bool hasAny,
        Role role,
        DateTimeOffset createdAt,
        DateTimeOffset now)
    {
        if (!isAuthor)
        {
            return policy.DeleteAllowModeratorOverride && hasAny ? null : Forbidden;
        }

        if (!policy.DeleteEnabled)
        {
            return DeleteDisabled;
        }

        if (!hasOwn)
        {
            return Forbidden;
        }

        if (!RoleAllowed(policy.DeleteRolesRestricted, policy.DeleteRoles, role))
        {
            return DeleteRoleDenied;
        }

        return WindowExpired(policy.DeleteWindowMinutes, createdAt, now) ? DeleteWindowExpired : null;
    }

    public static bool RoleAllowed(bool restricted, IReadOnlyList<string> roles, Role role)
    {
        if (!restricted)
        {
            return true;
        }

        if (roles.Any(item => item.Equals(role.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return role is Role.WorkspaceOwner or Role.PlatformOwner
            && roles.Any(item => item.Equals(nameof(Role.Admin), StringComparison.OrdinalIgnoreCase));
    }

    public static bool WindowExpired(int? minutes, DateTimeOffset createdAt, DateTimeOffset now) =>
        minutes is int window && now > createdAt.AddMinutes(window);
}
