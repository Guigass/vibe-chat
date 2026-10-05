using Microsoft.Extensions.DependencyInjection;

namespace VibeChat.Infrastructure;

internal static class OutboxServiceCollectionExtensions
{
    internal static IServiceCollection AddOutboxInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }

    internal static IServiceCollection AddMessageRetentionInfrastructure(this IServiceCollection services)
    {
        // B-047: processor shared; hosted purge loop is registered only in apps/worker.
        services.AddSingleton<MessageRetentionPurgeProcessor>();
        return services;
    }
}
