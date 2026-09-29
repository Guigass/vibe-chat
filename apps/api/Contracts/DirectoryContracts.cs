public sealed record WorkspaceResponse(Guid Id, string Name, string Slug, string Role);
public sealed record SpaceResponse(Guid Id, Guid WorkspaceId, string Name, int Order);
public sealed record WorkspaceMemberResponse(Guid UserId, string DisplayName, string Email, string Role);
public sealed record WorkspaceRolesResponse(string[] AssignableRoles);
public sealed record UpdateMemberRoleRequest(string Role);
public sealed record InviteMemberRequest(string Email, string? DisplayName = null, string? Role = null);
public sealed record CreateChannelInviteRequest(string? Email = null, int? ExpiresInDays = null);
public sealed record ChannelInviteCreatedResponse(Guid Id, string Url, DateTimeOffset ExpiresAt);
public sealed record ChannelInviteResponse(
    Guid Id,
    string? Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    Guid? AcceptedByUserId,
    DateTimeOffset? RevokedAt,
    string Status);
public sealed record ChannelGuestResponse(Guid UserId, string DisplayName, string Email, DateTimeOffset JoinedAt);
public sealed record ChannelInvitesPageResponse(ChannelInviteResponse[] Invites, ChannelGuestResponse[] Guests);
public sealed record AcceptInviteResponse(Guid ChannelId, Guid WorkspaceId, string ChannelName);
