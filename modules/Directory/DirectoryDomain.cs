using VibeChat.SharedKernel;

namespace VibeChat.Directory;

public sealed class Space : AggregateRoot
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
