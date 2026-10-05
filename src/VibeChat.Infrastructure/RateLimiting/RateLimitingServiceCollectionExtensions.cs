using Microsoft.Extensions.DependencyInjection;
using VibeChat.BuildingBlocks;

namespace VibeChat.Infrastructure;

internal static class RateLimitingServiceCollectionExtensions
{
    internal static IServiceCollection AddRateLimitingInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        return services;
    }
}
