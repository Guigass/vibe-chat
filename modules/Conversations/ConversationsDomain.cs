using VibeChat.SharedKernel;

namespace VibeChat.Conversations;

public enum ChannelType
{
    Public = 0,
    Private = 1,
    Announcement = 2,
    Direct = 3,
    Group = 4
}

public sealed class Channel : AggregateRoot
{
    public ChannelId Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid? SpaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelType Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public UserId CreatedBy { get; set; }
}

public sealed class ChannelMember : Entity
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public ChannelId ChannelId { get; set; }
    public UserId UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public interface IChannelMembershipReader
{
    Task<bool> CanAccessAsync(TenantId tenantId, ChannelId channelId, UserId userId, CancellationToken cancellationToken);
}
