using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using StackExchange.Redis;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Directory;
using VibeChat.Files;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Realtime;
using NpgsqlTypes;
using VibeChat.Search;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;
using Role = VibeChat.SharedKernel.Role;

namespace VibeChat.Infrastructure;

public sealed class RedisConnection : IAsyncDisposable
{
    private readonly string? _connectionString;
    private readonly Lazy<Task<IConnectionMultiplexer?>> _connection;

    public RedisConnection(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Redis");
        _connection = new Lazy<Task<IConnectionMultiplexer?>>(ConnectAsync);
    }

    public async Task<IDatabase?> GetDatabaseAsync()
    {
        var connection = await _connection.Value;
        return connection?.GetDatabase();
    }

    public async Task<ISubscriber?> GetSubscriberAsync()
    {
        var connection = await _connection.Value;
        return connection?.GetSubscriber();
    }

    private async Task<IConnectionMultiplexer?> ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return null;
        }

        return await ConnectionMultiplexer.ConnectAsync(_connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && await _connection.Value is { } connection)
        {
            await connection.CloseAsync();
            connection.Dispose();
        }
    }
}

public sealed class TypingService(RedisConnection redis, IClock clock) : ITypingService
{
    public async Task SetTypingAsync(TenantId tenantId, ChannelId channelId, UserId userId, string displayName, CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return;
        }

        var key = Key(tenantId, channelId);
        var expiresAt = clock.UtcNow.AddSeconds(5);
        await db.HashSetAsync(key, userId.ToString(), JsonSerializer.Serialize(new TypingUser(userId, displayName, expiresAt)));
        await db.KeyExpireAsync(key, TimeSpan.FromSeconds(10));
    }

    public async Task<IReadOnlyCollection<TypingUser>> GetTypingAsync(TenantId tenantId, ChannelId channelId, CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return [];
        }

        var entries = await db.HashGetAllAsync(Key(tenantId, channelId));
        var now = clock.UtcNow;
        return entries
            .Select(x => JsonSerializer.Deserialize<TypingUser>(x.Value.ToString()))
            .Where(x => x is not null && x.ExpiresAt > now)
            .Cast<TypingUser>()
            .ToArray();
    }

    private static string Key(TenantId tenantId, ChannelId channelId) => RedisKeys.Typing(tenantId, channelId);
}

/// <summary>
/// Redis key helpers — always tenant-first (<c>t:{tenantId}:…</c>) per multi-tenant.md.
/// </summary>
public static class RedisKeys
{
    public static string Typing(TenantId tenantId, ChannelId channelId) =>
        $"t:{tenantId.Value}:typing:{channelId.Value}";

    public static string PresenceStatus(TenantId tenantId, UserId userId) =>
        $"t:{tenantId.Value}:presence:status:{userId.Value}";

    public static string PresenceConnections(TenantId tenantId, UserId userId) =>
        $"t:{tenantId.Value}:presence:conn:{userId.Value}";

    public static string PresenceUsers(TenantId tenantId) =>
        $"t:{tenantId.Value}:presence:users";
}

public sealed class PresenceService(RedisConnection redis, IClock clock) : IPresenceService
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(45);

    public Task SetOnlineAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, userId, connectionId, PresenceStatus.Online, cancellationToken);

    public Task SetAwayAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, userId, connectionId, PresenceStatus.Away, cancellationToken);

    public Task HeartbeatAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, userId, connectionId, PresenceStatus.Online, cancellationToken);

    public async Task SetOfflineAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return;
        }

        await db.SetRemoveAsync(ConnectionsKey(tenantId, userId), connectionId);
        var remaining = await db.SetLengthAsync(ConnectionsKey(tenantId, userId));
        if (remaining == 0)
        {
            await db.KeyDeleteAsync(StatusKey(tenantId, userId));
            await db.SetRemoveAsync(UsersKey(tenantId), userId.Value.ToString());
        }
    }

    public async Task<int> CountOnlineAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return 0;
        }

        var userIds = await db.SetMembersAsync(UsersKey(tenantId));
        var count = 0;
        foreach (var entry in userIds)
        {
            if (!Guid.TryParse(entry.ToString(), out var userGuid))
            {
                continue;
            }

            var status = await ReadStatusAsync(db, tenantId, new UserId(userGuid));
            if (status is PresenceStatus.Online or PresenceStatus.Away)
            {
                count++;
            }
        }

        return count;
    }

    public async Task<IReadOnlyDictionary<UserId, PresenceStatus>> GetStatusesAsync(
        TenantId tenantId,
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        var result = new Dictionary<UserId, PresenceStatus>();
        if (db is null)
        {
            foreach (var userId in userIds)
            {
                result[userId] = PresenceStatus.Offline;
            }

            return result;
        }

        foreach (var userId in userIds)
        {
            result[userId] = await ReadStatusAsync(db, tenantId, userId) ?? PresenceStatus.Offline;
        }

        return result;
    }

    public async Task<IReadOnlyList<string>> GetConnectionIdsAsync(
        TenantId tenantId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return [];
        }

        var members = await db.SetMembersAsync(ConnectionsKey(tenantId, userId));
        return members
            .Select(x => x.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private async Task SetStatusAsync(
        TenantId tenantId,
        UserId userId,
        string connectionId,
        PresenceStatus status,
        CancellationToken cancellationToken)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return;
        }

        var expiresAt = clock.UtcNow.Add(PresenceTtl);
        var payload = JsonSerializer.Serialize(new PresenceEntry(userId, status, expiresAt));
        await db.StringSetAsync(StatusKey(tenantId, userId), payload, PresenceTtl);
        await db.SetAddAsync(ConnectionsKey(tenantId, userId), connectionId);
        await db.KeyExpireAsync(ConnectionsKey(tenantId, userId), PresenceTtl);
        await db.SetAddAsync(UsersKey(tenantId), userId.Value.ToString());
        await db.KeyExpireAsync(UsersKey(tenantId), PresenceTtl.Add(TimeSpan.FromMinutes(5)));
    }

    private async Task<PresenceStatus?> ReadStatusAsync(IDatabase db, TenantId tenantId, UserId userId)
    {
        var raw = await db.StringGetAsync(StatusKey(tenantId, userId));
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        var entry = JsonSerializer.Deserialize<PresenceEntry>(raw.ToString());
        if (entry is null || entry.ExpiresAt <= clock.UtcNow)
        {
            return null;
        }

        return entry.Status;
    }

    private static string StatusKey(TenantId tenantId, UserId userId) => RedisKeys.PresenceStatus(tenantId, userId);
    private static string ConnectionsKey(TenantId tenantId, UserId userId) => RedisKeys.PresenceConnections(tenantId, userId);
    private static string UsersKey(TenantId tenantId) => RedisKeys.PresenceUsers(tenantId);
}
