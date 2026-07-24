using VibeChat.SharedKernel;

namespace VibeChat.Realtime;

public sealed record RealtimeMessage(
    string EventName,
    TenantId TenantId,
    ChannelId ChannelId,
    object Payload);

public interface IChatPublisher
{
    Task PublishAsync(RealtimeMessage message, CancellationToken cancellationToken);
}

public interface ITypingService
{
    Task SetTypingAsync(TenantId tenantId, ChannelId channelId, UserId userId, string displayName, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TypingUser>> GetTypingAsync(TenantId tenantId, ChannelId channelId, CancellationToken cancellationToken);
}

public sealed record TypingUser(UserId UserId, string DisplayName, DateTimeOffset ExpiresAt);

public interface IPresenceService
{
    Task SetOnlineAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken);
    Task SetOfflineAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken);
    Task<int> CountOnlineAsync(TenantId tenantId, CancellationToken cancellationToken);
}
