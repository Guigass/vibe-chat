using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VibeChat.BuildingBlocks;

namespace VibeChat.Infrastructure;

public sealed class RedisRateLimiter(
    RedisConnection redis,
    ILogger<RedisRateLimiter> logger) : IRateLimiter
{
    public async Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return true;
        }

        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return true;
        }

        try
        {
            var count = await db.StringIncrementAsync(key);
            if (count == 1)
            {
                await db.KeyExpireAsync(key, window);
            }

            return count <= limit;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rate limiter failed for key {RateLimitKey}; allowing request", key);
            return true;
        }
    }
}
