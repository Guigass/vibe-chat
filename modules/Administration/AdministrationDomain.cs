namespace VibeChat.Administration;

public sealed record DashboardStats(
    int WorkspaceCount,
    int UserCount,
    int ChannelCount,
    int MessageCount,
    int PendingOutboxCount);

public interface IDashboardQuery
{
    Task<DashboardStats> GetStatsAsync(CancellationToken cancellationToken);
}
