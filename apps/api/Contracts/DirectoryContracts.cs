public sealed record WorkspaceResponse(Guid Id, string Name, string Slug, string Role);
public sealed record SpaceResponse(Guid Id, Guid WorkspaceId, string Name, int Order);
public sealed record WorkspaceMemberResponse(Guid UserId, string DisplayName, string Email, string Role);
public sealed record WorkspaceRolesResponse(string[] AssignableRoles);
public sealed record UpdateMemberRoleRequest(string Role);
public sealed record InviteMemberRequest(string Email, string? DisplayName = null, string? Role = null);
