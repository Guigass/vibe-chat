using VibeChat.SharedKernel;

namespace VibeChat.Tenancy;

public sealed class Workspace : AggregateRoot
{
    public WorkspaceId Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool AiEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class WorkspaceMember : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public UserId UserId { get; set; }
    public Role Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public interface IWorkspaceMembershipReader
{
    Task<bool> IsMemberAsync(TenantId tenantId, WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Role>> GetRolesAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken);
}
