using Microsoft.Extensions.DependencyInjection;

namespace VibeChat.Infrastructure;

internal static class HealthCheckServiceCollectionExtensions
{
    internal static IServiceCollection AddInfrastructureHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres")
            .AddCheck<RedisHealthCheck>("redis")
            .AddCheck<MinioHealthCheck>("minio");
        return services;
    }
}
